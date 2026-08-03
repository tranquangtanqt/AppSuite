using ModuleF.Models;

namespace ModuleF.Services;

/// <summary>Computed on-demand (Statistics button click), not kept running in the background - Sum/
/// Average/Duplicate detection are O(n) per column and not worth paying for on every edit when the
/// file has millions of rows.</summary>
public sealed class StatisticsService
{
    // Chr(1) (SOH) is vanishingly unlikely to appear in real CSV data, unlike a printable separator.
    private static readonly char RowKeySeparator = (char)1;

    public TableStatistics Compute(CsvDocument document, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var stats = new TableStatistics
        {
            RowCount = document.Rows.Count,
            ColumnCount = document.Columns.Count,
        };

        var columnAccumulators = document.Columns
            .Select(c => new ColumnStatistics { ColumnName = c.Name })
            .ToList();
        var uniqueSets = document.Columns.Select(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)).ToList();
        var sums = new double[document.Columns.Count];
        var counts = new int[document.Columns.Count];
        var mins = new double[document.Columns.Count];
        var maxes = new double[document.Columns.Count];
        var hasNumeric = new bool[document.Columns.Count];
        Array.Fill(mins, double.MaxValue);
        Array.Fill(maxes, double.MinValue);

        var rowHashes = new HashSet<string>();
        var duplicateCount = 0;
        var totalRows = document.Rows.Count;
        var processed = 0;
        var lastPercent = -1;

        foreach (var row in document.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rowKey = string.Join(RowKeySeparator, Enumerable.Range(0, document.Columns.Count).Select(row.GetCell));
            if (!rowHashes.Add(rowKey))
            {
                duplicateCount++;
            }

            for (var c = 0; c < document.Columns.Count; c++)
            {
                var cell = row.GetCell(c);
                if (string.IsNullOrEmpty(cell))
                {
                    columnAccumulators[c].EmptyCount++;
                }
                else
                {
                    uniqueSets[c].Add(cell);
                    if (double.TryParse(cell, out var number))
                    {
                        hasNumeric[c] = true;
                        sums[c] += number;
                        counts[c]++;
                        mins[c] = Math.Min(mins[c], number);
                        maxes[c] = Math.Max(maxes[c], number);
                    }
                }
            }

            processed++;
            if (totalRows > 0 && processed % 5000 == 0)
            {
                var percent = (int)(processed * 100L / totalRows);
                if (percent != lastPercent)
                {
                    progress?.Report(percent);
                    lastPercent = percent;
                }
            }
        }

        for (var c = 0; c < document.Columns.Count; c++)
        {
            columnAccumulators[c].UniqueCount = uniqueSets[c].Count;
            if (hasNumeric[c] && counts[c] > 0)
            {
                columnAccumulators[c].Sum = sums[c];
                columnAccumulators[c].Average = sums[c] / counts[c];
                columnAccumulators[c].Min = mins[c];
                columnAccumulators[c].Max = maxes[c];
            }
        }

        stats.PerColumn.AddRange(columnAccumulators);
        stats.DuplicateRowCount = duplicateCount;
        progress?.Report(100);
        return stats;
    }
}
