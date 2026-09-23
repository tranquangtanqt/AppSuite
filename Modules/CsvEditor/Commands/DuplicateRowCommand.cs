using CsvEditor.Models;

namespace CsvEditor.Commands;

/// <summary>Duplicating a row is just adding a clone right after the source - reuses AddRowCommand's
/// Execute/Undo, only the description differs.</summary>
public sealed class DuplicateRowCommand : AddRowCommand
{
    public DuplicateRowCommand(CsvDocument document, int sourceIndex, CsvRow sourceRow)
        : base(document, sourceIndex + 1, sourceRow.Clone())
    {
    }

    public override string Description => $"Nhân bản dòng {Index}";
}
