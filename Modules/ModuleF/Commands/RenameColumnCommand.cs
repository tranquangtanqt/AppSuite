using ModuleF.Models;

namespace ModuleF.Commands;

public sealed class RenameColumnCommand : IEditCommand
{
    private readonly CsvDocument _document;
    private readonly CsvColumn _column;
    private readonly string _oldName;
    private readonly string _newName;

    public RenameColumnCommand(CsvDocument document, CsvColumn column, string newName)
    {
        _document = document;
        _column = column;
        _oldName = column.Name;
        _newName = newName;
    }

    public string Description => $"Đổi tên cột '{_oldName}' → '{_newName}'";

    public void Execute()
    {
        _column.Name = _newName;
        _document.RaiseStructureChanged();
    }

    public void Undo()
    {
        _column.Name = _oldName;
        _document.RaiseStructureChanged();
    }
}
