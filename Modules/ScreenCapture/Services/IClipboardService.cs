using SkiaSharp;

namespace ScreenCapture.Services;

public interface IClipboardService
{
    Task CopyBitmapAsync(SKBitmap bitmap);

    /// <summary>Ảnh đang có trong clipboard (ảnh copy từ app khác, hoặc file ảnh copy trong Explorer),
    /// null nếu clipboard không chứa ảnh.</summary>
    Task<SKBitmap?> GetBitmapAsync();
}
