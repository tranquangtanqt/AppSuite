using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MeRon.Core.Cloud;

/// <summary>OAuth client loại "Desktop app" tạo trong Google Cloud (project me-ron) - file client_secret_….json tải về.</summary>
public sealed record GoogleClient(string ClientId, string ClientSecret)
{
    /// <summary>Đọc file client_secret_….json (khối "installed"). Sai định dạng → <see cref="GoogleAuthException"/>.</summary>
    public static GoogleClient Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var installed = doc.RootElement.GetProperty("installed");
            return new GoogleClient(installed.GetProperty("client_id").GetString()!, installed.GetProperty("client_secret").GetString()!);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new GoogleAuthException("File cấu hình Google không đúng (cần OAuth client loại Desktop app).", ex);
        }
    }

    /// <summary>File client_secret*.json đầu tiên trong các thư mục (theo thứ tự); không có → null.</summary>
    public static GoogleClient? Find(params string[] folders)
    {
        foreach (var folder in folders.Where(Directory.Exists))
        {
            if (Directory.EnumerateFiles(folder, "client_secret*.json").FirstOrDefault() is { } file)
            {
                return Parse(File.ReadAllText(file));
            }
        }
        return null;
    }
}

/// <summary>Lỗi đăng nhập / gọi Google - <see cref="Exception.Message"/> là câu tiếng Việt hiện thẳng cho người dùng.</summary>
public sealed class GoogleAuthException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Phải đăng nhập lại (vd thiếu quyền Drive) - app đăng xuất để hiện lại nút Đăng nhập.</summary>
    public bool NeedsSignIn { get; init; }
}

/// <summary>Nơi cất refresh token (bản máy: Windows Credential Manager; test: bộ nhớ).</summary>
public interface ITokenStore
{
    (string Email, string RefreshToken)? Load();

    void Save(string email, string refreshToken);

    void Clear();
}

