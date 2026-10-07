using ScreenCapture.Services.Interop;
using SkiaSharp;

namespace ScreenCapture.Services;

/// <summary>
/// The single capture primitive every capture mode reduces to: grab a rect from the desktop-composited
/// screen buffer. See PLAN.md "Nguyen ly capture duy nhat" for why this replaces per-mode capture logic.
/// </summary>
public interface ICaptureService
{
    /// <summary>Captures a rect (virtual-screen-relative device pixels) from the composited desktop.
    /// <paramref name="includeCursor"/>: vẽ thêm con trỏ chuột (đúng hình + vị trí lúc chụp) nếu nó nằm trong vùng.</summary>
    SKBitmap CaptureRect(RECT rectPx, bool includeCursor = false);

    /// <summary>Bounds of the entire virtual screen (all monitors), device pixels.</summary>
    RECT GetVirtualScreenRect();

    /// <summary>
    /// Restores-if-minimized and brings the foreground window to front, then returns its true visible
    /// bounds (DWM extended frame bounds, falling back to GetWindowRect). Null if there is no
    /// foreground window.
    /// </summary>
    Task<RECT?> GetForegroundWindowRectAsync();
}
