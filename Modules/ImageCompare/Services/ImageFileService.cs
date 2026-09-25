using ImageCompare.Engine;
using SkiaSharp;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ImageCompare.Services;

/// <summary>Hộp thoại mở / lưu file (app unpackaged → picker phải gắn hwnd qua InitializeWithWindow).
/// Phần lưu PNG chép từ ScreenCapture.Services.ImageFileService.</summary>
public sealed class ImageFileService
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    /// <summary>Chọn 1 file ảnh; null nếu huỷ.</summary>
    public async Task<string?> PickImageAsync(IntPtr ownerHwnd)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in ImageExtensions)
        {
            picker.FileTypeFilter.Add(ext);
        }
        InitializeWithWindow.Initialize(picker, ownerHwnd);
        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public Task<string?> SavePngAsync(SKBitmap bitmap, IntPtr ownerHwnd, string suggestedName) =>
        SaveAsync(ownerHwnd, suggestedName, "Ảnh PNG", ".png", stream =>
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            data.SaveTo(stream);
        });

    public Task<string?> SaveHtmlAsync(string html, IntPtr ownerHwnd, string suggestedName) =>
        SaveAsync(ownerHwnd, suggestedName, "Báo cáo HTML", ".html", stream =>
        {
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            writer.Write(html);
        });

    private static async Task<string?> SaveAsync(IntPtr ownerHwnd, string suggestedName, string typeName, string extension,
        Action<Stream> write)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add(typeName, new List<string> { extension });
        picker.SuggestedFileName = SanitizeFileName(suggestedName);
        InitializeWithWindow.Initialize(picker, ownerHwnd);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }
        using var stream = await file.OpenStreamForWriteAsync();
        // OpenStreamForWriteAsync không xoá nội dung cũ: ghi đè lên file lớn hơn sẽ còn sót đuôi dữ liệu cũ.
        stream.SetLength(0);
        write(stream);
        return file.Path;
    }

    /// <summary>Đọc file ảnh về định dạng pixel thống nhất của Engine (xem <see cref="ImageUtil.Normalize"/>).
    /// Ném <see cref="InvalidDataException"/> nếu không phải ảnh đọc được.</summary>
    public static SKBitmap LoadImage(string path)
    {
        using var decoded = SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"Không đọc được ảnh: {Path.GetFileName(path)}");
        return ImageUtil.Normalize(decoded);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
