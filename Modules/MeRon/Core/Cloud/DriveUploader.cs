using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MeRon.Core.Cloud;

/// <summary>Kết quả upload 1 bộ backup: folder vừa tạo trên Drive (link mở trên web) + số file.</summary>
public sealed record DriveUploadResult(string FolderId, string FolderName, string? WebLink, int FileCount);

/// <summary>
/// Upload 1 bộ backup TSV lên Google Drive như app điện thoại (drive_upload_service.dart): tạo folder con tên
/// yyyyMMdd_HHmmss trong folder đích rồi tải từng file .tsv vào. Folder đích: id do người dùng chọn (vd folder điện thoại
/// đang dùng), hoặc null → folder riêng "MeRon backup" ở My Drive (app tự tạo, tạo lại nếu đã xoá).
/// </summary>
public sealed class DriveUploader(HttpClient http, Func<CancellationToken, Task<string>> accessToken,
    string apiUrl = DriveUploader.DefaultApiUrl, string uploadUrl = DriveUploader.DefaultUploadUrl)
{
    public const string DefaultApiUrl = "https://www.googleapis.com/drive/v3";
    public const string DefaultUploadUrl = "https://www.googleapis.com/upload/drive/v3";
    public const string OwnFolderName = "MeRon backup";
    private const string FolderMime = "application/vnd.google-apps.folder";

    /// <param name="localFolder">Thư mục bộ backup vừa xuất (chứa các file .tsv).</param>
    /// <param name="parentId">Folder đích; null = folder riêng "MeRon backup".</param>
    public async Task<DriveUploadResult> UploadBackupAsync(string localFolder, string? parentId, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = Directory.GetFiles(localFolder, "*.tsv").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            throw new GoogleAuthException($"Không có file .tsv trong {localFolder}.");
        }
        var parent = parentId ?? await OwnFolderAsync(cancellationToken);
        var name = Path.GetFileName(localFolder.TrimEnd(Path.DirectorySeparatorChar));
        progress?.Report($"Tạo thư mục {name} trên Google Drive...");
        var folder = await CreateFolderAsync(name, parent, cancellationToken, isTarget: parentId is not null);
        var done = 0;
        foreach (var file in files)
        {
            progress?.Report($"Đang tải lên {++done}/{files.Count}: {Path.GetFileName(file)}");
            await UploadFileAsync(file, folder.Id, cancellationToken);
        }
        return new DriveUploadResult(folder.Id, name, folder.Link, files.Count);
    }

    /// <summary>Folder "MeRon backup" ở My Drive do chính app tạo (quyền drive.file chỉ thấy folder app tạo).</summary>
    private async Task<string> OwnFolderAsync(CancellationToken cancellationToken)
    {
        var q = $"name = '{OwnFolderName}' and mimeType = '{FolderMime}' and trashed = false and 'root' in parents";
        using var response = await SendAsync(HttpMethod.Get, $"{apiUrl}/files?q={Uri.EscapeDataString(q)}&fields=files(id)&spaces=drive", null, cancellationToken);
        var json = await ReadAsync(response, cancellationToken);
        var found = json.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("id").GetString()).FirstOrDefault();
        return found ?? (await CreateFolderAsync(OwnFolderName, "root", cancellationToken, isTarget: false)).Id;
    }

    private async Task<(string Id, string? Link)> CreateFolderAsync(string name, string parent, CancellationToken cancellationToken, bool isTarget)
    {
        var metadata = JsonSerializer.Serialize(new { name, mimeType = FolderMime, parents = new[] { parent } });
        using var response = await SendAsync(HttpMethod.Post, $"{apiUrl}/files?fields=id,webViewLink&supportsAllDrives=true",
            new StringContent(metadata, Encoding.UTF8, "application/json"), cancellationToken);
        if (isTarget && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            throw new GoogleAuthException("Không ghi được vào folder Google Drive đã chọn: không tìm thấy folder, tài khoản không có quyền sửa, " +
                                          "hoặc folder không do MeRon / app điện thoại tạo. Xoá trắng ô Folder để tải lên folder riêng \"MeRon backup\".");
        }
        var json = await ReadAsync(response, cancellationToken);
        return (json.GetProperty("id").GetString()!, json.TryGetProperty("webViewLink", out var link) ? link.GetString() : null);
    }

    private async Task UploadFileAsync(string path, string folderId, CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new { name = Path.GetFileName(path), parents = new[] { folderId } });
        var content = new MultipartContent("related")
        {
            new StringContent(metadata, Encoding.UTF8, "application/json"),
            new ByteArrayContent(await File.ReadAllBytesAsync(path, cancellationToken)) { Headers = { ContentType = new MediaTypeHeaderValue("text/tab-separated-values") } },
        };
        using var response = await SendAsync(HttpMethod.Post, $"{uploadUrl}/files?uploadType=multipart&fields=id&supportsAllDrives=true", content, cancellationToken);
        await ReadAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken(cancellationToken));
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GoogleAuthException("Không kết nối được Google Drive - kiểm tra mạng.", ex);
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement json = default;
        try
        {
            json = JsonDocument.Parse(text.Length == 0 ? "{}" : text).RootElement.Clone();
        }
        catch (JsonException)
        {
        }
        if (!response.IsSuccessStatusCode)
        {
            var message = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                          && error.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw new GoogleAuthException($"Google Drive báo lỗi (HTTP {(int)response.StatusCode}){(message is null ? "." : ": " + message)}");
        }
        return json;
    }
}
