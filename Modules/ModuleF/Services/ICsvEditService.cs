using ModuleF.Commands;
using ModuleF.Models;

namespace ModuleF.Services;

/// <summary>
/// The single place that creates <see cref="IEditCommand"/>s and pushes them through the
/// <see cref="UndoRedoStack"/>. ViewModels call the business-intent methods here (SetCell, AddRow,
/// ...) and never construct a command directly - keeps "what changed" (Commands) separate from
/// "what the user asked for" (ViewModel).
/// </summary>
public interface ICsvEditService
{
    CsvDocument Document { get; }
    UndoRedoStack UndoRedo { get; }

    void ReplaceDocument(CsvDocument document);

    IReadOnlyList<CsvColumn> GetColumns();
    IReadOnlyList<CsvRow> GetRows();
    string GetCell(int rowIndex, int columnIndex);

    /// <summary>Takes the row instance directly rather than an index - the DataGrid's cell-edit
    /// event and the filtered/sorted view both already have the CsvRow in hand, and an index lookup
    /// (O(n) IndexOf into a possibly multi-million-row collection) would be wasteful per keystroke.</summary>
    void SetCell(CsvRow row, int columnIndex, string value);
    void AddRow(int index);
    void DuplicateRow(int index);
    void RemoveRow(int index);
    void AddColumn(int index, string name);
    void RemoveColumn(int index);
    void RenameColumn(int index, string newName);
    void PasteBlock(IReadOnlyList<CsvRow> targetRows, int startColumnIndex, IReadOnlyList<IReadOnlyList<string>> block);
    void ReplaceCells(IReadOnlyList<(CsvRow Row, int ColumnIndex, string Value)> replacements);
}
