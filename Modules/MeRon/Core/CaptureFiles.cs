namespace MeRon.Core;

/// <summary>
/// Ảnh PNG do nút "Chụp ảnh" (màn Chi tiết) lưu vào Pictures\MeRon, và tự dọn ảnh cũ: mỗi lần mở app, ảnh MeRon tự đặt tên
/// (<see cref="Prefixes"/>) cũ hơn N ngày (Cài đặt, mặc định <see cref="MenuSettings.DefaultCaptureKeepDays"/>; 0 = giữ hết)
/// được đưa vào Thùng rác. File khác người dùng tự để trong thư mục không bị đụng tới.
/// </summary>
public static class CaptureFiles
{
    /// <summary>Chi tiết thường / mở từ trang Công nợ.</summary>
    public const string DetailPrefix = "chi_tiet_don_hang_";

    public const string DebtPrefix = "hoa_don_cong_no_";

    public static IReadOnlyList<string> Prefixes { get; } = [DetailPrefix, DebtPrefix];

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MeRon");

    /// <summary>Tên file mới: {prefix}yyyyMMdd_HHmmss.png.</summary>
    public static string NewPath(string prefix, DateTime now) => Path.Combine(Folder, $"{prefix}{now:yyyyMMdd_HHmmss}.png");

    /// <summary>Ảnh của MeRon trong <paramref name="folder"/> sửa lần cuối trước <paramref name="now"/> − <paramref name="keepDays"/> ngày.
    /// keepDays ≤ 0 = không có ảnh nào hết hạn.</summary>
    public static IReadOnlyList<string> Expired(string folder, int keepDays, DateTime now)
    {
        if (keepDays <= 0 || !Directory.Exists(folder))
        {
            return [];
        }
        var cutoff = now.AddDays(-keepDays);
        return Directory.EnumerateFiles(folder, "*.png")
            .Where(f => Prefixes.Any(p => Path.GetFileName(f).StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Where(f => File.GetLastWriteTime(f) < cutoff)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Xoá các ảnh hết hạn bằng <paramref name="delete"/> (app: đưa vào Thùng rác). Trả (đã xoá, lỗi) - lỗi 1 file
    /// (đang mở...) thì bỏ qua file đó.</summary>
    public static (int Deleted, int Failed) CleanUp(string folder, int keepDays, DateTime now, Action<string> delete)
    {
        int deleted = 0, failed = 0;
        foreach (var file in Expired(folder, keepDays, now))
        {
            try
            {
                delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                failed++;
            }
        }
        return (deleted, failed);
    }

    /// <summary>Đưa file vào Thùng rác (lấy lại được), không hỏi.</summary>
    public static void SendToRecycleBin(string file) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
}
