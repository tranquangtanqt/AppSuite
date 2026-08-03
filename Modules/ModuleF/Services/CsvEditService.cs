using ModuleF.Commands;
using ModuleF.Models;

namespace ModuleF.Services;

public sealed class CsvEditService : ICsvEditService
{
    public CsvDocument Document { get; private set; } = new();
    public UndoRedoStack UndoRedo { get; } = new();

    public void ReplaceDocument(CsvDocument document)
    {
        Document = document;
        UndoRedo.Clear();
    }

    public IReadOnlyList<CsvColumn> GetColumns() => Document.Columns;

    public IReadOnlyList<CsvRow> GetRows() => Document.Rows;

    public string GetCell(int rowIndex, int columnIndex) => Document.Rows[rowIndex].GetCell(columnIndex);

    public void SetCell(CsvRow row, int columnIndex, string value)
    {
        var oldValue = row.GetCell(columnIndex);
        if (oldValue == value)
        {
            return;
        }

        UndoRedo.Do(new SetCellCommand(row, columnIndex, oldValue, value));
        Document.IsDirty = true;
    }

    public void AddRow(int index)
    {
        var blankRow = new CsvRow(Enumerable.Repeat(string.Empty, Document.Columns.Count));
        UndoRedo.Do(new AddRowCommand(Document, index, blankRow));
        Document.IsDirty = true;
    }

    public void DuplicateRow(int index)
    {
        if (index < 0 || index >= Document.Rows.Count)
        {
            return;
        }

        UndoRedo.Do(new DuplicateRowCommand(Document, index, Document.Rows[index]));
        Document.IsDirty = true;
    }

    public void RemoveRow(int index)
    {
        if (index < 0 || index >= Document.Rows.Count)
        {
            return;
        }

        UndoRedo.Do(new RemoveRowCommand(Document, index));
        Document.IsDirty = true;
    }

    public void AddColumn(int index, string name)
    {
        UndoRedo.Do(new AddColumnCommand(Document, index, name));
        Document.IsDirty = true;
    }

    public void RemoveColumn(int index)
    {
        if (index < 0 || index >= Document.Columns.Count)
        {
            return;
        }

        UndoRedo.Do(new RemoveColumnCommand(Document, index));
        Document.IsDirty = true;
    }

    public void RenameColumn(int index, string newName)
    {
        if (index < 0 || index >= Document.Columns.Count)
        {
            return;
        }

        var column = Document.Columns[index];
        if (column.Name == newName)
        {
            return;
        }

        UndoRedo.Do(new RenameColumnCommand(Document, column, newName));
        Document.IsDirty = true;
    }

    public void PasteBlock(IReadOnlyList<CsvRow> targetRows, int startColumnIndex, IReadOnlyList<IReadOnlyList<string>> block)
    {
        var subCommands = new List<SetCellCommand>();
        for (var r = 0; r < block.Count && r < targetRows.Count; r++)
        {
            var row = targetRows[r];
            var sourceRow = block[r];
            for (var c = 0; c < sourceRow.Count; c++)
            {
                var targetColumnIndex = startColumnIndex + c;
                if (targetColumnIndex >= Document.Columns.Count)
                {
                    break;
                }

                var oldValue = row.GetCell(targetColumnIndex);
                var newValue = sourceRow[c];
                if (oldValue != newValue)
                {
                    subCommands.Add(new SetCellCommand(row, targetColumnIndex, oldValue, newValue));
                }
            }
        }

        if (subCommands.Count == 0)
        {
            return;
        }

        UndoRedo.Do(new PasteCommand(subCommands));
        Document.IsDirty = true;
    }
}
