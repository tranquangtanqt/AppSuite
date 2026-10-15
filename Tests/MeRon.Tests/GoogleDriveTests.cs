using System.Net;
using System.Text;
using MeRon.Core.Data;
using MeRon.Core.Import;

namespace MeRon.Tests;

/// <summary>
/// Google Drive giả: phục vụ trang embeddedfolderview (đúng cấu trúc HTML thật của Google, rút gọn) và link tải
/// uc?export=download từ 1 cây thư mục trên máy. Id của thư mục / file = đường dẫn tương đối, đổi ký tự lạ thành "-".
/// </summary>
internal sealed class FakeDrive(string root) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    /// <summary>Id → trả trang HTML thay cho nội dung file (file chưa chia sẻ).</summary>
    public HashSet<string> HtmlInsteadOfFile { get; } = [];

    public static string IdOf(string root, string path) =>
        "id-" + Path.GetRelativePath(root, path).Replace('\\', '-').Replace('.', '-').Replace(' ', '-');

    private string? PathOf(string id) =>
        id == "id-root" ? root : Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).FirstOrDefault(p => IdOf(root, p) == id);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        var id = Uri.UnescapeDataString(url[(url.LastIndexOf("id=", StringComparison.Ordinal) + 3)..]);
        var path = PathOf(id);
        if (path is null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>404</html>") });
        }
        if (url.Contains("embeddedfolderview", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Listing(path), Encoding.UTF8, "text/html") });
        }
        if (HtmlInsteadOfFile.Contains(id))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Sign in</html>", Encoding.UTF8, "text/html") });
        }
        var content = new ByteArrayContent(File.ReadAllBytes(path));
        content.Headers.ContentType = new("application/octet-stream");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private string Listing(string folder)
    {
        var html = new StringBuilder($"<html><head><title>{WebUtility.HtmlEncode(Path.GetFileName(folder))}</title></head><body><div class=\"flip-entries\">");
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
        {
            var id = IdOf(root, entry);
            var href = Directory.Exists(entry)
                ? $"https://drive.google.com/drive/folders/{id}"
                : $"https://drive.google.com/file/d/{id}/view?usp=drive_web";
            html.Append($"<div class=\"flip-entry\" id=\"entry-{id}\" tabindex=\"0\" role=\"link\"><div class=\"flip-entry-info\"><a href=\"{href}\" target=\"_blank\">")
                .Append("<div class=\"flip-entry-visual\"></div>")
                .Append($"<div class=\"flip-entry-list-icon\"></div><div class=\"flip-entry-title\">{WebUtility.HtmlEncode(Path.GetFileName(entry))}</div></a>")
                .Append("<div class=\"flip-entry-last-modified\"><div>Jul 10</div></div></div></div>");
        }
        return html.Append("</div></body></html>").ToString();
    }
}

public class GoogleDriveTests
{
    [Theory]
    [InlineData("https://drive.google.com/drive/folders/1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT", "1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT")]
    [InlineData("https://drive.google.com/drive/folders/1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT?usp=sharing", "1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT")]
    [InlineData("  \"https://drive.google.com/drive/u/1/folders/1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT\"  ", "1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT")]
    [InlineData("https://drive.google.com/open?id=1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT", "1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT")]
    [InlineData("http://DRIVE.google.com/embeddedfolderview?usp=x&id=1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT#list", "1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT")]
    [InlineData(@"D:\Backup\MeRon", null)]
    [InlineData("https://drive.google.com/file/d/1aXH-yWwRX_Nq8xWWlGFEtVnce0bQIRCq/view", null)]
    [InlineData("https://example.com/drive/folders/1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT", null)]
    [InlineData("", null)]
    public void Parses_folder_id_from_drive_links_only(string input, string? expected)
    {
        Assert.Equal(expected, GoogleDriveBackupSource.ParseFolderId(input));
    }

