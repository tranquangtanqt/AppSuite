namespace Rdbms.HtmlGenerator.Models;

/// <summary>One UNIQUE or CHECK constraint (primary / foreign keys have their own records).</summary>
public sealed record DbConstraintRecord
{
    public required string TableName { get; init; }

    /// <summary>Only used by TableNameQualifier (same-named tables in different schemas); not stored in SQLite.</summary>
    public string Schema { get; init; } = string.Empty;

    public required string ConstraintName { get; init; }

    /// <summary>"UNIQUE" or "CHECK".</summary>
    public required string ConstraintType { get; init; }

    /// <summary>Columns for UNIQUE ("A, B"), the condition for CHECK ("PRICE &gt; 0").</summary>
    public string Definition { get; init; } = string.Empty;
}
