using System.Globalization;
using System.Text.RegularExpressions;

namespace MeRon.Core;

/// <summary>
/// Ngày gõ tay trong ô ngày (dd/MM/yyyy như mọi chỗ trong app). Nhận: "14/10/2026", "14/10/26", "14/10" (năm nay), "14"
/// (tháng này), dấu cách "/", "-", "." hoặc khoảng trắng; hoặc chỉ số: "14102026", "141026", "1410". Sai → null.
/// </summary>
public static partial class DateInput
{
    public static string Format(DateOnly day) => day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Quanh giao thừa: ngày năm trước / năm sau cách hôm nay tối đa bấy nhiêu ngày thì lấy năm đó.</summary>
    public const int NearYearDays = 60;

    /// <param name="nearToday">true: ngày + tháng không ghi năm mà ngày đó của năm trước / năm sau cách hôm nay không quá
    /// <see cref="NearYearDays"/> ngày thì lấy năm đó - ngày giao, ngày của trang sổ: 02/01 gõ "30/12" là 30/12 năm trước,
    /// 30/12 gõ "02/01" là năm sau; ngoài khoảng đó vẫn năm nay như cũ. false: luôn năm nay (ô lọc "Từ ngày 1/1" của báo cáo
    /// gõ trong tháng 12 vẫn là đầu năm nay).</param>
    public static DateOnly? Parse(string? text, DateOnly today, bool nearToday)
    {
        var parsed = Parse(text, today);
        if (!nearToday || parsed is not { } day || !HasDayAndMonthOnly(text))
        {
            return parsed;
        }
        var near = new[] { day.Year - 1, day.Year + 1 }
            .Where(y => y is >= 1900 and <= 2999 && (day.Month != 2 || day.Day != 29 || DateTime.IsLeapYear(y)))
            .Select(y => new DateOnly(y, day.Month, day.Day))
            .Where(d => Math.Abs(d.DayNumber - today.DayNumber) <= NearYearDays)
            .ToList();
        return near.Count > 0 ? near[0] : day;
    }

    /// <summary>Chữ chỉ có ngày + tháng ("30/12", "3012").</summary>
    private static bool HasDayAndMonthOnly(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value.All(char.IsAsciiDigit) ? value.Length == 4 : Separators().Split(value).Length == 2;
    }

    public static DateOnly? Parse(string? text, DateOnly today)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return null;
        }
        string[] parts;
        if (value.All(char.IsAsciiDigit))
        {
            parts = value.Length switch
            {
                <= 2 => [value],
                4 => [value[..2], value[2..]],
                6 => [value[..2], value[2..4], value[4..]],
                8 => [value[..2], value[2..4], value[4..]],
                _ => [],
            };
        }
        else
        {
            parts = Separators().Split(value);
        }
        if (parts.Length is 0 or > 3 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit) || p.Length > 4))
        {
            return null;
        }

        var day = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var month = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : today.Month;
        var year = today.Year;
        if (parts.Length > 2)
        {
            year = int.Parse(parts[2], CultureInfo.InvariantCulture);
            if (parts[2].Length <= 2)
            {
                year += 2000;
            }
            else if (parts[2].Length != 4)
            {
                return null;
            }
        }
        if (year is < 1900 or > 2999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }
        return new DateOnly(year, month, day);
    }

    [GeneratedRegex(@"[\s/\-.]+")]
    private static partial Regex Separators();
}
