using SkiaSharp;

namespace ScreenCapture.Services;

public interface IImageFileService
{
    /// <summary>Chất lượng khi ghi JPG (launcher gán từ AppSettings.JpegQuality).</summary>
    int JpegQuality { get; set; }

    /// <summary>Hộp thoại Lưu thành: chọn PNG / JPG / BMP (<paramref name="defaultExtension"/> chọn sẵn), tên điền sẵn
    /// <paramref name="suggestedName"/>. Trả đường dẫn, null nếu huỷ. App unpackaged nên cần <paramref name="ownerHwnd"/>.</summary>
    Task<string?> PickSavePathAsync(IntPtr ownerHwnd, string suggestedName, string defaultExtension);

    /// <summary>Ghi ảnh ra <paramref name="path"/> theo đuôi file (PNG / JPG / BMP), thay thế file cũ an toàn.</summary>
    void WriteImage(SKBitmap bitmap, string path);

    /// <summary>Hộp thoại chọn 1 hoặc nhiều file ảnh để mở vào Editor. Rỗng nếu huỷ.</summary>
    Task<IReadOnlyList<string>> PickImagesAsync(IntPtr ownerHwnd);

    /// <summary>Hộp thoại chọn thư mục (dùng cho "Đóng tất cả" → lưu tất cả). Null nếu huỷ.</summary>
    Task<string?> PickFolderAsync(IntPtr ownerHwnd);

    /// <summary>Ghi PNG vào <paramref name="folder"/> với tên <paramref name="baseName"/>.png; trùng tên
    /// thì thêm " (2)", " (3)"... - không bao giờ ghi đè file có sẵn. Trả về đường dẫn đã ghi.</summary>
    string SavePngToFolder(SKBitmap bitmap, string folder, string baseName);
}
