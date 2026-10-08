using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using OcrLine = Common.Ocr.OcrLine;
using OcrResult = Common.Ocr.OcrResult;
using OcrWord = Common.Ocr.OcrWord;

namespace Common.Ocr;

/// <summary>Ngôn ngữ chữ trên màn hình cần so (chế độ So chữ).</summary>
public enum FormLanguage
{
    Japanese,
    /// <summary>Tiếng Việt và tiếng Anh - dữ liệu "vie" của Tesseract đọc cả 2 (xem PLAN.md, phần Tìm chữ).</summary>
    VietnameseEnglish,
}

/// <summary>Chọn bộ đọc chữ cho chế độ So chữ (đã đo, xem PLAN.md):
/// - Tiếng Nhật: Windows OCR "ja" (đúng 75–85%, nhanh nhất); máy không có gói OCR tiếng Nhật của Windows → Tesseract
///   "jpn" (62–74%) làm dự phòng.
/// - Tiếng Việt / tiếng Anh: Tesseract "vie" (Windows OCR không có tiếng Việt).</summary>
public static class FormReaders
{
    /// <param name="note">Ghi chú cho người dùng khi phải dùng dự phòng, null nếu không.</param>
    public static IFormTextReader Create(FormLanguage language, out string? note)
    {
        note = null;
        if (language == FormLanguage.VietnameseEnglish)
        {
            return new TesseractFormReader("vie", "Tesseract (tiếng Việt / Anh)");
        }
        if (WindowsFormReader.TryCreate("ja", "Windows OCR (tiếng Nhật)") is { } windows)
        {
            return windows;
        }
        // Ngắn: bảng kết quả hẹp - chữ dài đẩy danh sách xuống (đã gặp). Cách cài gói OCR ghi ở README.
        note = "Máy chưa có OCR tiếng Nhật của Windows → dùng Tesseract: kém chính xác, nhiều mục báo nhầm hơn (cách cài: README).";
        return new TesseractFormReader("jpn", "Tesseract (tiếng Nhật, dự phòng)");
    }

    /// <summary>Bộ đọc cho Tìm chữ (đọc cả ảnh bất kỳ, không riêng màn hình form) - cùng lựa chọn với Tìm chữ của ImageCompare:
    /// tiếng Nhật dùng bộ đọc form như <see cref="Create"/> (Windows OCR "ja", dự phòng Tesseract "jpn"); tiếng Việt / Anh dùng
    /// <see cref="TextRecognizer"/> (Tesseract "vie" đọc cả trang) - bộ đọc form "vie" (nhị phân hoá + cắt cụm chữ cho form) đọc
    /// chữ thường trên ảnh ra rác (đã gặp: 6 dòng Segoe UI 20pt → 17 dòng ký tự lạ).</summary>
    public static IFormTextReader CreateForSearch(FormLanguage language, out string? note)
    {
        if (language == FormLanguage.Japanese)
        {
            return Create(language, out note);
        }
        note = null;
        return new PageTextReader();
    }

    /// <summary>Bộ đọc dùng để đọc lại từng chỗ nghi khác (TextDiffVerifier của ImageCompare): bộ đọc chính, cộng
    /// Tesseract jpn khi bộ chính là Windows OCR - font bitmap mà engine này đọc sai thì engine kia có khi đúng. Đo trên
    /// ảnh thật: chỉ Windows OCR còn 31 chỗ khác (+1,2 s); thêm Tesseract còn 28 (+3,5 s).</summary>
    public static IReadOnlyList<IFormTextReader> VerifyReaders(IFormTextReader primary, FormLanguage language) =>
        primary is WindowsFormReader && language == FormLanguage.Japanese && TesseractFormReader.HasData("jpn")
            ? [primary, new TesseractFormReader("jpn", "Tesseract (tiếng Nhật)")]
            : [primary];
}

/// <summary>Đọc form bằng Windows OCR (Windows.Media.Ocr) trên ảnh đã <see cref="FormPreprocess"/>: đọc cả trang
/// (đo: tốt hơn đọc từng cụm - 75–85% so với 68–77%). Ảnh dài cắt thành dải vì Windows OCR giới hạn cạnh ảnh
/// (<see cref="OcrEngine.MaxImageDimension"/>, 10.000 px).</summary>
public sealed class WindowsFormReader : IFormTextReader
{
    private const int StripOverlap = 80; // px ảnh gốc, > 1 dòng chữ

    private readonly OcrEngine _engine;

    private WindowsFormReader(OcrEngine engine, string name)
    {
        _engine = engine;
        Name = name;
    }

    public string Name { get; }

