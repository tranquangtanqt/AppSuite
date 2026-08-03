using ModuleF.Models;

namespace ModuleF.Commands;

public class AddRowCommand : IEditCommand
{
    protected readonly CsvDocument Document;
    protected readonly int Index;
    protected readonly CsvRow NewRow;

    public AddRowCommand(CsvDocument document, int index, CsvRow newRow)
    {
        Document = document;
        Index = index;
        NewRow = newRow;
    }

    public virtual string Description => $"Thêm dòng {Index + 1}";

    public virtual void Execute() => Document.Rows.Insert(Index, NewRow);

    public virtual void Undo() => Document.Rows.RemoveAt(Index);
}
