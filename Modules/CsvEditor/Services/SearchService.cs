using System.Text.RegularExpressions;
using CsvEditor.Models;

namespace CsvEditor.Services;

/// <summary>Finds matching cells within the rows currently shown (the caller passes the post-filter
/// view, so Find only ever highlights what the user can actually see).</summary>
public sealed class SearchService
{
    /// <summary>Hàm đổi nội dung 1 ô khớp: chỉ thay phần chữ khớp, như Excel (tìm "an" thay "em": "An Bình" → "em Bình").
    /// Regex: chữ thay dùng được $1, $2... cho nhóm. Khớp toàn bộ ô thì thay cả ô.</summary>
    public static Func<string, string> CreateReplacer(string query, SearchMode mode, bool caseSensitive, string replacement)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        switch (mode)
        {
            case SearchMode.Contains:
                return cell => cell.Replace(query, replacement, comparison);
            case SearchMode.StartsWith:
                return cell => replacement + cell[query.Length..];
            case SearchMode.EndsWith:
                return cell => cell[..^query.Length] + replacement;
            case SearchMode.Regex:
                var regex = new Regex(query, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                return cell => regex.Replace(cell, replacement);
            default:
                return _ => replacement;
        }
    }

    /// <param name="hiddenColumns">Cột đang ẩn: không tìm (ô không thấy được thì không nhảy tới được, Thay thế cũng không được
    /// âm thầm ghi vào đó).</param>
    public List<CellRef> Find(IReadOnlyList<CsvRow> rows, string query, SearchMode mode, bool caseSensitive,
        IReadOnlySet<int>? hiddenColumns = null)
    {
        var matches = new List<CellRef>();
        if (string.IsNullOrEmpty(query))
        {
            return matches;
        }

        Regex? regex = null;
        if (mode == SearchMode.Regex)
        {
            var options = RegexOptions.Compiled | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            regex = new Regex(query, options);
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var c = 0; c < row.CellCount; c++)
            {
                if (hiddenColumns?.Contains(c) == true)
                {
                    continue;
                }

                var cell = row.GetCell(c);
                var isMatch = mode switch
                {
                    SearchMode.Contains => cell.Contains(query, comparison),
                    SearchMode.Equals => cell.Equals(query, comparison),
                    SearchMode.StartsWith => cell.StartsWith(query, comparison),
                    SearchMode.EndsWith => cell.EndsWith(query, comparison),
                    SearchMode.Regex => regex!.IsMatch(cell),
                    _ => false,
                };

                if (isMatch)
                {
                    matches.Add(new CellRef(r, c));
                }
            }
        }

        return matches;
    }
}
