using MeRon.Core;

namespace MeRon.Tests;

/// <summary>Tự dọn ảnh Chụp ảnh cũ (Pictures\MeRon) - chạy trên thư mục tạm, xoá thật thay cho Thùng rác.</summary>
public class CaptureFilesTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0);

    private static string Make(string folder, string name, int daysOld)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
        File.SetLastWriteTime(path, Now.AddDays(-daysOld));
        return path;
    }

    [Fact]
    public void Only_meron_images_older_than_keep_days_are_removed()
    {
        using var temp = new TempFolder();
        var old1 = Make(temp.Path, "chi_tiet_don_hang_20260901_080000.png", 52);
        var old2 = Make(temp.Path, "hoa_don_cong_no_20260920_090000.png", 31);
        var fresh = Make(temp.Path, "hoa_don_cong_no_20261020_090000.png", 3);
        var edge = Make(temp.Path, "chi_tiet_don_hang_20260923_130000.png", 29);
        var other = Make(temp.Path, "anh_cua_toi.png", 200);          // ảnh người dùng tự để: không đụng
        var notPng = Make(temp.Path, "chi_tiet_don_hang_ghi_chu.txt", 200);

        Assert.Equal([old1, old2], CaptureFiles.Expired(temp.Path, 30, Now));

        var deleted = new List<string>();
        var (count, failed) = CaptureFiles.CleanUp(temp.Path, 30, Now, f => { deleted.Add(f); File.Delete(f); });
        Assert.Equal((2, 0), (count, failed));
        Assert.All(new[] { fresh, edge, other, notPng }, f => Assert.True(File.Exists(f)));
        Assert.False(File.Exists(old1));
    }

    [Fact]
    public void Keep_zero_or_missing_folder_deletes_nothing_and_errors_are_counted()
    {
        using var temp = new TempFolder();
        var old = Make(temp.Path, "chi_tiet_don_hang_20250101_000000.png", 400);

        Assert.Empty(CaptureFiles.Expired(temp.Path, 0, Now));
        Assert.Empty(CaptureFiles.Expired(Path.Combine(temp.Path, "khong_co"), 30, Now));
        Assert.Equal((0, 1), CaptureFiles.CleanUp(temp.Path, 30, Now, _ => throw new IOException("đang mở")));
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void New_path_uses_prefix_and_timestamp()
    {
        Assert.Equal(Path.Combine(CaptureFiles.Folder, "hoa_don_cong_no_20261023_120000.png"), CaptureFiles.NewPath(CaptureFiles.DebtPrefix, Now));
    }
}
