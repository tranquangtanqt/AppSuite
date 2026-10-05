namespace FileTools.ViewModels;

/// <summary>Trang dùng "file đang làm" chung: mở trang thì nhận file người dùng chọn gần nhất ở trang khác.</summary>
public interface IUsesSharedFile
{
    /// <summary>Nhận file đang làm chung (file có thật). Trang đang chạy dở thì bỏ qua, không đổi file giữa chừng.</summary>
    void ApplySharedFile(string path);
}

/// <summary>
/// "File đang làm" dùng chung giữa các trang: chọn / kéo-thả / gõ đường dẫn file ở bất kỳ trang nào thì các trang khác (khi
/// mở ra) tự đổi theo - không phải chọn lại file mỗi lần chuyển trang. Chỉ nhớ file có thật; chỉ sống trong phiên làm việc.
/// </summary>
public static class SharedFile
{
    public static string? Current { get; private set; }

    /// <summary>Ghi nhận file người dùng vừa chọn (bỏ qua đường dẫn chưa gõ xong / không tồn tại).</summary>
    public static void Set(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            Current = path;
        }
    }

    /// <summary>Đưa file đang làm vào trang vừa mở (nếu trang dùng file chung).</summary>
    public static void ApplyTo(object? viewModel)
    {
        if (viewModel is IUsesSharedFile page && Current is { } path && File.Exists(path))
        {
            page.ApplySharedFile(path);
        }
    }

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); // đường dẫn đang gõ dở, có ký tự lạ
        }
    }
}