    public static WindowsFormReader? TryCreate(string languageTag, string name)
    {
        try
        {
            var language = new Windows.Globalization.Language(languageTag);
            return OcrEngine.IsLanguageSupported(language) && OcrEngine.TryCreateFromLanguage(language) is { } engine
                ? new WindowsFormReader(engine, name)
                : null;
        }
        catch (Exception)
        {
            return null; // API OCR không có (Windows rút gọn) → dùng dự phòng
        }
    }

    public OcrResult Read(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        int max = (int)OcrEngine.MaxImageDimension;
        int scale = Math.Clamp(max / Math.Max(1, bitmap.Width), 1, FormPreprocess.DefaultScale);
        int stripHeight = Math.Max(200, max / scale - 2 * StripOverlap);
        var lines = new List<OcrLine>();
        for (int core = 0; core < bitmap.Height; core += stripHeight)
        {
            ct.ThrowIfCancellationRequested();
            int coreBottom = Math.Min(bitmap.Height, core + stripHeight);
            int top = Math.Max(0, core - StripOverlap), bottom = Math.Min(bitmap.Height, coreBottom + StripOverlap);
            using var strip = Crop(bitmap, top, bottom);
            foreach (var line in ReadStrip(strip, scale, top, ct))
            {
                int mid = (line.Bounds.Top + line.Bounds.Bottom) / 2;
                if (mid >= core && (mid < coreBottom || coreBottom == bitmap.Height))
                {
                    lines.Add(line);
                }
            }
            progress?.Report((double)coreBottom / bitmap.Height);
        }
        return new OcrResult(lines, sw.Elapsed);
    }

    public string ReadText(SKBitmap crop, int scale, int? threshold = null)
    {
        var image = FormPreprocess.Prepare(crop, scale, threshold);
        int pad = 8 * scale; // Windows OCR bỏ sót chữ sát mép ảnh
        var gray = FormPreprocess.Crop(image, SKRectI.Create(image.Width, image.Height), pad, out int w, out int h);
        using var softwareBitmap = ToSoftwareBitmap(gray, w, h);
        // Đọc lại chạy song song (TextDiffVerifier của ImageCompare) - tài liệu không nói OcrEngine an toàn đa luồng → khoá.
        Windows.Media.Ocr.OcrResult result;
        lock (_engine)
        {
            result = _engine.RecognizeAsync(softwareBitmap).AsTask().GetAwaiter().GetResult();
        }
        return OcrText.JoinWords(result.Lines.SelectMany(l => l.Words).Select(w => w.Text));
    }

    private static SoftwareBitmap ToSoftwareBitmap(byte[] gray, int width, int height)
    {
        var bgra = new byte[gray.Length * 4];
        for (int i = 0; i < gray.Length; i++)
        {
            byte v = gray[i];
            bgra[i * 4] = bgra[i * 4 + 1] = bgra[i * 4 + 2] = v;
            bgra[i * 4 + 3] = 255;
        }
        return SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
    }

    private List<OcrLine> ReadStrip(SKBitmap strip, int scale, int top, CancellationToken ct)
    {
        var image = FormPreprocess.Prepare(strip, scale);
        using var softwareBitmap = ToSoftwareBitmap(image.Pixels, image.Width, image.Height);
        var result = _engine.RecognizeAsync(softwareBitmap).AsTask(ct).GetAwaiter().GetResult();
        var lines = new List<OcrLine>();
        foreach (var line in result.Lines)
        {
            var words = line.Words.Select(w => new OcrWord(w.Text, new SKRectI(
                (int)Math.Floor(w.BoundingRect.X / scale),
                top + (int)Math.Floor(w.BoundingRect.Y / scale),
                (int)Math.Ceiling((w.BoundingRect.X + w.BoundingRect.Width) / scale),
                top + (int)Math.Ceiling((w.BoundingRect.Y + w.BoundingRect.Height) / scale)), 100)).ToList();
            if (words.Count == 0)
            {
                continue;
            }
            var bounds = words[0].Bounds;
            foreach (var w in words)
            {
                bounds.Union(w.Bounds);
            }
            lines.Add(new OcrLine(words, bounds));
        }
        return lines;
    }

    private static SKBitmap Crop(SKBitmap source, int top, int bottom)
    {
        var strip = new SKBitmap(new SKImageInfo(source.Width, bottom - top, OcrText.ColorType, OcrText.AlphaType));
        using var canvas = new SKCanvas(strip);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, SKRect.Create(0, top, source.Width, bottom - top), SKRect.Create(source.Width, bottom - top));
        return strip;
    }
}
