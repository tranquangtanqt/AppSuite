namespace ModuleF.Commands;

/// <summary>One undoable/redoable data mutation. Execute() is also called on Redo, so it must be
/// idempotent-safe to call a second time after an Undo restored the pre-execute state.</summary>
public interface IEditCommand
{
    void Execute();
    void Undo();

    /// <summary>Shown in the Undo/Redo button tooltip, e.g. "Sửa ô B3".</summary>
    string Description { get; }
}
