using SkiaSharp;

namespace Common.Ocr;

/// <summary><see cref="IFormTextReader"/> bọc <see cref="TextRecognizer"/> (Tesseract "vie", đọc cả trang: phóng ×2, đảo màu dải
/// nền tối, ảnh dài cắt dải) - bộ đọc tiếng Việt / Anh của Tìm chữ (<see cref="FormReaders.CreateForSearch"/>).</summary>
/// <param name="careful">Đọc kỹ (<see cref="TextRecognizer.RecognizeCarefully"/>): chậm hơn, bắt được chữ đọc thường bỏ sót.</param>
public sealed class PageTextReader(bool careful = false) : IFormTextReader
{
    public string Name => careful ? "Tesseract (tiếng Việt / Anh, đọc kỹ)" : "Tesseract (tiếng Việt / Anh)";

    public OcrResult Read(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null) =>
        careful ? TextRecognizer.RecognizeCarefully(bitmap, ct, progress) : TextRecognizer.Recognize(bitmap, ct, progress);

    public string ReadText(SKBitmap crop, int scale, int? threshold = null) => TextRecognizer.Recognize(crop).FullText;
}
