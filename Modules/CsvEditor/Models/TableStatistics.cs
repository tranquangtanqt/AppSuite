namespace CsvEditor.Models;

public sealed class ColumnStatistics
{
    public required string ColumnName { get; init; }
    public int EmptyCount { get; set; }
    public int UniqueCount { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Average { get; set; }
    public double? Sum { get; set; }
}

public sealed class TableStatistics
{
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public int DuplicateRowCount { get; set; }
    public List<ColumnStatistics> PerColumn { get; } = new();
}
