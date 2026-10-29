using System.Collections.Concurrent;
using System.Diagnostics;
using SkiaSharp;
using Tesseract;

namespace Common.Ocr;

/// <summary>1 từ đọc được. <see cref="Bounds"/> theo toạ độ ảnh gốc.</summary>
public sealed record OcrWord(string Text, SKRectI Bounds, float Confidence);

/// <summary>1 dòng chữ = các từ liền nhau trên cùng dòng (theo Tesseract).</summary>
public sealed record OcrLine(IReadOnlyList<OcrWord> Words, SKRectI Bounds)
{
    /// <summary>Chữ của dòng (xem <see cref="OcrText.JoinLine"/>: chữ Nhật không chen dấu cách).</summary>
    public string Text => OcrText.JoinLine(Words.Select(w => w.Text));
}

public sealed record OcrResult(IReadOnlyList<OcrLine> Lines, TimeSpan Elapsed)
{
    /// <summary>Toàn bộ chữ, mỗi dòng 1 dòng (thứ tự trên → dưới).</summary>
    public string FullText => string.Join(Environment.NewLine, Lines.Select(l => l.Text));
}

/// <summary>Đọc chữ trong ảnh bằng Tesseract (dữ liệu tessdata_fast, thư mục <c>tessdata</c> cạnh exe): đọc cả trang
/// bằng "vie" (đúng dấu tiếng Việt hơn "vie+eng", nhanh hơn ~1,5 lần), dòng chữ Anh đọc lại bằng "eng" (xem dưới).
///
/// - Chữ màn hình nhỏ (x-height 6–8 px) Tesseract đọc kém → ảnh không quá rộng được phóng ×2 trước khi đọc
///   (thử nghiệm: ×2 đọc đúng dấu tiếng Việt, ×3 không hơn mà chậm hơn).
/// - Chữ sáng trên nền tối: dải nền tối được đảo màu cả dải; dòng đọc kém trong dải nền sáng (tiêu đề, nút bấm chữ
///   trắng trên nền màu) được đọc lại riêng với màu đảo, giữ bản tin cậy hơn.
/// - Ảnh dài (trang cuộn 30.000 px) cắt thành các dải ngang chồng lên nhau 1 đoạn, đọc song song mỗi dải 1
///   engine; 1 dòng chữ thuộc về dải chứa tâm của nó (phần lõi, không tính đoạn chồng) → không mất / không lặp
///   dòng ở chỗ nối. Huỷ được giữa các dải (Tesseract không huỷ được giữa chừng 1 lần đọc).
/// - Engine khởi tạo mất ~50–200 ms → giữ lại trong pool dùng cho lần sau (TesseractEngine không thread-safe:
///   mỗi lúc 1 engine chỉ đọc 1 dải).
/// - Không lọc từng từ theo độ tin cậy: từ đúng đôi khi chỉ được 12–14 điểm ("Khách hàng H") - chỉ bỏ cả dòng
///   khi trung bình quá thấp (rác đọc từ icon / hình vẽ).
/// - Dòng không có chữ tiếng Việt có dấu (chữ Anh, đường dẫn, URL) đọc lại bằng "eng", giữ bản tin cậy hơn: "vie" đọc
///   sai chữ Anh trên màn hình ("C:\Users\x\vAppData", "this ¡ssue", "localhost8020", "Ver2.I"). Không đọc cả trang bằng
///   "vie+eng" vì mất dấu tiếng Việt ("Trạng thai", "Tach file"); dòng có dấu giữ nguyên bản "vie" (xem PLAN.md ImageCompare).</summary>
public static class TextRecognizer
{
    private const string Languages = "vie";
    private const string EnglishLanguages = "eng";
    private const int StripHeight = 600; // px ảnh gốc - mỗi dải ~0,3–0,5 s → huỷ nhanh, báo tiến độ mịn
    private const int StripOverlap = 80;  // > chiều cao 1 dòng chữ lớn
    private const float RetryBelow = 50;  // độ tin cậy trung bình của dòng dưới mức này → đọc lại với màu đảo
    private const float DropBelow = 15;   // sau khi đọc lại vẫn dưới mức này → bỏ dòng

