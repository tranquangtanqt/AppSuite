using CsvEditor.Models;

namespace CsvEditor.Commands;

public sealed class SetCellCommand : IEditCommand
{
    private readonly CsvRow _row;
    private readonly int _columnIndex;
    private readonly string _oldValue;
    private readonly string _newValue;

    public SetCellCommand(CsvRow row, int columnIndex, string oldValue, string newValue)
    {
        _row = row;
        _columnIndex = columnIndex;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public string Description => $"Sửa ô (cột {_columnIndex + 1})";

    public bool ChangesStructure => false;

    public void Execute() => _row.SetCell(_columnIndex, _newValue);

    public void Undo() => _row.SetCell(_columnIndex, _oldValue);
}