    [Fact]
    public void Parses_listing_html_of_google()
    {
        // Rút từ trang thật (2026-10-13): folder con + file, tên có ký tự HTML.
        const string html =
            "<div class=\"flip-entries\"><div class=\"flip-entry\" id=\"entry-1nluXSJzG7xFf7Ut6dvPzU1PYVHnEZsIK\" tabindex=\"0\" role=\"link\"><div class=\"flip-entry-info\">" +
            "<a href=\"https://drive.google.com/drive/folders/1nluXSJzG7xFf7Ut6dvPzU1PYVHnEZsIK\" target=\"_blank\"><div class=\"flip-entry-visual\"><div class=\"flip-entry-visual-card\">" +
            "<div class=\"flip-entry-icon\"><div aria-label=\"Folder\" class=\"icon-color-1 drive-sprite-folder-grid-shared-icon\"></div></div></div></div>" +
            "<div class=\"flip-entry-list-icon\"></div><div class=\"flip-entry-title\">20260710_233456</div></a></div></div>" +
            "<div class=\"flip-entry\" id=\"entry-1aXH-yWwRX_Nq8xWWlGFEtVnce0bQIRCq\" tabindex=\"0\" role=\"link\"><div class=\"flip-entry-info\">" +
            "<a href=\"https://drive.google.com/file/d/1aXH-yWwRX_Nq8xWWlGFEtVnce0bQIRCq/view?usp=drive_web\" target=\"_blank\">" +
            "<div class=\"flip-entry-title\">Ghi ch&uacute; &amp; m&#7865;o.txt</div></a></div></div></div>";

        var entries = GoogleDriveBackupSource.ParseListing(html);

        Assert.Equal(
            [new DriveEntry("1nluXSJzG7xFf7Ut6dvPzU1PYVHnEZsIK", "20260710_233456", true), new DriveEntry("1aXH-yWwRX_Nq8xWWlGFEtVnce0bQIRCq", "Ghi chú & mẹo.txt", false)],
            entries);
    }

    [Fact]
    public async Task Parent_folder_downloads_newest_backup_and_imports()
    {
        using var temp = new TempFolder();
        var drive = temp.Sub("drive");
        Backup.Write(drive, "20261008_204533");
        Backup.Write(drive, "20261011_231039", f => f["users"] += "\n3\tChị Mới\t\t\t\t2026-10-11 07:20:53\t2026-10-11 07:20:53");
        Backup.Write(drive, "20261012_225408_bk"); // bản phụ - bỏ qua
        File.WriteAllText(Path.Combine(drive, "ghi chu.txt"), "x");
        var fake = new FakeDrive(drive);
        var progress = new List<string>();
        var source = new GoogleDriveBackupSource("id-root", temp.Sub("download"), new HttpClient(fake), new SyncProgress(progress));

        var backup = await source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal("20261011_231039", backup.Name);
        Assert.Equal("https://drive.google.com/drive/folders/id-20261011_231039", backup.Source);
        Assert.Empty(backup.Missing);
        Assert.All(backup.Files.Values, f => Assert.StartsWith(Path.Combine(temp.Path, "download", "20261011_231039"), f));
        Assert.Equal(8, fake.Requests.Count(r => r.Contains("uc?export=download", StringComparison.Ordinal)));
        Assert.Contains(progress, p => p.Contains("Đang tải 8/8", StringComparison.Ordinal));

        var db = temp.NewDatabase();
        var plan = BackupImporter.Prepare(backup);
        Assert.True(plan.CanImport, string.Join("\n", plan.Errors));
        BackupImporter.Apply(plan, db);
        Assert.Equal(3, db.Count(Schema.Users.Name));
        Assert.Equal("https://drive.google.com/drive/folders/id-20261011_231039", db.GetMeta(MeRonDatabase.MetaKeys.LastImportSource));
    }

    [Fact]
    public async Task Parent_folder_and_parent_directory_list_every_backup_to_choose_from()
    {
        using var temp = new TempFolder();
        var drive = temp.Sub("drive");
        Backup.Write(drive, "20261013_203040");
        Backup.Write(drive, "20261014_235508");
        Backup.Write(drive, "20261014_164527");
        Backup.Write(drive, "20261012_225408_bk"); // bản phụ - bỏ qua
        Directory.CreateDirectory(Path.Combine(drive, "20261015_000000")); // folder trống
        var ct = TestContext.Current.CancellationToken;

        var http = new HttpClient(new FakeDrive(drive));
        var folders = await new GoogleDriveBackupSource("id-root", temp.Sub("download"), http).ListBackupFoldersAsync(ct);
        // Drive (link công khai) không biết folder nào trống mà không đọc từng folder → vẫn liệt kê, chọn thì báo lỗi rõ.
        Assert.Equal(["20261015_000000", "20261014_235508", "20261014_164527", "20261013_203040"], folders.Select(f => f.Name));

        // Chọn 1 bản khác bản mới nhất: link thẳng folder đó, giữ đúng tên folder
        var chosen = folders.Single(f => f.Name == "20261013_203040");
        var backup = await new GoogleDriveBackupSource(chosen.Id, temp.Sub("download"), http, knownName: chosen.Name).GetLatestAsync(ct);
        Assert.Equal("20261013_203040", backup.Name);
        Assert.Empty(backup.Missing);

        // Thư mục cha trên máy: bỏ folder trống; chọn thẳng 1 bản → không có danh sách
        Assert.Equal(["20261014_235508", "20261014_164527", "20261013_203040"],
            new LocalFolderBackupSource(drive).ListBackupFolders().Select(Path.GetFileName));
        Assert.Empty(new LocalFolderBackupSource(Path.Combine(drive, "20261013_203040")).ListBackupFolders());
        Assert.Empty(await new GoogleDriveBackupSource(chosen.Id, temp.Path, http).ListBackupFoldersAsync(ct));
    }

