namespace Rdbms.HtmlGenerator.Models;

/// <summary>One foreign-key constraint read from PostgreSQL - a reference from
/// <see cref="LocalColumns"/> on <see cref="TableName"/> to <see cref="ReferencedColumns"/> on
/// <see cref="ReferencedTable"/>. Composite keys are comma-joined (same shape as Mcf.DbDef.HtmlGenerator).</summary>
public sealed record DbForeignKeyRecord
{
    public required string TableName { get; init; }
    public int OrdinalPosition { get; init; }
    public required string LocalColumns { get; init; }
    public required string ReferencedTable { get; init; }
    public string ReferencedColumns { get; init; } = string.Empty;

    /// <summary>Schemas of <see cref="TableName"/> / <see cref="ReferencedTable"/> - only used by
    /// TableNameQualifier (same-named tables in different schemas); not stored in SQLite.</summary>
    public string Schema { get; init; } = string.Empty;

    public string ReferencedSchema { get; init; } = string.Empty;
}
