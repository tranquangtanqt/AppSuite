using ModuleF.Models;

namespace ModuleF.Commands;

/// <summary>Snapshots the removed column's metadata and every row's value at that column before
/// deleting, so Undo can re-insert the column and restore every cell to its previous value in the
/// correct row order.</summary>
public sealed class RemoveColumnCommand : IEditCommand
{
    private readonly CsvDocument _document;
    private readonly int _index;
    private CsvColumn? _removedColumn;
    private List<string>? _removedValues;

    public RemoveColumnCommand(CsvDocument document, int index)
    {
        _document = document;
        _index = index;
    }

    public string Description => $"Xóa cột '{_document.Columns[_index].Name}'";

    public bool ChangesStructure => true;

    public void Execute()
    {
        _removedColumn = _document.Columns[_index];
        _removedValues = _document.Rows.Select(r => r.GetCell(_index)).ToList();

        _document.Columns.RemoveAt(_index);
        foreach (var row in _document.Rows)
        {
            row.RemoveCellAt(_index, raiseEvent: false);
        }

        _document.RaiseStructureChanged();
    }

    public void Undo()
    {
        _document.Columns.Insert(_index, _removedColumn!);
        for (var i = 0; i < _document.Rows.Count; i++)
        {
            var value = i < _removedValues!.Count ? _removedValues[i] : string.Empty;
            _document.Rows[i].InsertCellAt(_index, value, raiseEvent: false);
        }

        _document.RaiseStructureChanged();
    }
}
