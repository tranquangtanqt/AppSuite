using System.Globalization;
using System.Text;

namespace MeRon.Core;

/// <summary>Tìm theo tên không phân biệt hoa thường và dấu tiếng Việt: "banh bao" khớp "Bánh bao", "đo uong" khớp "Đồ uống".</summary>
public static class TextSearch
{
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            builder.Append(ch switch { 'đ' => 'd', 'Đ' => 'd', _ => char.ToLowerInvariant(ch) });
        }
        return builder.ToString();
    }

    /// <summary>Ô tìm trống → khớp tất cả.</summary>
    public static bool Matches(string? text, string? query) =>
        string.IsNullOrWhiteSpace(query) || Fold(text).Contains(Fold(query.Trim()), StringComparison.Ordinal);

    /// <summary>Mọi từ trong <paramref name="query"/> đều có trong <paramref name="text"/>, không cần liền nhau / đúng thứ tự
    /// ("bao xiu" khớp "Bánh bao xá xíu/trứng sữa"). Ô tìm trống → khớp tất cả.</summary>
    public static bool MatchesAllWords(string? text, string? query)
    {
        var folded = Fold(text);
        return Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => folded.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>Lọc theo <see cref="MatchesAllWords"/>; tên bắt đầu bằng chữ gõ lên trước, còn lại giữ thứ tự gốc.</summary>
    public static List<T> Filter<T>(IEnumerable<T> items, Func<T, string?> name, string? query, int max)
    {
        var prefix = Fold(query).Trim();
        return items.Where(item => MatchesAllWords(name(item), query))
            .OrderBy(item => prefix.Length > 0 && Fold(name(item)).StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1)
            .Take(max)
            .ToList();
    }
}
