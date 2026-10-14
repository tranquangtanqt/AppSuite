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
