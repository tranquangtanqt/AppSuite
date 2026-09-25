using SkiaSharp;

namespace ImageCompare.Models;

/// <summary>1 ảnh đưa vào so sánh (ảnh A hoặc B). <see cref="Bitmap"/> luôn ở định dạng chuẩn của Engine
/// (xem Engine/ImageUtil.Normalize).</summary>
/// <param name="Name">Tên hiển thị: tên file, hoặc "Clipboard".</param>
/// <param name="Path">Đường dẫn file gốc, null nếu dán từ clipboard.</param>
public sealed record LoadedImage(SKBitmap Bitmap, string Name, string? Path)
{
    public string Describe() => $"{Name}  ({Bitmap.Width} × {Bitmap.Height})";
}

/// <summary>Cách hiển thị 2 ảnh trên canvas.</summary>
public enum ViewMode
{
    /// <summary>Ảnh B nhạt màu + vùng khác tô đỏ, đánh số từng vùng.</summary>
    Diff,
    /// <summary>A bên trái, B bên phải, zoom / cuộn đồng bộ.</summary>
    SideBySide,
    /// <summary>B đặt chồng lên A với độ trong suốt chỉnh được.</summary>
    Overlay,
    /// <summary>Vạch chia kéo được: trái vạch là A, phải vạch là B.</summary>
    Swipe,
    /// <summary>Tìm ảnh nhỏ hơn trong ảnh lớn hơn (Engine/TemplateMatcher).</summary>
    Find,
    /// <summary>Đọc chữ trong 1 ảnh (OCR, Engine/TextRecognizer) và tìm chữ trong đó - chỉ cần 1 ảnh.</summary>
    Text,
}
