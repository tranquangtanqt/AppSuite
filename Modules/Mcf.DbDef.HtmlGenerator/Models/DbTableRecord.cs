namespace Mcf.DbDef.HtmlGenerator.Models;

/// <summary>One "テーブル/ビュー" block parsed out of a DBDef workbook.</summary>
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
