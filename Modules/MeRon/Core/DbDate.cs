using System.Globalization;

namespace MeRon.Core;

/// <summary>
/// Ngày giờ trong database là chuỗi. Điện thoại ghi lẫn 2 dạng: "2026-06-27T14:20:53.242992" (toIso8601String) và
/// "2026-06-27 07:20:53" (CURRENT_TIMESTAMP). Đọc được cả hai; ghi theo dạng toIso8601String.
/// </summary>
public static class DbDate
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
    ];

    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var text = value.Trim();
        if (text.EndsWith('Z'))
        {
            text = text[..^1];
        }
        return DateTime.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : null;
    }

    public static string Format(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture);

    /// <summary>"yyyy-MM-dd" để so với substr(cột, 1, 10) - đúng với cả 2 dạng chuỗi.</summary>
    public static string Day(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Hiển thị dd/MM/yyyy như điện thoại.</summary>
    public static string Show(DateTime? value) => value?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
}
