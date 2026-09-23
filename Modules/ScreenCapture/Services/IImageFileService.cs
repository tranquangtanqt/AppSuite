using SkiaSharp;

namespace ScreenCapture.Services;

public interface IImageFileService
{
    /// <summary>Shows a Save As dialog and encodes the bitmap as PNG. Returns the saved path, or null
    /// if the user cancelled. <paramref name="ownerHwnd"/> is required because this is an unpackaged
    /// app - FileSavePicker needs IInitializeWithWindow to know which window owns the dialog.</summary>
    Task<string?> SaveAsPngAsync(SKBitmap bitmap, IntPtr ownerHwnd);
}
