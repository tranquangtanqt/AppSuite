using System.Globalization;
using System.Text.RegularExpressions;

namespace ScreenCapture.Models;

/// <summary>
/// Tên tab gọn để thanh tab chứa được nhiều ảnh hơn (2026-10-16): tên ảnh chụp "yyyy-MM-dd HH mm ss" (+ hậu tố " (2)")
/// → chụp hôm nay chỉ ghi giờ "10:01:46", ngày khác "15/10 16:22", năm khác "15/10/25 16:22". Tên khác (tên file, "Ảnh
/// mới (5)") giữ nguyên. Tên đầy đủ hiện ở tooltip của tab.
/// </summary>
public static partial class TabTitle
{
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}) (\d{2}) (\d{2}) (\d{2})(.*)$")]
    private static partial Regex CaptureTitle();

    public static string Short(string title, DateTime now)
    {
        var match = CaptureTitle().Match(title);
        if (!match.Success || !DateTime.TryParseExact($"{match.Groups[1].Value} {match.Groups[2].Value}:{match.Groups[3].Value}:{match.Groups[4].Value}",
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return title;
        }
        var rest = match.Groups[5].Value;
        if (time.Date == now.Date)
        {
            return time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + rest;
        }
        return time.ToString(time.Year == now.Year ? "dd/MM HH:mm" : "dd/MM/yy HH:mm", CultureInfo.InvariantCulture) + rest;
    }
}
