using SkiaSharp;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ScreenCapture.Services;

public sealed class ImageFileService : IImageFileService
{
    /// <summary>Định dạng ghi được (đuôi → tên hiện trong hộp thoại Lưu thành). GIF / WEBP chỉ mở được, không ghi.</summary>
    public static readonly (string Extension, string Name)[] SaveFormats = [(".png", "PNG"), (".jpg", "JPG"), (".bmp", "BMP")];

    public int JpegQuality { get; set; } = 90;

    /// <summary>Ghi được ra định dạng của file này không (Lưu thẳng vào file đang mở thay vì hỏi Lưu thành).</summary>
    public static bool CanSaveAs(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp";

    public async Task<string?> PickSavePathAsync(IntPtr ownerHwnd, string suggestedName, string defaultExtension)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        // Định dạng mặc định đứng đầu danh sách (hộp thoại chọn sẵn loại đầu tiên).
        var ext = defaultExtension.ToLowerInvariant() == ".jpeg" ? ".jpg" : defaultExtension.ToLowerInvariant();
        foreach (var (extension, name) in SaveFormats.OrderBy(f => f.Extension == ext ? 0 : 1))
        {
            picker.FileTypeChoices.Add(name, new List<string> { extension });
        }
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(suggestedName)
            ? $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}"
            : SanitizeFileName(suggestedName);
        InitializeWithWindow.Initialize(picker, ownerHwnd);

