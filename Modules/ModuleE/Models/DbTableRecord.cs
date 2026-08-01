namespace ModuleE.Models;

/// <summary>One table/view read from a PostgreSQL schema. Fields with no Postgres equivalent
/// (JapaneseName, ManagementType, CautionItems, RevisionHistory, Alias, Note) stay empty - kept only
/// so this shape matches ModuleD's and the HTML report can be reused unchanged.</summary>
public sealed class DbTableRecord
{
    public required string TableName { get; init; }
    public string Alias { get; init; } = string.Empty;
    public string JapaneseName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ManagementType { get; init; } = string.Empty;
    public string CautionItems { get; init; } = string.Empty;
    public string RevisionHistory { get; init; } = string.Empty;
    public required string SourceFile { get; init; }
    public required string SourceSheet { get; init; }
}
