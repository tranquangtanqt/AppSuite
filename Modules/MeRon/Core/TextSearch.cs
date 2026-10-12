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
}
