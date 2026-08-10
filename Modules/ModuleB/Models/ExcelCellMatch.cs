namespace ModuleB.Models;

/// <summary>One non-empty cell extracted from a worksheet at index time.</summary>
public sealed record ExcelCellMatch(string SheetName, string CellReference, int RowIndex, int ColumnIndex, string Text);
