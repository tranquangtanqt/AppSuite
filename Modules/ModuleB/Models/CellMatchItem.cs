namespace ModuleB.Models;

/// <summary>
/// A cell shown in FileMatchesDialog, carrying the file path needed to jump to it in Excel. A plain
/// class with get-only properties, not a record: x:Bind's generated XamlTypeInfo needs settable
/// properties and fails to compile against a record's init-only accessors.
/// </summary>
public sealed class CellMatchItem
{
    public CellMatchItem(string fullPath, string sheetName, string cellReference, string text)
    {
        FullPath = fullPath;
        SheetName = sheetName;
        CellReference = cellReference;
        Text = text;
    }

    public string FullPath { get; }

    public string SheetName { get; }

    public string CellReference { get; }

    public string Text { get; }
}
