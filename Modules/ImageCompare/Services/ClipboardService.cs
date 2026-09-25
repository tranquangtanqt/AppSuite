using ImageCompare.Engine;
using SkiaSharp;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace ImageCompare.Services;

/// <summary>Đọc / ghi ảnh qua clipboard WinRT - chép từ ScreenCapture.Services.ClipboardService (đã chạy
/// được ở app unpackaged, xem PLAN.md của ScreenCapture).</summary>
public sealed class ClipboardService
{
    public async Task CopyBitmapAsync(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var stream = new InMemoryRandomAccessStream();
        using (var outputStream = stream.GetOutputStreamAt(0))
        {
            using var writer = new DataWriter(outputStream);
            writer.WriteBytes(data.ToArray());
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        var package = new DataPackage();
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
        Clipboard.SetContent(package);
    }

    /// <summary>Ảnh trong clipboard (ảnh copy từ app khác, hoặc file ảnh copy trong Explorer) kèm tên hiển
    /// thị; null nếu clipboard không chứa ảnh.</summary>
    public async Task<(SKBitmap Bitmap, string Name)?> GetBitmapAsync()
    {
        var content = Clipboard.GetContent();

        if (content.Contains(StandardDataFormats.Bitmap))
        {
            var reference = await content.GetBitmapAsync();
            using var stream = await reference.OpenReadAsync();
            using var netStream = stream.AsStreamForRead();
            using var decoded = SKBitmap.Decode(netStream);
            return decoded is null ? null : (ImageUtil.Normalize(decoded), "Clipboard");
        }

        // Copy file ảnh trong Explorer → clipboard chứa StorageItems, không chứa Bitmap.
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var items = await content.GetStorageItemsAsync();
            var file = items.OfType<Windows.Storage.StorageFile>()
                .FirstOrDefault(f => ImageFileService.ImageExtensions.Contains(f.FileType.ToLowerInvariant()));
            if (file is not null)
            {
                return (ImageFileService.LoadImage(file.Path), file.Name);
            }
        }

        return null;
    }
}
