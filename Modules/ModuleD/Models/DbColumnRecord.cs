namespace ModuleD.Models;

/// <summary>One column row (項目名) belonging to a <see cref="DbTableRecord"/>.</summary>
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

    /// <summary>True when this row is expanded from a shared column-group ($EXCTRL_COLS$ etc.)
    /// defined on the workbook's "制御用"/"EXCTRL" sheet, rather than declared directly on the table.</summary>
    public bool IsCommon { get; init; }
}
