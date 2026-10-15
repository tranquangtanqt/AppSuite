using MeRon.Core.Cloud;

namespace MeRon.Core.Import;

/// <summary>
/// 1 bộ backup đã chọn trong danh sách Google Drive (đăng nhập, <see cref="DriveBackupBrowser"/>): tải các file .tsv cần dùng
/// về <c>downloadRoot\&lt;tên bản&gt;</c> rồi trả <see cref="BackupFileSet"/> như backup trên máy - phần nạp không đổi.
/// Lỗi Google (mạng, quyền) giữ nguyên <see cref="GoogleAuthException"/> để trang Nhập biết khi nào cần đăng nhập lại.
/// </summary>
public sealed class DriveApiBackupSource(DriveBackup backup, DriveBackupBrowser browser, string downloadRoot, IProgress<string>? progress = null)
    : IBackupSource
{
    public async Task<BackupFileSet> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var toDownload = GoogleDriveBackupSource.PickFiles(backup.Files);
        if (toDownload.Count == 0)
        {
            throw new BackupSourceException($"Bản {backup.Name} trên Google Drive không có file backup (.tsv) nào.");
        }

        var target = Path.Combine(downloadRoot, GoogleDriveBackupSource.SafeName(backup.Name));
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
        Directory.CreateDirectory(target);

        var index = 0;
        foreach (var file in toDownload)
        {
            index++;
            progress?.Report($"Đang tải {index}/{toDownload.Count}: {file.Name}");
            await browser.DownloadAsync(file.Id, Path.Combine(target, GoogleDriveBackupSource.SafeName(file.Name)), cancellationToken);
        }

        return new BackupFileSet
        {
            Name = backup.Name,
            Source = GoogleDriveBackupSource.FolderUrl(backup.FolderId),
            Files = LocalFolderBackupSource.FindFiles(target),
        };
    }
}
