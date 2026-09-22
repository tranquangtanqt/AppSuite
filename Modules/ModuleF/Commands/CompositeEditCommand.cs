namespace ModuleF.Commands;

/// <summary>Batches several sub-commands so they undo/redo together as one step - used by paste and by
/// Replace All, whose descriptions differ but whose execute/undo mechanics are identical.</summary>
public sealed class CompositeEditCommand : IEditCommand
{
    private readonly List<IEditCommand> _subCommands;

    public CompositeEditCommand(List<IEditCommand> subCommands, string description)
    {
        _subCommands = subCommands;
        Description = description;
    }

    public string Description { get; }

    public bool ChangesStructure => _subCommands.Any(command => command.ChangesStructure);

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
