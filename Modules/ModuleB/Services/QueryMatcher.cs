using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ModuleB.Services;

/// <summary>
/// Parses queries like "10.PD AND ban hang OR 20.PG" into OR-of-AND groups, matched case-insensitively
/// as substrings against the candidate text.
/// </summary>
public static class QueryMatcher
{
    private static readonly Regex OrSplit = new(@"\s+OR\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AndSplit = new(@"\s+AND\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool Matches(string candidate, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return OrSplit.Split(query).Any(group => AndSplit.Split(group)
            .Select(term => term.Trim())
            .Where(term => term.Length > 0)
            .All(term => candidate.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Flattens a query like "10.PD AND ban hang OR 20.PG" into its individual terms, ignoring the
    /// AND/OR grouping - used to highlight which cells contain any searched word, since a single
    /// AND group's terms can legitimately live in different cells of the same file.
    /// </summary>
    public static List<string> ExtractTerms(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<string>();
        }

        return OrSplit.Split(query)
            .SelectMany(group => AndSplit.Split(group))
            .Select(term => term.Trim())
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
