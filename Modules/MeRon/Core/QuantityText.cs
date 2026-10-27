using System.Text.RegularExpressions;

namespace MeRon.Core;

/// <summary>
/// Ô chọn món gõ kèm số lượng: "2 chao", "2x chao", "chao 2", "chao x2" → ("chao", 2). Số 1..999; không có số
/// ở đầu / cuối (hoặc chỉ có số) → (nguyên chữ, null).
/// </summary>
public static partial class QuantityText
{
    public const int Max = 999;

    [GeneratedRegex(@"^\s*(\d{1,3})\s*[x×*]?\s+(\S.*?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Leading();

    [GeneratedRegex(@"^\s*(\S.*?)\s+[x×*]?\s*(\d{1,3})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Trailing();

    public static (string Name, int? Quantity) Split(string? text)
    {
        text ??= string.Empty;
        foreach (var (match, quantity, name) in new[] { (Leading().Match(text), 1, 2), (Trailing().Match(text), 2, 1) })
        {
            if (match.Success && int.TryParse(match.Groups[quantity].Value, out var q) && q is >= 1 and <= Max
                && !match.Groups[name].Value.All(char.IsDigit))
            {
                return (match.Groups[name].Value, q);
            }
        }
        return (text, null);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();

    /// <summary>Tên món có đúng số này là 1 số riêng ("Combo 2" có 2; "Yến chưng 40k" không có 4) - gõ "combo 2" là tên món,
    /// không phải số lượng.</summary>
    public static bool HasNumber(string? name, int number) =>
        Number().Matches(name ?? string.Empty).Any(m => int.TryParse(m.Value, out var n) && n == number);
}
