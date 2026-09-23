using System.Collections.ObjectModel;
using System.Text;

namespace CsvEditor.Models;

/// <summary>
/// The whole open CSV/TSV file: columns + every row, fully in memory. Not observable at the document
/// level - batch operations (opening a file, adding/removing a column across every row) mutate the
/// backing store directly and raise a single event afterwards, instead of one notification per row,
/// which would be prohibitively slow for large files.
/// </summary>
public sealed class CsvDocument
{
    public List<CsvColumn> Columns { get; private set; } = new();

    /// <summary>The DataGrid's ItemsSource. Replaced wholesale (never Clear()+loop Add()) when a file
    /// is opened, to avoid raising one CollectionChanged per row.</summary>
    public ObservableCollection<CsvRow> Rows { get; private set; } = new();

    public string FilePath { get; set; } = string.Empty;
    public string FileName => string.IsNullOrEmpty(FilePath) ? "(chưa lưu)" : Path.GetFileName(FilePath);
    public Encoding Encoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    public char Delimiter { get; set; } = ',';

    /// <summary>Raised once after Columns/Rows are replaced wholesale (Open, or Undo/Redo of a
    /// structural command). Subscribers should rebuild DataGrid columns and reset ItemsSource.</summary>
    public event EventHandler? StructureChanged;

    /// <summary>Replaces the entire document content in one shot - used by <c>CsvFileService.OpenAsync</c>
    /// after parsing completes on a background thread.</summary>
    public void ReplaceAll(List<CsvColumn> columns, List<CsvRow> rows)
    {
        Columns = columns;
        Rows = new ObservableCollection<CsvRow>(rows);
        RaiseStructureChanged();
    }

    /// <summary>Called by column-level commands after they finish mutating rows in bulk, to trigger a
    /// single DataGrid column rebuild instead of per-cell notifications.</summary>
    public void RaiseStructureChanged() => StructureChanged?.Invoke(this, EventArgs.Empty);
}
