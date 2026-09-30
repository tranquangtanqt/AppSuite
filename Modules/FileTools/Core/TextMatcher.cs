using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FileTools.Core;

public sealed record MatchOptions
{
    /// <summary>Các từ khoá (bỏ dòng trống); regex thì chỉ dùng mục đầu tiên.</summary>
    public required IReadOnlyList<string> Terms { get; init; }
    public bool IsRegex { get; init; }
    public bool MatchCase { get; init; }
    /// <summary>Không phân biệt dấu tiếng Việt ("thanh toan" khớp "Thanh toán"; đ = d). Không áp dụng cho regex.</summary>
    public bool IgnoreDiacritics { get; init; }
    /// <summary>Nhiều từ khoá: true = dòng phải chứa tất cả, false = chỉ cần 1.</summary>
    public bool MatchAll { get; init; }
}

/// <summary>So khớp 1 dòng với từ khoá / regex - dùng chung cho Tìm và Lọc dòng.</summary>
public sealed class TextMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private readonly Regex? _regex;
    private readonly string[] _terms;
    private readonly StringComparison _comparison;
    private readonly bool _foldDiacritics;
    private readonly bool _matchAll;

    public TextMatcher(MatchOptions options)
    {
        var terms = options.Terms.Where(t => t.Length > 0).ToArray();
        if (terms.Length == 0)
        {
            throw new ArgumentException("Chưa nhập từ khoá cần tìm.");
        }
        _matchAll = options.MatchAll;
        if (options.IsRegex)
        {
            var flags = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (!options.MatchCase)
            {
                flags |= RegexOptions.IgnoreCase;
            }
            try
            {
                _regex = new Regex(terms[0], flags, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("Regex không hợp lệ: " + ex.Message, ex);
            }
            _terms = [];
            return;
        }
        _foldDiacritics = options.IgnoreDiacritics;
        _comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        _terms = _foldDiacritics ? terms.Select(FoldDiacritics).ToArray() : terms;
    }

    /// <summary>Vị trí khớp đầu tiên trong dòng (-1 = không khớp). Với nhiều từ khoá "tất cả" là vị trí của từ đầu.</summary>
    public int Find(string line)
    {
        if (_regex is not null)
        {
            var m = _regex.Match(line);
            return m.Success ? m.Index : -1;
        }
        var text = _foldDiacritics ? FoldDiacritics(line) : line;
        int first = -1;
        foreach (var term in _terms)
        {
            int at = text.IndexOf(term, _comparison);
            if (at < 0)
            {
                if (_matchAll)
                {
                    return -1;
                }
                continue;
            }
            if (!_matchAll)
            {
                return at;
            }
            first = first < 0 ? at : Math.Min(first, at);
        }
        return first;
    }

    public bool IsMatch(string line) => Find(line) >= 0;

    /// <summary>Bỏ dấu, giữ nguyên độ dài chuỗi (mỗi ký tự → đúng 1 ký tự) để vị trí khớp vẫn đúng với dòng gốc.</summary>
    public static string FoldDiacritics(string s)
    {
        bool plain = true;
        foreach (char c in s)
        {
            if (c >= 0x80)
            {
                plain = false;
                break;
            }
        }
        if (plain)
        {
            return s;
        }
        var table = FoldTable.Value;
        return string.Create(s.Length, s, (dest, src) =>
        {
            for (int i = 0; i < src.Length; i++)
            {
                dest[i] = table[src[i]];
            }
        });
    }

    /// <summary>Bảng tra 65.536 ký tự → ký tự gốc bỏ dấu, tính 1 lần (Normalize từng ký tự của file vài GB quá chậm).</summary>
    private static readonly Lazy<char[]> FoldTable = new(() =>
    {
        var table = new char[char.MaxValue + 1];
        for (int i = 0; i <= char.MaxValue; i++)
        {
            char c = (char)i;
            table[i] = c;
            if (c < 0x80 || char.IsSurrogate(c))
            {
                continue;
            }
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.OtherNotAssigned)
            {
                continue;
            }
            string decomposed;
            try
            {
                decomposed = c.ToString().Normalize(NormalizationForm.FormD);
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (decomposed.Length > 1 && CharUnicodeInfo.GetUnicodeCategory(decomposed[0]) != UnicodeCategory.NonSpacingMark)
            {
                table[i] = decomposed[0];
            }
        }
        table['đ'] = 'd';
        table['Đ'] = 'D';
        return table;
    });
}
