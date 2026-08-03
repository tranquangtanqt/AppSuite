namespace ModuleF.Models;

public enum SearchMode
{
    Contains,
    Equals,
    StartsWith,
    EndsWith,
    Regex,
}

public readonly record struct CellRef(int RowIndex, int ColumnIndex);