        StorageFile? file = await picker.PickSaveFileAsync();
        return file is null ? null : TypedExtensionPath(file.Path);
    }

    /// <summary>Người dùng gõ "x.jpg" khi loại đang chọn là PNG → hộp thoại trả "x.jpg.png" (đã gặp khi kiểm tra). Tên gõ đã
    /// có đuôi ghi được thì theo đuôi đó: "x.jpg"; xoá file rỗng hộp thoại có thể đã tạo sẵn ở tên kia.</summary>
    internal static string TypedExtensionPath(string path)
    {
        var inner = Path.GetFileNameWithoutExtension(path);
        if (!CanSaveAs(inner) || string.Equals(Path.GetExtension(inner), Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length == 0)
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // không xoá được file rỗng thừa - không ảnh hưởng việc lưu
        }
        return Path.Combine(Path.GetDirectoryName(path)!, inner);
    }

    /// <summary>Ghi ảnh theo đuôi file (PNG / JPG theo <see cref="JpegQuality"/> / BMP 24-bit). Ghi ra file tạm cạnh file
    /// đích rồi mới thay thế - lưu lỗi giữa chừng (đầy ổ, mất quyền) không làm hỏng file cũ. JPG / BMP không có kênh trong
    /// suốt → ghép lên nền trắng (không thì phần trong suốt thành đen).</summary>
    public void WriteImage(SKBitmap bitmap, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (var stream = File.Create(temp))
            {
                if (ext is ".jpg" or ".jpeg")
                {
                    using var opaque = OnWhite(bitmap);
                    using var image = SKImage.FromBitmap(opaque);
                    using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(JpegQuality, 1, 100));
                    data.SaveTo(stream);
                }
                else if (ext == ".bmp")
                {
                    using var opaque = OnWhite(bitmap);
                    WriteBmp24(opaque, stream);
                }
                else
                {
                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    data.SaveTo(stream);
                }
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static SKBitmap OnWhite(SKBitmap bitmap)
    {
        var result = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(bitmap, 0, 0);
        return result;
    }

    /// <summary>BMP 24-bit không nén (SkiaSharp không mã hoá được BMP): dòng dưới cùng trước, mỗi dòng đệm đủ bội 4 byte.</summary>
    private static unsafe void WriteBmp24(SKBitmap bitmap, Stream stream)
    {
        int w = bitmap.Width, h = bitmap.Height, rowSize = (w * 3 + 3) & ~3, imageSize = rowSize * h;
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write((byte)'B'); writer.Write((byte)'M');
        writer.Write(14 + 40 + imageSize); writer.Write(0); writer.Write(14 + 40);
        writer.Write(40); writer.Write(w); writer.Write(h); writer.Write((short)1); writer.Write((short)24);
        writer.Write(0); writer.Write(imageSize); writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0); // 96 dpi
        var row = new byte[rowSize];
        byte* basePtr = (byte*)bitmap.GetPixels();
        for (int y = h - 1; y >= 0; y--)
        {
            uint* src = (uint*)(basePtr + (long)y * bitmap.RowBytes);
            for (int x = 0; x < w; x++)
            {
                uint p = src[x]; // BGRA (đã ghép nền trắng nên A = 255, không cần bỏ premultiply)
                row[x * 3] = (byte)p;
                row[x * 3 + 1] = (byte)(p >> 8);
                row[x * 3 + 2] = (byte)(p >> 16);
            }
            writer.Write(row);
        }
    }

    /// <summary>Đuôi file ảnh mở được vào Editor (SkiaSharp đọc được; GIF chỉ lấy khung đầu).</summary>
    public static readonly string[] OpenableExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    /// <summary>Ảnh lớn hơn chừng này pixel (≈ 1 GB RAM BGRA) không mở - Editor giữ cả ảnh trong bộ nhớ và vẽ toàn bộ.</summary>
    public const long MaxOpenPixels = 250_000_000;

    public static bool IsOpenableImage(string path) =>
        OpenableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<string>> PickImagesAsync(IntPtr ownerHwnd)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in OpenableExtensions)
        {
            picker.FileTypeFilter.Add(ext);
        }
        InitializeWithWindow.Initialize(picker, ownerHwnd);
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(f => f.Path).ToList();
    }

    /// <summary>Đọc 1 file ảnh về định dạng pixel của ảnh chụp (BGRA premul), xoay / lật theo EXIF (ảnh chụp điện thoại
    /// lưu ngang kèm cờ "xoay 90°"). Ném <see cref="InvalidDataException"/> nếu không đọc được / quá lớn.</summary>
    public static SKBitmap LoadImage(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"Không đọc được ảnh: {Path.GetFileName(path)}");
        int w = codec.Info.Width, h = codec.Info.Height;
        if ((long)w * h > MaxOpenPixels)
        {
            throw new InvalidDataException($"Ảnh quá lớn ({w} × {h}): tối đa {MaxOpenPixels / 1_000_000} triệu pixel.");
        }
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var raw = new SKBitmap(info);
        var result = codec.GetPixels(info, raw.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            raw.Dispose();
            throw new InvalidDataException($"Không đọc được ảnh: {Path.GetFileName(path)} ({result})");
        }
        return ApplyOrigin(raw, codec.EncodedOrigin);
    }

    /// <summary>Đọc nhiều file (nền, không chặn UI) cho nút Mở / kéo-thả. Tên = tên file bỏ đuôi (làm tên tab). File không
    /// phải ảnh / đọc lỗi → vào <c>Errors</c> ("tên file: lý do"), các file khác vẫn mở.</summary>
    public static async Task<(List<(string Name, string Path, SKBitmap Bitmap)> Images, List<string> Errors)> LoadImagesAsync(IEnumerable<string> paths)
    {
        var images = new List<(string, string, SKBitmap)>();
        var errors = new List<string>();
        foreach (var path in paths)
        {
            if (!IsOpenableImage(path))
            {
                errors.Add($"{Path.GetFileName(path)}: không phải ảnh {string.Join(" / ", OpenableExtensions)}");
                continue;
            }
            try
            {
                images.Add((Path.GetFileNameWithoutExtension(path), path, await Task.Run(() => LoadImage(path))));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return (images, errors);
    }

    /// <summary>Xoay / lật theo EXIF Orientation. Ma trận đưa điểm (x, y) của ảnh lưu sang ảnh hiển thị đúng chiều
    /// (W, H = cỡ ảnh lưu; 4 trường hợp cuối đổi rộng ↔ cao).</summary>
    private static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return src;
        }
        float w = src.Width, h = src.Height;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),     // lật ngang
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1), // xoay 180°
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),   // lật dọc
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),       // chuyển vị
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),     // xoay 90° thuận
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1), // chuyển vị ngược
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),   // xoay 90° ngược
            _ => SKMatrix.Identity,
        };
        var dst = new SKBitmap(new SKImageInfo(swap ? src.Height : src.Width, swap ? src.Width : src.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(dst))
        {
            canvas.SetMatrix(m);
            canvas.DrawBitmap(src, 0, 0);
        }
        src.Dispose();
        return dst;
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

    /// <param name="baseName">Tên file chưa có đuôi; có thể kèm thư mục con (vd theo mẫu tên khi tự lưu "{date}\..." -
    /// <see cref="Models.FileNameTemplate"/>) → tạo thư mục con nếu chưa có.</param>
    public string SavePngToFolder(SKBitmap bitmap, string folder, string baseName)
    {
        if (Path.GetDirectoryName(baseName) is { Length: > 0 } subfolder)
        {
            folder = Path.Combine(folder, subfolder);
            baseName = Path.GetFileName(baseName);
        }
        Directory.CreateDirectory(folder);
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
