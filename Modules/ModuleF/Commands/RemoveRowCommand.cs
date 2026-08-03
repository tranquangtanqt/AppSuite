using ModuleF.Models;

namespace ModuleF.Commands;

/// <summary>Snapshots the row's values at removal time so Undo can restore them exactly, even though
/// the CsvRow instance itself is discarded from <see cref="CsvDocument.Rows"/>.</summary>
public sealed class RemoveRowCommand : IEditCommand
{
    private readonly CsvDocument _document;
    private readonly int _index;
    private CsvRow? _removedSnapshot;

    public RemoveRowCommand(CsvDocument document, int index)
    {
        _document = document;
        _index = index;
    }

    public string Description => $"Xóa dòng {_index + 1}";

    public void Execute()
    {
        _removedSnapshot = _document.Rows[_index].Clone();
        _document.Rows.RemoveAt(_index);
    }

    public void Undo()
    {
        _document.Rows.Insert(_index, _removedSnapshot!.Clone());
    }
}
