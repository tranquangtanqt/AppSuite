using System.Net;
using System.Text.RegularExpressions;

namespace MeRon.Core.Import;

/// <summary>1 mục trong folder Google Drive: id, tên, là thư mục hay file.</summary>
public sealed record DriveEntry(string Id, string Name, bool IsFolder);

/// <summary>
/// Folder Google Drive để chế độ "Bất kỳ ai có đường liên kết" - không cần đăng nhập. Giống
/// <see cref="LocalFolderBackupSource"/>: link là chính folder 1 lần backup, hoặc folder cha (folder điện thoại tải lên)
/// → tự chọn folder con mới nhất dạng yyyyMMdd_HHmmss. Tải các file .tsv cần dùng về <c>downloadRoot\&lt;tên bản&gt;</c>
/// rồi trả <see cref="BackupFileSet"/> như backup trên máy, nên phần nạp không đổi.
/// <para>Danh sách file lấy từ trang embeddedfolderview của Google (không phải API chính thức, không cần khoá API) -
/// Google đổi trang thì báo lỗi rõ ràng, vẫn nhập từ thư mục trên máy được.</para>
/// </summary>
public sealed partial class GoogleDriveBackupSource(
    string folderId, string downloadRoot, HttpClient http, IProgress<string>? progress = null, string? baseUrl = null)
    : IBackupSource
{
    [GeneratedRegex(@"^\d{8}_\d{6}$")]
    private static partial Regex BackupFolderName();

    /// <summary>drive.google.com/drive/folders/ID, …/drive/u/0/folders/ID?usp=sharing, …/open?id=ID, …?id=ID.</summary>
    [GeneratedRegex(@"^https?://drive\.google\.com/(?:drive/(?:u/\d+/)?folders/(?<id>[\w-]{10,})|(?:open|embeddedfolderview)\?(?:.*&)?id=(?<id>[\w-]{10,}))",
        RegexOptions.IgnoreCase)]
    private static partial Regex FolderLink();

    [GeneratedRegex(@"<div class=""flip-entry"" id=""entry-(?<id>[\w-]+)"".*?<a href=""(?<href>[^""]+)"".*?<div class=""flip-entry-title"">(?<name>[^<]*)<",
        RegexOptions.Singleline)]
    private static partial Regex Entry();

    /// <summary>Lấy id folder từ link Google Drive; không phải link folder Drive → null.</summary>
    public static string? ParseFolderId(string? input)
    {
        var match = FolderLink().Match(input?.Trim().Trim('"') ?? string.Empty);
        return match.Success ? match.Groups["id"].Value : null;
    }

    public static string FolderUrl(string id) => $"https://drive.google.com/drive/folders/{id}";

    /// <summary>Gốc của link đọc danh sách / tải file. Biến môi trường MERON_DRIVE_BASE_URL đổi sang Drive giả (test GUI
    /// trong Sandbox không có Internet).</summary>
    private readonly string _base = (baseUrl ?? Environment.GetEnvironmentVariable("MERON_DRIVE_BASE_URL") switch
    {
        { Length: > 0 } custom => custom,
        _ => "https://drive.google.com",
    }).TrimEnd('/');

    /// <summary>Đọc trang danh sách của Google: chỉ lấy id / tên / loại của từng mục.</summary>
    public static IReadOnlyList<DriveEntry> ParseListing(string html) =>
        Entry().Matches(html)
            .Select(m => new DriveEntry(
                m.Groups["id"].Value,
                WebUtility.HtmlDecode(m.Groups["name"].Value).Trim(),
                m.Groups["href"].Value.Contains("/folders/", StringComparison.Ordinal)))
            .ToList();

    public async Task<BackupFileSet> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        progress?.Report("Đang đọc folder Google Drive...");
        var entries = await ListAsync(folderId, cancellationToken);

        var sourceFolderId = folderId;
        string? name = null;
        if (!HasAnyBackupFile(entries))
        {
            var latest = entries
                .Where(e => e.IsFolder && BackupFolderName().IsMatch(e.Name))
                .OrderByDescending(e => e.Name, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? throw new BackupSourceException(
                    "Folder Google Drive này không có file backup (.tsv) nào, và cũng không có folder con dạng yyyyMMdd_HHmmss.");
            sourceFolderId = latest.Id;
            name = latest.Name;
            progress?.Report($"Bản mới nhất: {name}. Đang đọc danh sách file...");
            entries = await ListAsync(latest.Id, cancellationToken);
            if (!HasAnyBackupFile(entries))
            {
                throw new BackupSourceException($"Folder {name} trên Google Drive không có file backup (.tsv) nào.");
            }
        }

        // Folder chính là 1 lần backup: lấy tên từ hậu tố file (orders_20261011_231039.tsv → 20261011_231039).
        var toDownload = PickFiles(entries);
        name ??= BackupNameFromFiles(toDownload) ?? folderId;

        var target = Path.Combine(downloadRoot, SafeName(name));
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
            await DownloadAsync(file, Path.Combine(target, SafeName(file.Name)), cancellationToken);
        }

        return new BackupFileSet
        {
            Name = name,
            Source = FolderUrl(sourceFolderId),
            Files = LocalFolderBackupSource.FindFiles(target),
        };
    }

    private async Task<IReadOnlyList<DriveEntry>> ListAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await GetAsync($"{_base}/embeddedfolderview?id={Uri.EscapeDataString(id)}", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new BackupSourceException(
                "Không mở được folder Google Drive: link sai, folder đã xoá, hoặc folder chưa chia sẻ \"Bất kỳ ai có đường liên kết\".");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new BackupSourceException($"Google Drive trả lỗi {(int)response.StatusCode} khi đọc folder. Thử lại sau.");
        }
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!html.Contains("flip-entries", StringComparison.Ordinal))
        {
            // Bị chuyển sang trang đăng nhập, hoặc Google đổi giao diện trang danh sách.
            throw new BackupSourceException(
                "Không đọc được danh sách file của folder Google Drive (folder chưa chia sẻ công khai, hoặc Google đã đổi trang). " +
                "Có thể tải folder về máy rồi chọn thư mục như bình thường.");
        }
        return ParseListing(html);
    }

    private async Task DownloadAsync(DriveEntry file, string path, CancellationToken cancellationToken)
    {
        using var response = await GetAsync($"{_base}/uc?export=download&id={Uri.EscapeDataString(file.Id)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BackupSourceException($"Không tải được {file.Name} từ Google Drive (lỗi {(int)response.StatusCode}).");
        }
        if (response.Content.Headers.ContentType?.MediaType == "text/html")
        {
            // File không chia sẻ / vượt hạn mức tải → Google trả trang HTML thay vì nội dung file.
            throw new BackupSourceException($"Google Drive không cho tải {file.Name} (file chưa chia sẻ công khai hoặc tạm vượt hạn mức tải).");
        }
        await using var output = File.Create(path);
        await response.Content.CopyToAsync(output, cancellationToken);
    }

    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BackupSourceException("Không kết nối được Google Drive (kiểm tra mạng): " + ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BackupSourceException("Google Drive không trả lời (hết thời gian chờ). Kiểm tra mạng rồi thử lại.");
        }
    }

    private static bool HasAnyBackupFile(IReadOnlyList<DriveEntry> entries) => PickFiles(entries).Count > 0;

    /// <summary>Mỗi bảng 1 file "x_*.tsv" (tên lớn nhất = mới nhất) - cùng quy tắc với <see cref="LocalFolderBackupSource.FindFiles"/>.</summary>
    internal static List<DriveEntry> PickFiles(IReadOnlyList<DriveEntry> entries)
    {
        var files = entries.Where(e => !e.IsFolder && e.Name.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)).ToList();
        var result = new List<DriveEntry>();
        foreach (var table in Data.Schema.BackupTables)
        {
            var match = files
                .Where(f => f.Name.StartsWith(table.FilePrefix + "_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match is not null)
            {
                result.Add(match);
            }
        }
        return result;
    }

    private static string? BackupNameFromFiles(IEnumerable<DriveEntry> files) =>
        files.Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .Select(n => n.Length > 15 ? n[^15..] : string.Empty)
            .FirstOrDefault(n => BackupFolderName().IsMatch(n));

    private static string SafeName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