    [Fact]
    public async Task Link_to_a_single_backup_folder_takes_name_from_files_and_replaces_old_download()
    {
        using var temp = new TempFolder();
        var drive = temp.Sub("drive");
        var folder = Backup.Write(drive, "20261011_231039");
        var download = temp.Sub("download");
        var stale = Path.Combine(download, "20261011_231039");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "orders_29991231_000000.tsv"), "rác của lần tải trước");

        var backup = await new GoogleDriveBackupSource(FakeDrive.IdOf(drive, folder), download, new HttpClient(new FakeDrive(drive)))
            .GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal("20261011_231039", backup.Name);
        Assert.Equal(8, Directory.GetFiles(Path.Combine(download, "20261011_231039")).Length);
        Assert.EndsWith("orders_20261011_231039.tsv", backup.Files[Schema.Orders]);
    }

    [Fact]
    public async Task Clear_errors_for_missing_folder_empty_folder_and_unshared_file()
    {
        using var temp = new TempFolder();
        var drive = temp.Sub("drive");
        Directory.CreateDirectory(Path.Combine(drive, "Ảnh"));
        var fake = new FakeDrive(drive);
        var http = new HttpClient(fake);
        var ct = TestContext.Current.CancellationToken;

        var missing = await Assert.ThrowsAsync<BackupSourceException>(() => new GoogleDriveBackupSource("id-khong-co", temp.Path, http).GetLatestAsync(ct));
        Assert.Contains("Bất kỳ ai có đường liên kết", missing.Message);

        var empty = await Assert.ThrowsAsync<BackupSourceException>(() => new GoogleDriveBackupSource("id-root", temp.Path, http).GetLatestAsync(ct));
        Assert.Contains("yyyyMMdd_HHmmss", empty.Message);

        var folder = Backup.Write(drive, "20261011_231039");
        fake.HtmlInsteadOfFile.Add(FakeDrive.IdOf(drive, Path.Combine(folder, "users_20261011_231039.tsv")));
        var unshared = await Assert.ThrowsAsync<BackupSourceException>(() => new GoogleDriveBackupSource("id-root", temp.Sub("dl"), http).GetLatestAsync(ct));
        Assert.Contains("users_20261011_231039.tsv", unshared.Message);
    }

    [Fact]
    public async Task Page_without_listing_is_reported_not_parsed_as_empty()
    {
        using var temp = new TempFolder();
        var http = new HttpClient(new StubHandler("<html><body>Sign in - Google Accounts</body></html>"));

        var ex = await Assert.ThrowsAsync<BackupSourceException>(
            () => new GoogleDriveBackupSource("abc", temp.Path, http).GetLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Không đọc được danh sách", ex.Message);
    }

    [Fact]
    public async Task Real_google_drive_folder_downloads_and_passes_checks()
    {
        var link = Environment.GetEnvironmentVariable("MERON_REAL_DRIVE");
        Assert.SkipWhen(string.IsNullOrEmpty(link), "MERON_REAL_DRIVE chưa đặt (link folder Google Drive công khai)");

        using var temp = new TempFolder();
        var id = GoogleDriveBackupSource.ParseFolderId(link) ?? link!;
        var backup = await new GoogleDriveBackupSource(id, temp.Path, new HttpClient()).GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Empty(backup.Missing);
        var plan = BackupImporter.Prepare(backup);
        Assert.True(plan.CanImport, string.Join("\n", plan.Errors));
        TestContext.Current.SendDiagnosticMessage($"{backup.Name}: " + string.Join(", ", plan.Tables.Select(t => $"{t.Table.Name}={t.Rows.Count}")));
    }

    /// <summary>Progress&lt;T&gt; gọi qua SynchronizationContext (không đồng bộ) - test cần ghi nhận ngay.</summary>
    private sealed class SyncProgress(List<string> sink) : IProgress<string>
    {
        public void Report(string value) => sink.Add(value);
    }

    private sealed class StubHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") });
    }
}
