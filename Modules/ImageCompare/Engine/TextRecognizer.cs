using System.Collections.Concurrent;
using System.Diagnostics;
using SkiaSharp;
using Tesseract;

namespace ImageCompare.Engine;

/// <summary>1 từ đọc được. <see cref="Bounds"/> theo toạ độ ảnh gốc.</summary>
public sealed record OcrWord(string Text, SKRectI Bounds, float Confidence);

/// <summary>1 dòng chữ = các từ liền nhau trên cùng dòng (theo Tesseract).</summary>
public sealed record OcrLine(IReadOnlyList<OcrWord> Words, SKRectI Bounds)
{
    public string Text => string.Join(" ", Words.Select(w => w.Text));
}

public sealed record OcrResult(IReadOnlyList<OcrLine> Lines, TimeSpan Elapsed)
{
    /// <summary>Toàn bộ chữ, mỗi dòng 1 dòng (thứ tự trên → dưới).</summary>
    public string FullText => string.Join(Environment.NewLine, Lines.Select(l => l.Text));
}

/// <summary>Đọc chữ trong ảnh bằng Tesseract (dữ liệu tiếng Việt tessdata_fast, thư mục
/// <c>tessdata</c> cạnh exe - chỉ "vie": thử nghiệm đọc chữ tiếng Anh / code / đường dẫn đúng ngang "vie+eng",
/// còn tiếng Việt thì đúng dấu hơn, nhanh hơn ~1,5 lần và bớt 4 MB dữ liệu).
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
///   khi trung bình quá thấp (rác đọc từ icon / hình vẽ).</summary>
public static class TextRecognizer
{
    private const string Languages = "vie";
    private const int StripHeight = 600; // px ảnh gốc - mỗi dải ~0,3–0,5 s → huỷ nhanh, báo tiến độ mịn
    private const int StripOverlap = 80;  // > chiều cao 1 dòng chữ lớn
    private const float RetryBelow = 50;  // độ tin cậy trung bình của dòng dưới mức này → đọc lại với màu đảo
    private const float DropBelow = 15;   // sau khi đọc lại vẫn dưới mức này → bỏ dòng

    private static readonly ConcurrentBag<TesseractEngine> Pool = [];

    /// <summary>Thư mục chứa *.traineddata.</summary>
    public static string DataPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "tessdata");

    /// <param name="progress">Phần đã đọc 0–1 (sau mỗi dải).</param>
    public static OcrResult Recognize(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        int scale = bitmap.Width <= 2400 ? 2 : 1;
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
            for (int i = 0; i < lines.Count; i++)
            {
                if (Confidence(lines[i]) >= RetryBelow)
                {
                    continue;
                }
                var area = SKRectI.Intersect(SKRectI.Inflate(lines[i].Bounds, 4 * scale, 4 * scale), SKRectI.Create(gray.Width, gray.Height));
                using var pix = ToPix(gray, area, !gray.Inverted);
                using var page = engine.Process(pix, PageSegMode.SingleLine);
                var words = ReadLines(page, area.Left, area.Top).SelectMany(l => l.Words).ToList();
                if (words.Count > 0 && words.Average(w => w.Confidence) > Confidence(lines[i]) + 10)
                {
                    lines[i] = NewLine(words);
                }
            }
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
