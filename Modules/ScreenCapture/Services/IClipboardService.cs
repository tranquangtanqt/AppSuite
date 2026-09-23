using SkiaSharp;

namespace ScreenCapture.Services;

public interface IClipboardService
{
    Task CopyBitmapAsync(SKBitmap bitmap);
}
