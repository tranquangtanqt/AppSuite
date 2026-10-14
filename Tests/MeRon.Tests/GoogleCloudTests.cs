using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MeRon.Core.Cloud;

namespace MeRon.Tests;

/// <summary>Google giả: ghi lại request, trả lời theo hàm <c>reply</c>.</summary>
internal sealed class FakeGoogle(Func<HttpRequestMessage, string, (HttpStatusCode, string)> reply) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string Body, string? Auth)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
        var (status, json) = reply(request, body);
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

internal sealed class MemoryTokenStore : ITokenStore
{
    public (string Email, string RefreshToken)? Saved { get; set; }

    public (string Email, string RefreshToken)? Load() => Saved;

    public void Save(string email, string refreshToken) => Saved = (email, refreshToken);

    public void Clear() => Saved = null;
}

public class GoogleAuthTests
{
    private static readonly GoogleClient Client = new("id-123.apps.googleusercontent.com", "secret-xyz");

    private static string IdToken(string email) =>
        "h." + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"email\":\"{email}\",\"sub\":\"1\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".s";

    [Fact]
    public void Reads_desktop_client_file_and_rejects_other_files()
    {
        var client = GoogleClient.Parse("""{"installed":{"client_id":"a.apps.googleusercontent.com","project_id":"me-ron","client_secret":"s","redirect_uris":["http://localhost"]}}""");
        Assert.Equal(("a.apps.googleusercontent.com", "s"), (client.ClientId, client.ClientSecret));
        Assert.Throws<GoogleAuthException>(() => GoogleClient.Parse("""{"web":{"client_id":"a"}}"""));
        Assert.Throws<GoogleAuthException>(() => GoogleClient.Parse("không phải json"));

        using var temp = new TempFolder();
        Assert.Null(GoogleClient.Find(temp.Path, Path.Combine(temp.Path, "khong-co")));
        File.WriteAllText(Path.Combine(temp.Path, "client_secret_1.json"), """{"installed":{"client_id":"b","client_secret":"t"}}""");
        Assert.Equal("b", GoogleClient.Find(Path.Combine(temp.Path, "khong-co"), temp.Path)!.ClientId);
    }

    [Theory]
    [InlineData("GET /?state=st&code=4%2F0Ab-c&scope=email HTTP/1.1", "4/0Ab-c", "st", null)]
    [InlineData("GET /?error=access_denied&state=st HTTP/1.1", null, "st", "access_denied")]
    public void Parses_the_browser_callback(string line, string? code, string? state, string? error) =>
        Assert.Equal((code, state, error), GoogleAuth.ParseCallback(line));

    [Theory]
    [InlineData("GET /favicon.ico HTTP/1.1")]
    [InlineData("GET / HTTP/1.1")]
    [InlineData("")]
    public void Ignores_other_requests(string line) => Assert.Null(GoogleAuth.ParseCallback(line));

    [Fact]
    public void Reads_email_from_id_token()
    {
        Assert.Equal("chi.huyen@gmail.com", GoogleAuth.EmailOf(IdToken("chi.huyen@gmail.com")));
        Assert.Null(GoogleAuth.EmailOf("abc"));
        Assert.Null(GoogleAuth.EmailOf(null));
    }

    [Fact]
    public void Auth_url_asks_for_drive_file_with_pkce_and_offline_access()
    {
        var auth = new GoogleAuth(Client, new MemoryTokenStore(), new HttpClient());
        var url = auth.BuildAuthUrl("http://127.0.0.1:5000/", "CH", "ST");

        Assert.StartsWith(GoogleAuth.DefaultAuthUrl + "?", url);
        foreach (var part in new[] { "client_id=id-123.apps.googleusercontent.com", "redirect_uri=http%3A%2F%2F127.0.0.1%3A5000%2F",
                     "code_challenge=CH", "code_challenge_method=S256", "state=ST", "access_type=offline", "response_type=code",
                     "scope=openid%20email%20https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive.file" })
        {
            Assert.Contains(part, url);
        }
    }

