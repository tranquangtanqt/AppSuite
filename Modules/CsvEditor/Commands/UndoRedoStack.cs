namespace CsvEditor.Commands;

/// <summary>Standard linear undo/redo stack. A new <see cref="Do"/> after an Undo clears the redo
/// history, matching every mainstream editor's convention.</summary>
public sealed class UndoRedoStack
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();

    /// <summary>The undo-stack top at the moment of the last Save (or Open/Clear), null when that
    /// moment was an empty stack. Comparing it by reference against the current top is what lets
    /// <see cref="IsDirty"/> answer correctly after an Undo/Redo, not just after a fresh edit: content
    /// is back to the saved state iff we are back at the exact same position in the linear command
    /// history, and reference identity of the top command is a reliable stand-in for that position
    /// (Do() after an Undo discards the old redo instances, so identities never get reused across
    /// diverging timelines).</summary>
    private IEditCommand? _cleanMarker;
    private bool _cleanMarkerIsEmptyStack = true;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Count > 0 ? _undo.Peek().Description : null;
    public string? RedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

    public bool IsDirty => _cleanMarkerIsEmptyStack
        ? _undo.Count > 0
        : _undo.Count == 0 || !ReferenceEquals(_undo.Peek(), _cleanMarker);

    public event EventHandler? StateChanged;

    public void Do(IEditCommand command)
    {
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns the command that was undone (null if there was nothing to undo) so the caller
    /// can check <see cref="IEditCommand.ChangesStructure"/> and only pay for a full view rebuild when
    /// the undone command actually needs one.</summary>
    public IEditCommand? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        var command = _undo.Pop();
        command.Undo();
        _redo.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return command;
    }

    /// <summary>Same contract as <see cref="Undo"/>, for Redo.</summary>
    public IEditCommand? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        var command = _redo.Pop();
        command.Execute();
        _undo.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return command;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        MarkClean();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Call after a successful Save/Save As so <see cref="IsDirty"/> tracks the document's
    /// position in the undo history relative to right now, instead of a one-shot boolean.</summary>
    public void MarkClean()
    {
        _cleanMarkerIsEmptyStack = _undo.Count == 0;
        _cleanMarker = _cleanMarkerIsEmptyStack ? null : _undo.Peek();
    }
}
