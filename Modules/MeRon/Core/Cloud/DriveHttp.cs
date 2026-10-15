using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MeRon.Core.Cloud;

/// <summary>Gọi Google Drive API kèm access token + đổi lỗi của Google thành câu dễ hiểu (dùng chung cho tải lên / tải về).</summary>
internal static class DriveHttp
{
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, Func<CancellationToken, Task<string>> accessToken,
        HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken(cancellationToken));
        try
        {
            return await http.SendAsync(request, completion, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GoogleAuthException("Không kết nối được Google Drive - kiểm tra mạng.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GoogleAuthException("Google Drive không trả lời (hết thời gian chờ). Kiểm tra mạng rồi thử lại.", ex);
        }
    }

    /// <summary>Đọc JSON trả về; HTTP lỗi → <see cref="GoogleAuthException"/> (thiếu quyền Drive → cần đăng nhập lại).</summary>
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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
        EnsureSuccess(response, json);
        return json;
    }

    public static void EnsureSuccess(HttpResponseMessage response, JsonElement json = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var message = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                      && error.TryGetProperty("message", out var m) ? m.GetString() : null;
        if (response.StatusCode == HttpStatusCode.Forbidden && message?.Contains("insufficient authentication scopes", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new GoogleAuthException(GoogleAuth.MissingDriveScope) { NeedsSignIn = true };
        }
        throw new GoogleAuthException($"Google Drive báo lỗi (HTTP {(int)response.StatusCode}){(message is null ? "." : ": " + message)}");
    }
}