    [Fact]
    public async Task Sign_in_through_the_loopback_port_saves_the_refresh_token()
    {
        var google = new FakeGoogle((_, body) => body.Contains("grant_type=authorization_code")
            ? (HttpStatusCode.OK, $$"""{"access_token":"AT1","expires_in":3599,"refresh_token":"RT1","id_token":"{{IdToken("chi.huyen@gmail.com")}}","scope":"https://www.googleapis.com/auth/drive.file openid https://www.googleapis.com/auth/userinfo.email"}""")
            : (HttpStatusCode.BadRequest, """{"error":"unexpected"}"""));
        var store = new MemoryTokenStore();
        var auth = new GoogleAuth(Client, store, new HttpClient(google));
        Task<string>? browser = null;

        var email = await auth.SignInAsync(url => browser = BrowserAsync(url, "CODE-1"), TimeSpan.FromSeconds(30));
        Assert.Contains("Đã đăng nhập Google (chi.huyen@gmail.com)", await browser!); // trang trả sau khi đã đổi mã

        Assert.Equal("chi.huyen@gmail.com", email);
        Assert.Equal(("chi.huyen@gmail.com", "RT1"), store.Saved);
        Assert.Equal("chi.huyen@gmail.com", auth.Email);
        var exchange = Assert.Single(google.Requests);
        Assert.Contains("code=CODE-1", exchange.Body);
        Assert.Contains("code_verifier=", exchange.Body);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A", exchange.Body);
        Assert.Equal("AT1", await auth.GetAccessTokenAsync()); // vừa lấy, chưa cần làm mới
        Assert.Single(google.Requests);
    }

    /// <summary>Trang đồng ý của Google cho bỏ tick ô Google Drive: đăng nhập "thành công" nhưng không có quyền Drive.</summary>
    [Fact]
    public async Task Sign_in_without_the_drive_box_ticked_is_refused_and_revoked()
    {
        var google = new FakeGoogle((r, _) => r.RequestUri!.ToString() == GoogleAuth.DefaultRevokeUrl
            ? (HttpStatusCode.OK, "{}")
            : (HttpStatusCode.OK, $$"""{"access_token":"AT1","expires_in":3599,"refresh_token":"RT1","id_token":"{{IdToken("a@b.c")}}","scope":"openid https://www.googleapis.com/auth/userinfo.email"}"""));
        var store = new MemoryTokenStore();
        var auth = new GoogleAuth(Client, store, new HttpClient(google));
        Task<string>? browser = null;

        var ex = await Assert.ThrowsAsync<GoogleAuthException>(() => auth.SignInAsync(url => browser = BrowserAsync(url, "CODE-2"), TimeSpan.FromSeconds(30)));
        var page = await browser!;
        Assert.Contains("Chưa đăng nhập xong", page); // trình duyệt không còn báo "Đã đăng nhập" sai
        Assert.Contains("Google Drive", page);

        Assert.Equal(GoogleAuth.MissingDriveScope, ex.Message);
        Assert.Contains("tick", ex.Message);
        Assert.Null(store.Saved);
        Assert.Equal("token=RT1", google.Requests[^1].Body); // thu hồi phiên thiếu quyền
    }

    [Fact]
    public async Task Sign_in_reports_a_cancelled_consent()
    {
        var auth = new GoogleAuth(Client, new MemoryTokenStore(), new HttpClient(new FakeGoogle((_, _) => (HttpStatusCode.OK, "{}"))));
        Task<string>? browser = null;

        var ex = await Assert.ThrowsAsync<GoogleAuthException>(() => auth.SignInAsync(url => browser = BrowserAsync(url, null), TimeSpan.FromSeconds(30)));
        await browser!;
        Assert.Equal("Đã huỷ đăng nhập Google.", ex.Message);
    }

