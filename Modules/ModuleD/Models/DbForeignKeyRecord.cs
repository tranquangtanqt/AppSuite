namespace ModuleD.Models;

/// <summary>One "FOREIGN" row parsed out of a table's column list - a reference from
/// <see cref="LocalColumns"/> on <see cref="TableName"/> to <see cref="ReferencedColumns"/> (or, if
/// blank, the same-named columns) on <see cref="ReferencedTable"/>.</summary>
public sealed class DbForeignKeyRecord
{
    public required string TableName { get; init; }
    public int OrdinalPosition { get; init; }
    public required string LocalColumns { get; init; }
    public required string ReferencedTable { get; init; }
    public string ReferencedColumns { get; init; } = string.Empty;
}
