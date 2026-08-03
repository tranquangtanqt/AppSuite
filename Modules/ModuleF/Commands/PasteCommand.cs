namespace ModuleF.Commands;

/// <summary>Pasting a multi-cell block is a batch of <see cref="SetCellCommand"/>s that undo/redo
/// together as one step.</summary>
public sealed class PasteCommand : IEditCommand
{
    private readonly List<SetCellCommand> _subCommands;

    public PasteCommand(List<SetCellCommand> subCommands)
    {
        _subCommands = subCommands;
    }

    public string Description => $"Dán {_subCommands.Count} ô";

    public void Execute()
    {
        foreach (var command in _subCommands)
        {
            command.Execute();
        }
    }

    public void Undo()
    {
        for (var i = _subCommands.Count - 1; i >= 0; i--)
        {
            _subCommands[i].Undo();
        }
    }
}
