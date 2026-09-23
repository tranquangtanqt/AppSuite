namespace Rdbms.HtmlGenerator.Models;

/// <summary>One column belonging to a <see cref="DbTableRecord"/>, read from PostgreSQL.
/// <see cref="Level"/> reuses Mcf.DbDef.HtmlGenerator's convention (0 = primary-key column, 1 = everything else) so
/// the shared HTML report's "khoa chinh" styling works unchanged. <see cref="IsCommon"/> is always
/// false - Postgres has no equivalent to the Excel workbooks' shared "$...$" column groups.</summary>
public sealed class DbColumnRecord
{
    public required string TableName { get; init; }
    public int OrdinalPosition { get; init; }
    public int? Level { get; init; }
    public required string ColumnName { get; init; }
    public string Meta { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string Length { get; init; } = string.Empty;
    public string Nullable { get; init; } = string.Empty;
    public string DefaultValue { get; init; } = string.Empty;
    public string JapaneseName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string ValueRestriction { get; init; } = string.Empty;
    public bool IsCommon { get; init; }
}