/// <summary>
/// Đăng nhập Google cho app desktop (OAuth "installed app" + PKCE, nhận mã qua loopback http://127.0.0.1:cổng): mở trình
/// duyệt, người dùng chọn tài khoản + đồng ý, app đổi mã lấy refresh token và cất vào <see cref="ITokenStore"/> - lần sau
/// không phải đăng nhập lại. Quyền xin: <c>drive.file</c> (chỉ file do app tạo / mở) + email để hiện "Đã đăng nhập: …".
/// </summary>
public sealed class GoogleAuth(GoogleClient client, ITokenStore store, HttpClient http, string authUrl = GoogleAuth.DefaultAuthUrl,
    string tokenUrl = GoogleAuth.DefaultTokenUrl, string revokeUrl = GoogleAuth.DefaultRevokeUrl)
{
    public const string DefaultAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string DefaultTokenUrl = "https://oauth2.googleapis.com/token";
    public const string DefaultRevokeUrl = "https://oauth2.googleapis.com/revoke";
    public const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";
    public const string Scopes = "openid email " + DriveFileScope;

    /// <summary>Trang đồng ý của Google cho tick từng quyền: bỏ tick ô Google Drive thì vẫn đăng nhập được nhưng không tải lên được.</summary>
    public const string MissingDriveScope =
        "Chưa cấp quyền Google Drive cho MeRon. Bấm Đăng nhập Google lại; ở trang Google nhớ tick ô \"Xem, chỉnh sửa, tạo và xoá chỉ những " +
        "tệp cụ thể trên Google Drive mà bạn dùng với ứng dụng này\" (See, edit, create, and delete only the specific Google Drive files…) rồi bấm Tiếp tục.";

    private string? _accessToken;
    private DateTime _accessExpires;

    /// <summary>Email tài khoản đã đăng nhập; null = chưa đăng nhập.</summary>
    public string? Email => store.Load()?.Email;

    /// <summary>
    /// Đăng nhập: mở trình duyệt (<paramref name="openBrowser"/>) tới trang Google, chờ Google chuyển về cổng loopback
    /// (tối đa <paramref name="timeout"/>), đổi mã lấy token. Trả về email.
    /// </summary>
    public async Task<string> SignInAsync(Action<string> openBrowser, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            openBrowser(BuildAuthUrl(redirect, challenge, state));

            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timer.CancelAfter(timeout);
            try
            {
                // Đổi mã + kiểm tra quyền xong mới trả trang cho trình duyệt: trang báo đúng kết quả cuối cùng.
                return await WaitForCodeAsync(listener, state, code => CompleteAsync(code, redirect, verifier, cancellationToken), timer.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new GoogleAuthException("Hết thời gian chờ đăng nhập Google trên trình duyệt - bấm Đăng nhập lại.");
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Access token còn hạn (tự làm mới bằng refresh token). Chưa đăng nhập / phiên bị thu hồi → lỗi rõ ràng.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_accessToken is not null && DateTime.UtcNow < _accessExpires)
        {
            return _accessToken;
        }
        var saved = store.Load() ?? throw new GoogleAuthException("Chưa đăng nhập Google.");
        try
        {
            Remember(await PostTokenAsync(new Dictionary<string, string>
            {
                ["refresh_token"] = saved.RefreshToken,
                ["client_id"] = client.ClientId,
                ["client_secret"] = client.ClientSecret,
                ["grant_type"] = "refresh_token",
            }, cancellationToken));
        }
        catch (GoogleAuthException ex) when (ex.Message.Contains("invalid_grant", StringComparison.Ordinal))
        {
            store.Clear(); // bị thu hồi / đổi mật khẩu → phải đăng nhập lại
            throw new GoogleAuthException("Phiên đăng nhập Google đã hết hoặc bị thu hồi - bấm Đăng nhập lại.", ex);
        }
        return _accessToken!;
    }

    /// <summary>Đăng xuất: thu hồi quyền trên Google (lỗi mạng thì bỏ qua) và xoá token trên máy.</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var saved = store.Load();
        store.Clear();
        _accessToken = null;
        if (saved is null)
        {
            return;
        }
        await RevokeAsync(saved.Value.RefreshToken, cancellationToken);
    }

    private async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
            using var _ = await http.PostAsync(revokeUrl, content, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // không mạng: token trên máy đã xoá; quyền trên Google tự hết khi không dùng
        }
    }

    internal string BuildAuthUrl(string redirect, string challenge, string state) =>
        authUrl + "?" + string.Join("&", new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = redirect,
            ["scope"] = Scopes,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent", // luôn có refresh token, kể cả khi đã đồng ý trước đó
        }.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));

    /// <summary>Đọc dòng đầu request trình duyệt gửi về cổng loopback ("GET /?code=…&amp;state=… HTTP/1.1").</summary>
    /// <returns>null nếu không phải request trả mã (vd /favicon.ico).</returns>
    public static (string? Code, string? State, string? Error)? ParseCallback(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2 || !parts[1].StartsWith('/'))
        {
            return null;
        }
        var query = parts[1].IndexOf('?') is var i and >= 0 ? parts[1][(i + 1)..] : string.Empty;
        var values = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty);
        if (!values.ContainsKey("code") && !values.ContainsKey("error"))
        {
            return null;
        }
        return (values.GetValueOrDefault("code"), values.GetValueOrDefault("state"), values.GetValueOrDefault("error"));
    }

    /// <summary>Email trong id_token (JWT) - chỉ để hiển thị, không cần kiểm chữ ký (nhận thẳng từ Google qua HTTPS).</summary>
    public static string? EmailOf(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Các quyền Google cấp ở lần đăng nhập gần nhất (ghi log khi thiếu quyền Drive).</summary>
    public string? LastGrantedScopes { get; private set; }

    /// <summary>Đổi mã lấy token, kiểm tra đã cấp quyền Drive, cất refresh token. Trả về email.</summary>
    private async Task<string> CompleteAsync(string code, string redirect, string verifier, CancellationToken cancellationToken)
    {
        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret,
            ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        }, cancellationToken);
        var refresh = Text(token, "refresh_token")
            ?? throw new GoogleAuthException("Google không trả về quyền dùng lâu dài (refresh token) - thử đăng nhập lại.");
        LastGrantedScopes = Text(token, "scope") ?? string.Empty;
        if (!LastGrantedScopes.Split(' ').Contains(DriveFileScope))
        {
            await RevokeAsync(refresh, cancellationToken); // không giữ phiên thiếu quyền
            throw new GoogleAuthException(MissingDriveScope);
        }
        var email = EmailOf(Text(token, "id_token")) ?? "(không rõ email)";
        store.Save(email, refresh);
        Remember(token);
        return email;
    }

    /// <summary>
    /// Chờ trình duyệt gọi về cổng loopback; có mã thì chạy <paramref name="complete"/> (đổi mã lấy token) rồi mới trả trang
    /// cho trình duyệt - thành công hay lỗi (vd chưa tick ô Google Drive) đều hiện đúng trên trình duyệt.
    /// </summary>
    private static async Task<string> WaitForCodeAsync(TcpListener listener, string state, Func<string, Task<string>> complete,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var socket = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            // Trình duyệt hay mở sẵn kết nối "dự phòng" không gửi gì: chờ mỗi kết nối tối đa 3 giây rồi bỏ qua.
            using var lineTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lineTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            string line;
            try
            {
                line = await reader.ReadLineAsync(lineTimeout.Token) ?? string.Empty;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
            var callback = ParseCallback(line);
            string? email = null;
            GoogleAuthException? failure = null;
            string? page;
            switch (callback)
            {
                case null:
                    page = null;
                    break;
                case { Error: { } error }:
                    failure = new GoogleAuthException(error == "access_denied" ? "Đã huỷ đăng nhập Google." : $"Google báo lỗi đăng nhập: {error}.");
                    page = $"Đăng nhập không thành công ({error}). Đóng trang này và thử lại trong MeRon.";
                    break;
                case { State: var s } when s != state:
                    page = "Phiên đăng nhập không khớp. Đóng trang này và thử lại trong MeRon.";
                    break;
                default:
                    try
                    {
                        email = await complete(callback!.Value.Code!);
                        page = $"Đã đăng nhập Google ({email}). Có thể đóng trang này và quay lại MeRon.";
                    }
                    catch (GoogleAuthException ex)
                    {
                        failure = ex;
                        page = "Chưa đăng nhập xong: " + ex.Message;
                    }
                    catch (HttpRequestException ex)
                    {
                        failure = new GoogleAuthException("Không kết nối được Google - kiểm tra mạng rồi đăng nhập lại.", ex);
                        page = "Chưa đăng nhập xong: " + failure.Message;
                    }
                    break;
            }
            await RespondAsync(stream, page, failure is not null, cancellationToken);
            if (failure is not null)
            {
                throw failure;
            }
            if (email is not null)
            {
                return email;
            }
        }
    }

    private static async Task RespondAsync(Stream stream, string? page, bool failed, CancellationToken cancellationToken)
    {
        var body = page is null ? string.Empty
            : "<!doctype html><meta charset=\"utf-8\"><title>MeRon</title><body style=\"font-family:Segoe UI,sans-serif;padding:40px;max-width:760px\">" +
              $"<h2 style=\"color:{(failed ? "#c42b1c" : "#0f7b0f")}\">{page.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")}</h2></body>";
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {(page is null ? "404 Not Found" : "200 OK")}\r\nContent-Type: text/html; charset=utf-8\r\n" +
                                           $"Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private async Task<JsonElement> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(tokenUrl, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new GoogleAuthException($"Google trả lời không đúng định dạng (HTTP {(int)response.StatusCode}).");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleAuthException($"Google từ chối: {Text(root, "error") ?? response.StatusCode.ToString()} {Text(root, "error_description")}".Trim());
        }
        return root;
    }

    private void Remember(JsonElement token)
    {
        _accessToken = Text(token, "access_token") ?? throw new GoogleAuthException("Google không trả về access token.");
        var seconds = token.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 3600;
        _accessExpires = DateTime.UtcNow.AddSeconds(seconds - 60);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
