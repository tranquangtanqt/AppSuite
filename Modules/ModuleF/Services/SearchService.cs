using System.Text.RegularExpressions;
using ModuleF.Models;

namespace ModuleF.Services;

/// <summary>Finds matching cells within the rows currently shown (the caller passes the post-filter
/// view, so Find only ever highlights what the user can actually see).</summary>
public sealed class SearchService
{
    public List<CellRef> Find(IReadOnlyList<CsvRow> rows, string query, SearchMode mode, bool caseSensitive)
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
