using SkiaSharp;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ScreenCapture.Services;

public sealed class ImageFileService : IImageFileService
{
    public async Task<string?> SaveAsPngAsync(SKBitmap bitmap, IntPtr ownerHwnd, string? suggestedName = null)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(suggestedName)
            ? $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}"
            : SanitizeFileName(suggestedName);
        InitializeWithWindow.Initialize(picker, ownerHwnd);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = await file.OpenStreamForWriteAsync();
        // OpenStreamForWriteAsync không xoá nội dung cũ: ghi đè lên file PNG lớn hơn sẽ còn sót đuôi dữ
        // liệu cũ phía sau → cắt về 0 trước khi ghi.
        stream.SetLength(0);
        data.SaveTo(stream);
        return file.Path;
    }

    public async Task<string?> PickFolderAsync(IntPtr ownerHwnd)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, ownerHwnd);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    public string SavePngToFolder(SKBitmap bitmap, string folder, string baseName)
    {
        baseName = SanitizeFileName(baseName);
        var path = Path.Combine(folder, baseName + ".png");
        for (int i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(folder, $"{baseName} ({i}).png");
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }
}
