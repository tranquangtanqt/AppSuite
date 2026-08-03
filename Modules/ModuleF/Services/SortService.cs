using ModuleF.Models;

namespace ModuleF.Services;

/// <summary>View-state only sort - never mutates <see cref="CsvDocument.Rows"/>. Numeric-aware:
/// compares as numbers when both cells parse, otherwise falls back to ordinal string comparison.</summary>
public sealed class SortService
{
    public IEnumerable<CsvRow> ApplyMultiColumn(IEnumerable<CsvRow> rows, IReadOnlyList<(int ColumnIndex, bool Descending)> sortSpec)
    {
        if (sortSpec.Count == 0)
        {
            return rows;
        }

        IOrderedEnumerable<CsvRow>? ordered = null;
        foreach (var (columnIndex, descending) in sortSpec)
        {
            Comparison<CsvRow> comparison = (a, b) => CompareCells(a.GetCell(columnIndex), b.GetCell(columnIndex));
            var comparer = Comparer<CsvRow>.Create(comparison);

            ordered = ordered is null
                ? (descending ? rows.OrderByDescending(r => r, comparer) : rows.OrderBy(r => r, comparer))
                : (descending ? ordered.ThenByDescending(r => r, comparer) : ordered.ThenBy(r => r, comparer));
        }

        return ordered!;
    }

    private static int CompareCells(string left, string right)
    {
        if (double.TryParse(left, out var leftNumber) && double.TryParse(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
