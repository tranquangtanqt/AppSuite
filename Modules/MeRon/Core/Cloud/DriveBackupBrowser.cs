using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeRon.Core.Data;
using MeRon.Core.Import;

namespace MeRon.Core.Cloud;

/// <summary>
/// 1 bộ backup trên Google Drive (folder yyyyMMdd_HHmmss do app điện thoại hoặc MeRon tạo) + các file trong đó.
/// <see cref="InOwnFolder"/>: nằm trong folder riêng "MeRon backup" của bản máy tính (không thì thường là folder của điện thoại).
/// </summary>
public sealed record DriveBackup(string FolderId, string Name, bool InOwnFolder, IReadOnlyList<DriveEntry> Files)
{
    public DateTime? Time =>
        DateTime.TryParseExact(Name, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    /// <summary>Số bảng có file (đủ = <see cref="Schema.BackupTables"/>.Count).</summary>
    public int TableCount => GoogleDriveBackupSource.PickFiles(Files).Count;

    public bool IsComplete => TableCount == Schema.BackupTables.Count;
}

/// <summary>
/// Đọc các bộ backup trên Google Drive bằng tài khoản đã đăng nhập (quyền drive.file - chỉ thấy file do MeRon / app điện thoại
/// tạo, cùng project Google Cloud "me-ron") rồi tải file về. Không cần folder chia sẻ công khai như
/// <see cref="GoogleDriveBackupSource"/>. Chỉ 2 lần đọc danh sách (mọi folder + mọi file app thấy), không đọc từng folder.
/// </summary>
public sealed partial class DriveBackupBrowser(HttpClient http, Func<CancellationToken, Task<string>> accessToken,
    string apiUrl = DriveUploader.DefaultApiUrl)
{
    [GeneratedRegex(@"^\d{8}_\d{6}$")]
    private static partial Regex BackupFolderName();

    private sealed record Item(string Id, string Name, bool IsFolder, IReadOnlyList<string> Parents);

    /// <summary>Các bộ backup mới nhất trước (theo tên yyyyMMdd_HHmmss). Folder trống (không có file .tsv nào) bị bỏ qua.</summary>
    public async Task<IReadOnlyList<DriveBackup>> ListAsync(CancellationToken cancellationToken = default)
    {
        var folders = await QueryAsync($"mimeType = '{DriveUploader.FolderMime}' and trashed = false", cancellationToken);
        var files = await QueryAsync($"mimeType != '{DriveUploader.FolderMime}' and trashed = false", cancellationToken);

        var own = folders.Where(f => f.Name == DriveUploader.OwnFolderName).Select(f => f.Id).ToHashSet();
        var filesByFolder = files
            .Where(f => f.Name.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => f.Parents.Select(p => (Parent: p, File: f)))
            .ToLookup(x => x.Parent, x => new DriveEntry(x.File.Id, x.File.Name, false));

        return folders
            .Where(f => BackupFolderName().IsMatch(f.Name) && filesByFolder[f.Id].Any())
            .Select(f => new DriveBackup(f.Id, f.Name, f.Parents.Any(own.Contains), filesByFolder[f.Id].ToList()))
            .OrderByDescending(b => b.Name, StringComparer.Ordinal)
            .ThenByDescending(b => b.TableCount)
            .ToList();
    }

    /// <summary>Tải nội dung 1 file về <paramref name="path"/>.</summary>
    public async Task DownloadAsync(string fileId, string path, CancellationToken cancellationToken = default)
    {
        using var response = await DriveHttp.SendAsync(http, accessToken, HttpMethod.Get,
            $"{apiUrl}/files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true", null, cancellationToken,
            HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            await DriveHttp.ReadAsync(response, cancellationToken); // ném lỗi có thông báo của Google
        }
        await using var output = File.Create(path);
        await response.Content.CopyToAsync(output, cancellationToken);
    }

    private async Task<List<Item>> QueryAsync(string q, CancellationToken cancellationToken)
    {
        var items = new List<Item>();
        string? pageToken = null;
        do
        {
            var url = $"{apiUrl}/files?q={Uri.EscapeDataString(q)}&spaces=drive&pageSize=1000" +
                      $"&fields={Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,parents)")}" +
                      (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            using var response = await DriveHttp.SendAsync(http, accessToken, HttpMethod.Get, url, null, cancellationToken);
            var json = await DriveHttp.ReadAsync(response, cancellationToken);
            if (json.TryGetProperty("files", out var files))
            {
                foreach (var f in files.EnumerateArray())
                {
                    items.Add(new Item(
                        f.GetProperty("id").GetString()!,
                        f.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                        f.TryGetProperty("mimeType", out var m) && m.GetString() == DriveUploader.FolderMime,
                        f.TryGetProperty("parents", out var p) && p.ValueKind == JsonValueKind.Array
                            ? p.EnumerateArray().Select(x => x.GetString()!).ToList()
                            : []));
                }
            }
            pageToken = json.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (!string.IsNullOrEmpty(pageToken));
        return items;
    }
}
