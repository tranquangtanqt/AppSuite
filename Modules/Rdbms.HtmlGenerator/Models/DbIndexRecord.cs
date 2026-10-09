namespace Rdbms.HtmlGenerator.Models;

/// <summary>One index on a table (primary-key / unique indexes included).</summary>
public sealed record DbIndexRecord
{
    public required string TableName { get; init; }

    /// <summary>Only used by TableNameQualifier (same-named tables in different schemas); not stored in SQLite.</summary>
    public string Schema { get; init; } = string.Empty;

    public required string IndexName { get; init; }

    /// <summary>Key columns / expressions in index order, comma-joined (vd "CUST_ID, BRANCH DESC").</summary>
    public string Columns { get; init; } = string.Empty;

    public bool IsUnique { get; init; }

    public bool IsPrimaryKey { get; init; }

    /// <summary>Access method as the DB names it (btree, gin / NORMAL, BITMAP / BTREE / CLUSTERED, NONCLUSTERED...).</summary>
    public string IndexType { get; init; } = string.Empty;

    /// <summary>Extras worth knowing: "INCLUDE (...)", "WHERE ..." (partial / filtered index).</summary>
    public string Note { get; init; } = string.Empty;
}
