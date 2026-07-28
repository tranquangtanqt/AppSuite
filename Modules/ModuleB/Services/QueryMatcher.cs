using System;
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
}
