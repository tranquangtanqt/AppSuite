using SkiaSharp;

namespace ScreenCapture.Services;

public interface IImageFileService
{
    /// <summary>Shows a Save As dialog and encodes the bitmap as PNG. Returns the saved path, or null
    /// if the user cancelled. <paramref name="ownerHwnd"/> is required because this is an unpackaged
    /// app - FileSavePicker needs IInitializeWithWindow to know which window owns the dialog.</summary>
    Task<string?> SaveAsPngAsync(SKBitmap bitmap, IntPtr ownerHwnd);

    /// <summary>Hộp thoại chọn thư mục (dùng cho "Đóng tất cả" → lưu tất cả). Null nếu huỷ.</summary>
    Task<string?> PickFolderAsync(IntPtr ownerHwnd);

    /// <summary>Ghi PNG vào <paramref name="folder"/> với tên <paramref name="baseName"/>.png; trùng tên
    /// thì thêm " (2)", " (3)"... - không bao giờ ghi đè file có sẵn. Trả về đường dẫn đã ghi.</summary>
    string SavePngToFolder(SKBitmap bitmap, string folder, string baseName);
}
