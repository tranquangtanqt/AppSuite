using MeRon.Core.Cloud;
using Windows.Security.Credentials;

namespace MeRon.Services;

/// <summary>
/// Refresh token Google cất trong Windows Credential Manager (Thông tin xác thực Windows, mã hoá theo tài khoản Windows) -
/// không nằm trong file hay database của app, chép database sang máy khác cũng không mang theo.
/// </summary>
public sealed class VaultTokenStore : ITokenStore
{
    private const string Resource = "MeRon - Google Drive";

    public (string Email, string RefreshToken)? Load()
    {
        try
        {
            var credential = new PasswordVault().FindAllByResource(Resource).FirstOrDefault();
            if (credential is null)
            {
                return null;
            }
            credential.RetrievePassword();
            return (credential.UserName, credential.Password);
        }
        catch (Exception) // chưa có mục nào: PasswordVault ném lỗi "Element not found"
        {
            return null;
        }
    }

    public void Save(string email, string refreshToken)
    {
        Clear();
        new PasswordVault().Add(new PasswordCredential(Resource, email, refreshToken));
    }

    public void Clear()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var credential in vault.FindAllByResource(Resource))
            {
                vault.Remove(credential);
            }
        }
        catch (Exception)
        {
            // không có gì để xoá
        }
    }
}

/// <summary>
/// Đăng nhập Google + upload Drive dùng chung. File OAuth client (client_secret_….json, loại Desktop app, không commit vào
/// git) tìm ở Assets\ cạnh MeRon.exe rồi %LOCALAPPDATA%\AppSuite\MeRon; không có → <see cref="Auth"/> null, trang Xuất báo
/// "chưa cấu hình".
/// </summary>
public static class GoogleServices
{
    /// <summary>Upload 8 file có thể lâu khi mạng chậm.</summary>
    public static HttpClient Http { get; } = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static string ConfigFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppSuite", "MeRon");

    private static readonly Lazy<GoogleAuth?> LazyAuth = new(() =>
        GoogleClient.Find(Path.Combine(AppContext.BaseDirectory, "Assets"), ConfigFolder) is { } client
            ? new GoogleAuth(client, new VaultTokenStore(), Http)
            : null);

    public static GoogleAuth? Auth => LazyAuth.Value;
}
