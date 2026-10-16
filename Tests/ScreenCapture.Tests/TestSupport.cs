using System.Runtime.CompilerServices;
using SkiaSharp;

namespace ScreenCapture.Tests;

internal static class TestSupport
{
    /// <summary>SessionService.Folder đọc SCREENCAPTURE_SESSION_DIR 1 lần (static) → đặt trước mọi test, trỏ vào thư mục tạm
    /// riêng: không bao giờ đụng phiên tạm thật của người dùng (%TEMP%\AppSuite\ScreenCapture\Session).</summary>
    public static string SessionFolder { get; } =
        Path.Combine(Path.GetTempPath(), "ScreenCaptureTests_" + Guid.NewGuid().ToString("N"), "Session");

    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable("SCREENCAPTURE_SESSION_DIR", SessionFolder);

    /// <summary>Ảnh nền 1 màu, có thể chấm 1 pixel khác màu để dò biến đổi.</summary>
    public static SKBitmap Solid(int width, int height, SKColor color, (int X, int Y, SKColor Color)? dot = null)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        if (dot is { } d)
        {
            bitmap.SetPixel(d.X, d.Y, d.Color);
        }
        return bitmap;
    }
}
