namespace CsvEditor.Commands;

/// <summary>One undoable/redoable data mutation. Execute() is also called on Redo, so it must be
/// idempotent-safe to call a second time after an Undo restored the pre-execute state.</summary>
public interface IEditCommand
{
    void Execute();
    void Undo();

    /// <summary>Shown in the Undo/Redo button tooltip, e.g. "Sửa ô B3".</summary>
    string Description { get; }

    /// <summary>True for commands that change row/column count or order (Add/Remove Row, Add/Remove/
    /// Rename Column) - the ViewModel uses this to decide whether an Undo/Redo needs to rebuild the
    /// filtered/sorted view and DataGrid columns (expensive: O(rows) or worse on a large file) or can
    /// stay as cheap as a normal cell edit (a single CsvRow property notification).</summary>
    bool ChangesStructure { get; }
}