    private static readonly ConcurrentBag<TesseractEngine> Pool = [];
    private static readonly ConcurrentBag<TesseractEngine> EnglishPool = [];
    private const string VietnameseLetters = "àáảãạăằắẳẵặâầấẩẫậđèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵ";

    /// <summary>Thư mục chứa *.traineddata.</summary>
    public static string DataPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "tessdata");

    /// <param name="progress">Phần đã đọc 0–1 (sau mỗi dải).</param>
    public static OcrResult Recognize(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null) =>
        Recognize(bitmap, bitmap.Width <= 2400 ? 2 : 1, ct, progress);

    private static OcrResult Recognize(SKBitmap bitmap, int scale, CancellationToken ct, IProgress<double>? progress)
    {
        var sw = Stopwatch.StartNew();
        var strips = new List<(int Top, int Bottom, int CoreTop, int CoreBottom)>();
        for (int core = 0; core < bitmap.Height; core += StripHeight)
        {
            int coreBottom = Math.Min(bitmap.Height, core + StripHeight);
            strips.Add((Math.Max(0, core - StripOverlap), Math.Min(bitmap.Height, coreBottom + StripOverlap), core, coreBottom));
        }

        var results = new List<OcrLine>[strips.Count];
        int done = 0;
        var parallel = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
        };
        Parallel.For(0, strips.Count, parallel, i =>
        {
            var (top, bottom, coreTop, coreBottom) = strips[i];
            var lines = ReadStrip(bitmap, top, bottom, scale);
            // Dòng thuộc dải chứa tâm của nó; dải cuối nhận cả dòng sát mép dưới.
            results[i] = lines.Where(l =>
            {
                int mid = (l.Bounds.Top + l.Bounds.Bottom) / 2;
                return mid >= coreTop && (mid < coreBottom || coreBottom == bitmap.Height);
            }).ToList();
            progress?.Report((double)Interlocked.Increment(ref done) / strips.Count);
        });
        ct.ThrowIfCancellationRequested();
        return new OcrResult(results.SelectMany(r => r).ToList(), sw.Elapsed);
    }

    private static readonly int[] BandPaddings = [3, 6]; // px nới trên / dưới dải dòng chữ - mỗi dải đọc cả 2, giữ bản tin cậy hơn
    private const double BandBetter = 1.25; // bản đọc theo dải phải hơn bản đọc thường ≥ 25% mới thay (ảnh đọc tốt giữ nguyên)

    /// <summary>Đọc kỹ (chậm hơn đọc thường ~1,5–3 lần): đọc thường, rồi tách ảnh thành các dải dòng chữ (theo nét chữ từng hàng
    /// pixel), đọc lại riêng từng dải; dải nào bản đọc lại tin cậy hơn hẳn thì thay chữ đọc thường trong dải đó.
    ///
    /// - Tesseract tự tìm dòng chữ (PSM Auto) hay bỏ sót chữ nhỏ trong giao diện rối: dòng đang chọn (nền màu + viền chấm),
    ///   ô lưới, khung đỏ. Cùng dòng đó cắt sát (bỏ viền chấm) thì đọc đúng - đo trên ảnh VS (Error List): đọc thường bỏ
    ///   sót dòng lỗi, đọc kỹ ra đúng "Resource file "My Project\Resources.resx" cannot be found."; ảnh 24 dòng tiếng Việt
    ///   có 5 dòng đang chọn: đọc thường 16/24 dòng đúng, đọc kỹ 23/24 (xem PLAN.md ImageCompare).
    /// - Dải = các hàng pixel liền nhau có nét chữ (nhiều cặp pixel cạnh nhau lệch sáng hơn hẳn hàng trống gần đó); hàng
    ///   "gai" (đường kẻ, viền chấm 1–2 px) không tính là chữ → dải dừng ở viền, không dính viền.
    /// - Đọc dải ở ×2 cả khi ảnh rộng (dải thấp, không tốn bộ nhớ).</summary>
    /// <param name="progress">Phần đã đọc 0–1.</param>
    public static OcrResult RecognizeCarefully(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        var normal = Recognize(bitmap, ct, progress is null ? null : new ScaledProgress(progress, 0, 0.3)).Lines;
        var bands = TextBands(bitmap);
        var bandLines = new List<OcrLine>?[bands.Count]; // null = giữ bản đọc thường
        int done = 0;
        var parallel = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
        };
        Parallel.For(0, bands.Count, parallel, i =>
        {
            var (top, bottom) = bands[i];
            List<OcrLine> best = [];
            double bestScore = 0;
            foreach (int padding in BandPaddings)
            {
                var area = new SKRectI(0, Math.Max(0, top - padding), bitmap.Width, Math.Min(bitmap.Height, bottom + padding));
                var lines = ReadArea(bitmap, area);
                double score = Score(lines.SelectMany(l => l.Words));
                if (score > bestScore)
                {
                    (best, bestScore) = (lines, score);
                }
            }
            double normalScore = Score(normal.SelectMany(l => l.Words).Where(w => InBand(w, bands[i])));
            bandLines[i] = bestScore > normalScore * BandBetter ? best : null;
            progress?.Report(0.3 + 0.7 * Interlocked.Increment(ref done) / bands.Count);
        });
        ct.ThrowIfCancellationRequested();

        // Chữ đọc thường trong dải được thay bị bỏ; dòng của dải chèn trước dòng đầu tiên nằm dưới nó.
        var replaced = Enumerable.Range(0, bands.Count).Where(i => bandLines[i] is not null).Select(i => bands[i]).ToList();
        var result = normal
            .Select(l => l.Words.Where(w => !replaced.Any(b => InBand(w, b))).ToList())
            .Where(words => words.Count > 0)
            .Select(NewLine)
            .ToList();
        foreach (var line in bandLines.OfType<List<OcrLine>>().SelectMany(l => l))
        {
            int at = result.FindIndex(l => l.Bounds.Top > line.Bounds.Top);
            result.Insert(at < 0 ? result.Count : at, line);
        }
        return new OcrResult(result, sw.Elapsed);
    }

    /// <summary>Điểm của 1 bản đọc = Σ độ tin cậy × số chữ cái / chữ số (ký tự rác "|", "—" không cộng điểm).</summary>
    private static double Score(IEnumerable<OcrWord> words) => words.Sum(w => w.Confidence * w.Text.Count(char.IsLetterOrDigit));

    private static bool InBand(OcrWord word, (int Top, int Bottom) band) =>
        word.Bounds.MidY >= band.Top - BandPaddings[0] && word.Bounds.MidY < band.Bottom + BandPaddings[0];

    /// <summary>Đọc riêng 1 vùng (×2), kết quả theo toạ độ ảnh gốc. Bỏ "từ" không có chữ cái / chữ số ở đầu / cuối dòng: viền
    /// chấm, vạch cột hai bên dòng đọc ra "¡", "|", "{".</summary>
    private static List<OcrLine> ReadArea(SKBitmap bitmap, SKRectI area)
    {
        using var crop = new SKBitmap(new SKImageInfo(area.Width, area.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, area, SKRect.Create(area.Width, area.Height));
        }
        var lines = new List<OcrLine>();
        foreach (var line in Recognize(crop, 2, CancellationToken.None, null).Lines)
        {
            var words = line.Words
                .SkipWhile(w => !w.Text.Any(char.IsLetterOrDigit))
                .Reverse().SkipWhile(w => !w.Text.Any(char.IsLetterOrDigit)).Reverse()
                .Select(w => w with { Bounds = SKRectI.Create(area.Left + w.Bounds.Left, area.Top + w.Bounds.Top, w.Bounds.Width, w.Bounds.Height) })
                .ToList();
            if (words.Count > 0)
            {
                lines.Add(NewLine(words));
            }
        }
        return lines;
    }

    private const int EdgeContrast = 40; // 2 pixel cạnh nhau lệch sáng hơn mức này = 1 nét
    private const int BandWindow = 12;   // px trên / dưới để tìm hàng trống (nền) và hàng nhiều nét nhất quanh 1 hàng

    /// <summary>Các dải dòng chữ [Top, Bottom) theo hàng pixel. Hàng có chữ = số nét vượt hàng ít nét nhất quanh đó (nền, kể cả
    /// viền dọc / vạch cột chạy suốt) một khoảng ≥ 1/8 chênh lệch với hàng nhiều nét nhất. Dải ≤ 3 px (gạch chân, viền) bỏ.</summary>
    private static List<(int Top, int Bottom)> TextBands(SKBitmap bitmap)
    {
        var gray = ToGray(bitmap, 0, bitmap.Height, 1);
        int height = gray.Height;
        var count = new int[height];
        for (int y = 0; y < height; y++)
        {
            int row = y * gray.Width;
            for (int x = 1; x < gray.Width; x++)
            {
                if (Math.Abs(gray.Pixels[row + x] - gray.Pixels[row + x - 1]) > EdgeContrast)
                {
                    count[y]++;
                }
            }
        }
        // Hàng "gai" (đường kẻ, viền chấm của dòng đang chọn) nhiều nét hơn hẳn 2 hàng cách nó 2 px → không phải chữ.
        var spike = new bool[height];
        for (int y = 0; y < height; y++)
        {
            int around = Math.Max(y >= 2 ? count[y - 2] : 0, y + 2 < height ? count[y + 2] : 0);
            spike[y] = count[y] > 2 * around + 5;
        }
        var ink = new bool[height];
        for (int y = 0; y < height; y++)
        {
            int floor = int.MaxValue, peak = 0;
            for (int k = Math.Max(0, y - BandWindow); k < Math.Min(height, y + BandWindow + 1); k++)
            {
                floor = Math.Min(floor, count[k]);
                if (!spike[k])
                {
                    peak = Math.Max(peak, count[k]);
                }
            }
            ink[y] = !spike[y] && count[y] > floor + Math.Max(3, (peak - floor) / 8);
        }
        var bands = new List<(int, int)>();
        for (int y = 0; y < height;)
        {
            if (!ink[y])
            {
                y++;
                continue;
            }
            int top = y;
            while (y < height && ink[y])
            {
                y++;
            }
            if (y - top > 3)
            {
                bands.Add((top, y));
            }
        }
        return bands;
    }

    /// <summary>Báo tiến độ của 1 bước con vào khoảng [from, to] của tiến độ chung.</summary>
    private sealed class ScaledProgress(IProgress<double> inner, double from, double to) : IProgress<double>
    {
        public void Report(double value) => inner.Report(from + (to - from) * value);
    }

    /// <summary>Dải ảnh xám đã phóng; <see cref="Inverted"/> = dải nền tối, đọc với màu đảo.</summary>
    private sealed record GrayStrip(byte[] Pixels, int Width, int Height, bool Inverted);

    /// <summary>Đọc 1 dải; kết quả theo toạ độ ảnh gốc.</summary>
    private static List<OcrLine> ReadStrip(SKBitmap bitmap, int top, int bottom, int scale)
    {
        var gray = ToGray(bitmap, top, bottom, scale);
        var engine = Rent();
        try
        {
            List<OcrLine> lines;
            using (var pix = ToPix(gray, SKRectI.Create(gray.Width, gray.Height), gray.Inverted))
            using (var page = engine.Process(pix, PageSegMode.Auto))
            {
                lines = ReadLines(page, 0, 0);
            }
            var inverted = new bool[lines.Count]; // dòng đã đọc lại với màu đảo
            for (int i = 0; i < lines.Count; i++)
            {
                if (Confidence(lines[i]) >= RetryBelow)
                {
                    continue;
                }
                var area = LineArea(lines[i], gray, scale);
                using var pix = ToPix(gray, area, !gray.Inverted);
                using var page = engine.Process(pix, PageSegMode.SingleLine);
                var words = ReadLines(page, area.Left, area.Top).SelectMany(l => l.Words).ToList();
                if (words.Count > 0 && words.Average(w => w.Confidence) > Confidence(lines[i]) + 10)
                {
                    lines[i] = NewLine(words);
                    inverted[i] = true;
                }
            }
            ReadEnglishLines(lines, inverted, gray, scale);
            return lines
                .Where(l => Confidence(l) >= DropBelow)
                .Select(l => NewLine(l.Words.Select(w => w with
                {
                    Bounds = new SKRectI(w.Bounds.Left / scale, top + w.Bounds.Top / scale,
                        (w.Bounds.Right + scale - 1) / scale, top + (w.Bounds.Bottom + scale - 1) / scale),
                }).ToList()))
                .ToList();
        }
        finally
        {
            Pool.Add(engine);
        }
    }

    /// <summary>Đọc lại bằng "eng" các dòng không có chữ tiếng Việt có dấu, thay khi "eng" tin cậy bằng / hơn "vie".
    /// PSM SingleBlock: đã thử trên ảnh thật - SingleLine hay dính 2 từ ("bya") và đọc kém đường dẫn hơn.</summary>
    private static void ReadEnglishLines(List<OcrLine> lines, bool[] inverted, GrayStrip gray, int scale)
    {
        if (!lines.Any(l => !IsVietnamese(l)) || RentEnglish() is not { } engine)
        {
            return;
        }
        try
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsVietnamese(lines[i]))
                {
                    continue;
                }
                var area = LineArea(lines[i], gray, scale);
                using var pix = ToPix(gray, area, gray.Inverted ^ inverted[i]);
                using var page = engine.Process(pix, PageSegMode.SingleBlock);
                var words = ReadLines(page, area.Left, area.Top).SelectMany(l => l.Words).ToList();
                if (words.Count > 0 && words.Average(w => w.Confidence) >= Confidence(lines[i]))
                {
                    lines[i] = NewLine(words);
                }
            }
        }
        finally
        {
            EnglishPool.Add(engine);
        }
    }

    private static bool IsVietnamese(OcrLine line) =>
        line.Words.Any(w => w.Text.Any(c => VietnameseLetters.Contains(char.ToLowerInvariant(c))));

    /// <summary>Khung dòng nới 4 px (ảnh gốc) để đọc lại riêng dòng đó.</summary>
    private static SKRectI LineArea(OcrLine line, GrayStrip gray, int scale) =>
        SKRectI.Intersect(SKRectI.Inflate(line.Bounds, 4 * scale, 4 * scale), SKRectI.Create(gray.Width, gray.Height));

    private static float Confidence(OcrLine line) => line.Words.Average(w => w.Confidence);

    private static OcrLine NewLine(List<OcrWord> words)
    {
        var b = words[0].Bounds;
        foreach (var w in words)
        {
            b.Union(w.Bounds);
        }
        return new OcrLine(words, b);
    }

    private static TesseractEngine Rent()
    {
        if (Pool.TryTake(out var engine))
        {
            return engine;
        }
        if (!File.Exists(Path.Combine(DataPath, "vie.traineddata")))
        {
            throw new FileNotFoundException($"Thiếu dữ liệu nhận dạng chữ trong {DataPath}");
        }
        return new TesseractEngine(DataPath, Languages, EngineMode.LstmOnly);
    }

    /// <summary>Engine "eng"; null khi thiếu eng.traineddata (bản cũ chỉ có "vie") → giữ nguyên bản đọc "vie".</summary>
    private static TesseractEngine? RentEnglish()
    {
        if (EnglishPool.TryTake(out var engine))
        {
            return engine;
        }
        return File.Exists(Path.Combine(DataPath, EnglishLanguages + ".traineddata"))
            ? new TesseractEngine(DataPath, EnglishLanguages, EngineMode.LstmOnly)
            : null;
    }

    /// <summary>Cắt dải [top, bottom) của ảnh, phóng ×<paramref name="scale"/>, đổi sang xám 8 bit.</summary>
    private static unsafe GrayStrip ToGray(SKBitmap bitmap, int top, int bottom, int scale)
    {
        int width = bitmap.Width, height = bottom - top;
        using var strip = new SKBitmap(new SKImageInfo(width * scale, height * scale, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(strip))
        using (var paint = new SKPaint { FilterQuality = scale > 1 ? SKFilterQuality.High : SKFilterQuality.None })
        {
            canvas.Clear(SKColors.White); // pixel trong suốt → nền trắng
            canvas.DrawBitmap(bitmap, SKRect.Create(0, top, width, height), SKRect.Create(strip.Width, strip.Height), paint);
        }

        var gray = new byte[strip.Width * strip.Height];
        byte* src = (byte*)strip.GetPixels();
        long sum = 0;
        for (int y = 0; y < strip.Height; y++)
        {
            byte* s = src + (long)y * strip.RowBytes;
            int row = y * strip.Width;
            for (int x = 0; x < strip.Width; x++, s += 4)
            {
                int luma = (s[2] * 77 + s[1] * 150 + s[0] * 29) >> 8;
                gray[row + x] = (byte)luma;
                sum += luma;
            }
        }
        // Giao diện tối (chữ sáng trên nền tối): Tesseract đọc được nhưng hay mất dấu tiếng Việt → đảo màu khi
        // phần lớn dải là nền tối.
        bool inverted = gray.Length > 0 && sum / gray.Length < 110;
        return new GrayStrip(gray, strip.Width, strip.Height, inverted);
    }

    private static unsafe Pix ToPix(GrayStrip gray, SKRectI area, bool invert)
    {
        var pix = Pix.Create(area.Width, area.Height, 8);
        var data = pix.GetData();
        uint mask = invert ? 255u : 0u;
        for (int y = 0; y < area.Height; y++)
        {
            int row = (area.Top + y) * gray.Width + area.Left;
            uint* line = (uint*)data.Data + (long)y * data.WordsPerLine;
            for (int x = 0; x < area.Width; x++)
            {
                PixData.SetDataByte(line, x, gray.Pixels[row + x] ^ mask);
            }
        }
        return pix;
    }

    /// <summary>Các dòng Tesseract đọc được; toạ độ = toạ độ trong Pix + (<paramref name="left"/>, <paramref name="top"/>).</summary>
    private static List<OcrLine> ReadLines(Page page, int left, int top)
    {
        var lines = new List<OcrLine>();
        var words = new List<OcrWord>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            string? text = iter.GetText(PageIteratorLevel.Word)?.Trim();
            if (!string.IsNullOrEmpty(text) && iter.TryGetBoundingBox(PageIteratorLevel.Word, out var r))
            {
                words.Add(new OcrWord(text, new SKRectI(left + r.X1, top + r.Y1, left + r.X2, top + r.Y2), iter.GetConfidence(PageIteratorLevel.Word)));
            }
            if (iter.IsAtFinalOf(PageIteratorLevel.TextLine, PageIteratorLevel.Word) && words.Count > 0)
            {
                lines.Add(NewLine(words.ToList()));
                words.Clear();
            }
        }
        while (iter.Next(PageIteratorLevel.Word));
        if (words.Count > 0)
        {
            lines.Add(NewLine(words));
        }
        return lines;
    }
}
