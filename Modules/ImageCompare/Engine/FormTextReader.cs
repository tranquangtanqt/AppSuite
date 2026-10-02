using System.Collections.Concurrent;
using System.Diagnostics;
using SkiaSharp;
using Tesseract;

namespace ImageCompare.Engine;

/// <summary>Bộ đọc chữ cho chế độ So chữ: ảnh chụp màn hình form → các cụm chữ kèm khung (toạ độ ảnh gốc).
/// Có 2 cài đặt: <see cref="TesseractFormReader"/> (Engine, chạy mọi máy) và Windows OCR (lớp
/// <c>Services/WindowsFormReader</c> của app - cần API WinRT nên không nằm trong Engine).</summary>
public interface IFormTextReader
{
    /// <summary>Tên hiển thị cho người dùng, vd "Windows OCR (tiếng Nhật)".</summary>
    string Name { get; }

    /// <param name="progress">Phần đã đọc 0–1.</param>
    OcrResult Read(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null);

    /// <summary>Đọc 1 vùng nhỏ cắt ra (1 nhãn / 1 ô) như 1 dòng, phóng <paramref name="scale"/> lần - dùng khi kiểm tra
    /// lại 1 chỗ nghi khác (<see cref="TextDiffVerifier"/>). Chuỗi rỗng nếu không đọc ra gì.</summary>
    string ReadText(SKBitmap crop, int scale, int? threshold = null);
}

/// <summary>Đọc form bằng Tesseract: <see cref="FormPreprocess"/> → tách cụm chữ → đọc từng cụm như 1 dòng
/// (PSM SingleLine). Đo trên màn hình tiếng Nhật: đọc cả trang (kể cả đã xoá viền) chỉ 9% đoạn chữ ra giống nhau
/// giữa 2 ảnh cùng nội dung; đọc từng cụm: 42% và đúng nhiều hơn (62–74% so với 61–68%).</summary>
public sealed class TesseractFormReader : IFormTextReader
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<TesseractEngine>> Pools = new();

    private readonly string _language;

    /// <param name="language">Mã dữ liệu tessdata: "jpn" (tiếng Nhật) hoặc "vie" (tiếng Việt + tiếng Anh).</param>
    public TesseractFormReader(string language, string displayName)
    {
        _language = language;
        Name = displayName;
    }

    public string Name { get; }

    public static bool HasData(string language) =>
        File.Exists(Path.Combine(TextRecognizer.DataPath, language + ".traineddata"));

    public OcrResult Read(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        if (!HasData(_language))
        {
            throw new FileNotFoundException($"Thiếu dữ liệu nhận dạng chữ {_language}.traineddata trong {TextRecognizer.DataPath}");
        }
        var image = FormPreprocess.Prepare(bitmap);
        ct.ThrowIfCancellationRequested();
        var blobs = FormPreprocess.TextBlobs(image);
        var lines = new OcrLine?[blobs.Count];
        int done = 0;
        var parallel = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
        };
        Parallel.For(0, blobs.Count, parallel, i =>
        {
            lines[i] = ReadBlob(image, blobs[i]);
            int n = Interlocked.Increment(ref done);
            if (n % 16 == 0 || n == blobs.Count)
            {
                progress?.Report((double)n / blobs.Count);
            }
        });
        ct.ThrowIfCancellationRequested();
        return new OcrResult(lines.Where(l => l is not null).Select(l => l!).ToList(), sw.Elapsed);
    }

    public string ReadText(SKBitmap crop, int scale, int? threshold = null)
    {
        var image = FormPreprocess.Prepare(crop, scale, threshold);
        int pad = 6 * scale;
        var gray = FormPreprocess.Crop(image, SKRectI.Create(image.Width, image.Height), pad, out int w, out int h);
        var engine = Rent();
        try
        {
            using var pix = ToPix(gray, w, h);
            using var page = engine.Process(pix, PageSegMode.SingleLine);
            return page.GetText().Trim();
        }
        finally
        {
            Pools.GetOrAdd(_language, _ => []).Add(engine);
        }
    }

    /// <summary>Đọc 1 cụm; null nếu không ra chữ. Toạ độ từ trả về theo ảnh gốc.</summary>
    private OcrLine? ReadBlob(BinaryImage image, SKRectI blob)
    {
        int s = image.Scale, pad = 6 * s;
        var crop = FormPreprocess.Crop(image, blob, pad, out int w, out int h);
        var engine = Rent();
        try
        {
            using var pix = ToPix(crop, w, h);
            using var page = engine.Process(pix, PageSegMode.SingleLine);
            var words = new List<OcrWord>();
            using var iter = page.GetIterator();
            iter.Begin();
            do
            {
                string? text = iter.GetText(PageIteratorLevel.Word)?.Trim();
                if (string.IsNullOrEmpty(text) || !iter.TryGetBoundingBox(PageIteratorLevel.Word, out var r))
                {
                    continue;
                }
                // Toạ độ trong ô cắt → ảnh đã phóng → ảnh gốc.
                int left = blob.Left - pad + r.X1, top = blob.Top - pad + r.Y1, right = blob.Left - pad + r.X2, bottom = blob.Top - pad + r.Y2;
                words.Add(new OcrWord(text, new SKRectI(left / s, top / s, (right + s - 1) / s, (bottom + s - 1) / s),
                    iter.GetConfidence(PageIteratorLevel.Word)));
            }
            while (iter.Next(PageIteratorLevel.Word));
            // Rác từ icon / hình (độ tin cậy rất thấp, xem TextRecognizer.DropBelow).
            if (words.Count == 0 || words.Average(w => w.Confidence) < 15)
            {
                return null;
            }
            var bounds = words[0].Bounds;
            foreach (var word in words)
            {
                bounds.Union(word.Bounds);
            }
            return new OcrLine(words, bounds);
        }
        finally
        {
            Pools.GetOrAdd(_language, _ => []).Add(engine);
        }
    }

    private TesseractEngine Rent() =>
        Pools.GetOrAdd(_language, _ => []).TryTake(out var engine)
            ? engine
            : new TesseractEngine(TextRecognizer.DataPath, _language, EngineMode.LstmOnly);

    private static unsafe Pix ToPix(byte[] gray, int width, int height)
    {
        var pix = Pix.Create(width, height, 8);
        var data = pix.GetData();
        for (int y = 0; y < height; y++)
        {
            uint* line = (uint*)data.Data + (long)y * data.WordsPerLine;
            for (int x = 0; x < width; x++)
            {
                PixData.SetDataByte(line, x, gray[y * width + x]);
            }
        }
        return pix;
    }
}
