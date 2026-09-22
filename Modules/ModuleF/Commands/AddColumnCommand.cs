using ModuleF.Models;

namespace ModuleF.Commands;

/// <summary>Inserts a column and a blank cell in every row. Mutates rows without raising a
/// per-row PropertyChanged (see <see cref="CsvRow.InsertCellAt"/>) and raises one
/// <see cref="CsvDocument.StructureChanged"/> instead, so the ViewModel rebuilds DataGrid columns and
/// refreshes the grid exactly once even for large row counts.</summary>
public sealed class AddColumnCommand : IEditCommand
{
    private readonly CsvDocument _document;
    private readonly int _index;
    private readonly string _columnName;

    public AddColumnCommand(CsvDocument document, int index, string columnName)
    {
        _document = document;
        _index = index;
        _columnName = columnName;
    }

    public string Description => $"Thêm cột '{_columnName}'";

    public bool ChangesStructure => true;

    public void Execute()
    {
        _document.Columns.Insert(_index, new CsvColumn(_columnName));
        foreach (var row in _document.Rows)
        {
            row.InsertCellAt(_index, string.Empty, raiseEvent: false);
        }

        _document.RaiseStructureChanged();
    }

    public void Undo()
    {
        _document.Columns.RemoveAt(_index);
        foreach (var row in _document.Rows)
        {
            row.RemoveCellAt(_index, raiseEvent: false);
        }

        _document.RaiseStructureChanged();
    }
}
