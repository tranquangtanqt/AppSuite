namespace ModuleC.Models;

/// <summary>One row of the result grid on the right-hand side.</summary>
public sealed class SearchResultRow
{
    public int Stt { get; init; }
    public string TableName { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
}
