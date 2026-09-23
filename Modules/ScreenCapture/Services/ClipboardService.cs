using SkiaSharp;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace ScreenCapture.Services;

/// <summary>
/// NOTE (PLAN.md rủi ro): Windows.ApplicationModel.DataTransfer.Clipboard is a WinRT API that can be
/// finicky in unpackaged (WindowsPackageType=None) apps without a package identity. If SetContent
/// throws/no-ops at runtime, fall back to raw GDI clipboard (OpenClipboard/SetClipboardData(CF_DIB))
/// instead - not implemented here yet, verify the WinRT path first.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    public async Task CopyBitmapAsync(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var stream = new InMemoryRandomAccessStream();
        using (var outputStream = stream.GetOutputStreamAt(0))
        {
            var bytes = data.ToArray();
            using var writer = new DataWriter(outputStream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        var package = new DataPackage();
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
        Clipboard.SetContent(package);
    }
}
