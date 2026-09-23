using SkiaSharp;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ScreenCapture.Services;

public sealed class ImageFileService : IImageFileService
{
    public async Task<string?> SaveAsPngAsync(SKBitmap bitmap, IntPtr ownerHwnd)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });
        picker.SuggestedFileName = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}";
        InitializeWithWindow.Initialize(picker, ownerHwnd);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = await file.OpenStreamForWriteAsync();
        data.SaveTo(stream);
        return file.Path;
    }
}
