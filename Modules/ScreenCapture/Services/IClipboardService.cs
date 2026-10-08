using SkiaSharp;

namespace ScreenCapture.Services;

public interface IClipboardService
{
    Task CopyBitmapAsync(SKBitmap bitmap);

    /// <summary>Đặt chữ vào clipboard (Tìm chữ: Copy toàn bộ chữ / Copy chữ trong vùng chọn).</summary>
    void CopyText(string text);

    /// <summary>Ảnh đang có trong clipboard (ảnh copy từ app khác, hoặc file ảnh copy trong Explorer),
    /// null nếu clipboard không chứa ảnh.</summary>
    Task<SKBitmap?> GetBitmapAsync();
}
