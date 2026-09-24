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

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    public async Task<SKBitmap?> GetBitmapAsync()
    {
        var content = Clipboard.GetContent();

        if (content.Contains(StandardDataFormats.Bitmap))
        {
            var reference = await content.GetBitmapAsync();
            using var stream = await reference.OpenReadAsync();
            using var netStream = stream.AsStreamForRead();
            return SKBitmap.Decode(netStream);
        }

        // Copy file ảnh trong Explorer → clipboard chứa StorageItems, không chứa Bitmap.
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var items = await content.GetStorageItemsAsync();
            var file = items.OfType<Windows.Storage.StorageFile>()
                .FirstOrDefault(f => ImageExtensions.Contains(f.FileType.ToLowerInvariant()));
            if (file is not null)
            {
                return SKBitmap.Decode(file.Path);
            }
        }

        return null;
    }
}
