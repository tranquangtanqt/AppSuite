namespace ScreenCapture.Models;

/// <summary>Đóng ảnh đã <b>tự lưu</b> sau khi chụp thì làm gì với file đó (Cài đặt > Lưu ảnh). Mặc định giữ như trước.</summary>
public enum AutoSavedCloseAction
{
    /// <summary>Giữ file (như trước đây).</summary>
    Keep,
    /// <summary>Hỏi có xoá file không (kèm ô "Không hỏi lại").</summary>
    Ask,
    /// <summary>Luôn xoá file (vào Thùng rác).</summary>
    Delete,
}

/// <summary>
/// File tự lưu nào được phép đề nghị xoá khi đóng tab (2026-10-19): chỉ file do <b>tự lưu sau khi chụp</b> tạo ra và tab vẫn
/// còn gắn đúng file đó. Ảnh mở từ file có sẵn, file người dùng tự Lưu thành / Lưu tất cả không bao giờ bị xoá; Lưu đè
/// (Ctrl+S) vào chính file tự lưu vẫn giữ quyền xoá; file đã bị xoá / đổi tên bên ngoài thì bỏ qua.
/// </summary>
public static class AutoSavedFile
{
    /// <returns>Đường dẫn được xoá, hoặc null.</returns>
    public static string? Deletable(string? autoSavedPath, string? filePath, Func<string, bool> exists) =>
        autoSavedPath is { Length: > 0 } path && filePath is not null
            && string.Equals(Path.GetFullPath(path), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase)
            && exists(path)
            ? path
            : null;

    /// <summary>Lưu xong vào <paramref name="savedPath"/>: còn giữ cờ tự lưu không (chỉ khi ghi đè đúng file tự lưu).</summary>
    public static string? AfterSave(string? autoSavedPath, string savedPath) =>
        autoSavedPath is not null && string.Equals(Path.GetFullPath(autoSavedPath), Path.GetFullPath(savedPath), StringComparison.OrdinalIgnoreCase)
            ? autoSavedPath
            : null;
}
