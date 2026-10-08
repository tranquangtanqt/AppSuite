using SkiaSharp;

namespace Common.Ocr;

/// <summary><see cref="IFormTextReader"/> bọc <see cref="TextRecognizer"/> (Tesseract "vie", đọc cả trang: phóng ×2, đảo màu dải
/// nền tối, ảnh dài cắt dải) - bộ đọc tiếng Việt / Anh của Tìm chữ (<see cref="FormReaders.CreateForSearch"/>).</summary>
public sealed class PageTextReader : IFormTextReader
{
    public string Name => "Tesseract (tiếng Việt / Anh)";

    public OcrResult Read(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null) =>
        TextRecognizer.Recognize(bitmap, ct, progress);

    public string ReadText(SKBitmap crop, int scale, int? threshold = null) => TextRecognizer.Recognize(crop).FullText;
}