    /// <summary>Trình duyệt giả: mở 1 kết nối "dự phòng" không gửi gì (như Chrome), rồi gọi về redirect_uri với code / lỗi.</summary>
    private static async Task<string> BrowserAsync(string url, string? code)
    {
        var query = new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var redirect = new Uri(query["redirect_uri"]);
        using var idle = new TcpClient();
        await idle.ConnectAsync(IPAddress.Loopback, redirect.Port);
        using var http = new HttpClient();
        var target = code is null ? $"?error=access_denied&state={query["state"]}" : $"?code={code}&state={query["state"]}";
        var page = await http.GetStringAsync(redirect + target);
        Assert.Contains("MeRon", page);
        return page;
    }

    [Fact]
    public async Task Access_token_is_refreshed_and_a_revoked_session_signs_out()
    {
        var revoked = false;
        var google = new FakeGoogle((_, body) => revoked
            ? (HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""")
            : (HttpStatusCode.OK, """{"access_token":"AT2","expires_in":3599}"""));
        var store = new MemoryTokenStore();
        var auth = new GoogleAuth(Client, store, new HttpClient(google));

        Assert.Equal("Chưa đăng nhập Google.", (await Assert.ThrowsAsync<GoogleAuthException>(() => auth.GetAccessTokenAsync())).Message);

        store.Save("a@b.c", "RT");
        Assert.Equal("AT2", await auth.GetAccessTokenAsync());
        Assert.Contains("grant_type=refresh_token", google.Requests[0].Body);
        Assert.Contains("refresh_token=RT", google.Requests[0].Body);

        revoked = true;
        var fresh = new GoogleAuth(Client, store, new HttpClient(google));
        var ex = await Assert.ThrowsAsync<GoogleAuthException>(() => fresh.GetAccessTokenAsync());
        Assert.Contains("Đăng nhập lại", ex.Message);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task Sign_out_revokes_and_forgets_the_token()
    {
        var google = new FakeGoogle((_, _) => (HttpStatusCode.OK, "{}"));
        var store = new MemoryTokenStore { Saved = ("a@b.c", "RT9") };
        await new GoogleAuth(Client, store, new HttpClient(google)).SignOutAsync();

        Assert.Null(store.Saved);
        var revoke = Assert.Single(google.Requests);
        Assert.Equal(GoogleAuth.DefaultRevokeUrl, revoke.Url);
        Assert.Equal("token=RT9", revoke.Body);
    }
}

public class DriveUploaderTests
{
    private static string Backup(TempFolder temp)
    {
        var folder = Path.Combine(temp.Path, "20261014_093000");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "users_20261014_093000.tsv"), "id\tname\n1\tChị An");
        File.WriteAllText(Path.Combine(folder, "orders_20261014_093000.tsv"), "id\tuserId\n");
        return folder;
    }

    private static string Name(string body) => JsonDocument.Parse(body[body.IndexOf('{')..(body.IndexOf('}') + 1)]).RootElement.GetProperty("name").GetString()!;

    [Fact]
    public async Task Uploads_into_own_folder_creating_it_once()
    {
        using var temp = new TempFolder();
        var created = 0;
        var google = new FakeGoogle((request, body) =>
        {
            var url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Get)
            {
                return (HttpStatusCode.OK, """{"files":[]}""");
            }
            if (url.Contains("/upload/"))
            {
                return (HttpStatusCode.OK, """{"id":"F"}""");
            }
            created++;
            return (HttpStatusCode.OK, created == 1 ? """{"id":"OWN"}""" : """{"id":"SUB","webViewLink":"https://drive.google.com/drive/folders/SUB"}""");
        });
        var tokens = 0;
        var uploader = new DriveUploader(new HttpClient(google), _ => Task.FromResult($"AT{++tokens}"));
        var steps = new List<string>();

        var result = await uploader.UploadBackupAsync(Backup(temp), null, new Progress<string>(steps.Add));

        Assert.Equal(new DriveUploadResult("SUB", "20261014_093000", "https://drive.google.com/drive/folders/SUB", 2), result);
        Assert.Equal(5, google.Requests.Count); // tìm folder riêng, tạo folder riêng, tạo folder con, 2 file
        Assert.Contains("name = 'MeRon backup'", Uri.UnescapeDataString(google.Requests[0].Url));
        Assert.Equal("MeRon backup", Name(google.Requests[1].Body));
        Assert.Contains("\"root\"", google.Requests[1].Body);
        Assert.Equal("20261014_093000", Name(google.Requests[2].Body));
        Assert.Contains("\"OWN\"", google.Requests[2].Body);
        Assert.Equal(["orders_20261014_093000.tsv", "users_20261014_093000.tsv"], google.Requests.Skip(3).Select(r => Name(r.Body)));
        Assert.All(google.Requests.Skip(3), r =>
        {
            Assert.Contains("uploadType=multipart", r.Url);
            Assert.Contains("\"SUB\"", r.Body);
        });
        Assert.Contains("Chị An", google.Requests[4].Body);
        Assert.All(google.Requests, r => Assert.StartsWith("Bearer AT", r.Auth));
    }

    [Fact]
    public async Task Uploads_into_a_chosen_folder_or_explains_why_it_cannot()
    {
        using var temp = new TempFolder();
        var folder = Backup(temp);
        var ok = new FakeGoogle((_, _) => (HttpStatusCode.OK, """{"id":"X"}"""));
        await new DriveUploader(new HttpClient(ok), _ => Task.FromResult("AT")).UploadBackupAsync(folder, "PHONE");
        Assert.Contains("\"PHONE\"", ok.Requests[0].Body); // không tìm / tạo folder riêng
        Assert.Equal(3, ok.Requests.Count);

        var denied = new FakeGoogle((_, _) => (HttpStatusCode.NotFound, """{"error":{"code":404,"message":"File not found: PHONE."}}"""));
        var ex = await Assert.ThrowsAsync<GoogleAuthException>(() =>
            new DriveUploader(new HttpClient(denied), _ => Task.FromResult("AT")).UploadBackupAsync(folder, "PHONE"));
        Assert.Contains("MeRon backup", ex.Message);

        var quota = new FakeGoogle((r, _) => r.RequestUri!.ToString().Contains("/upload/")
            ? (HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"The user's Drive storage quota has been exceeded."}}""")
            : (HttpStatusCode.OK, """{"id":"X"}"""));
        ex = await Assert.ThrowsAsync<GoogleAuthException>(() =>
            new DriveUploader(new HttpClient(quota), _ => Task.FromResult("AT")).UploadBackupAsync(folder, "PHONE"));
        Assert.Equal("Google Drive báo lỗi (HTTP 403): The user's Drive storage quota has been exceeded.", ex.Message);
        Assert.False(ex.NeedsSignIn);

        // Phiên cũ đăng nhập mà chưa tick quyền Drive → báo cách sửa + cần đăng nhập lại
        var scopes = new FakeGoogle((_, _) => (HttpStatusCode.Forbidden,
            """{"error":{"code":403,"message":"Request had insufficient authentication scopes.","status":"PERMISSION_DENIED"}}"""));
        ex = await Assert.ThrowsAsync<GoogleAuthException>(() =>
            new DriveUploader(new HttpClient(scopes), _ => Task.FromResult("AT")).UploadBackupAsync(folder, null));
        Assert.Equal(GoogleAuth.MissingDriveScope, ex.Message);
        Assert.True(ex.NeedsSignIn);

        Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        await Assert.ThrowsAsync<GoogleAuthException>(() => new DriveUploader(new HttpClient(ok), _ => Task.FromResult("AT")).UploadBackupAsync(folder, null));
    }
}
