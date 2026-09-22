using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModuleF.Models;
using ModuleF.Services;

namespace ModuleF.ViewModels;

/// <summary>
/// Orchestrates the whole editor: owns the loaded <see cref="CsvDocument"/> (through
/// <see cref="ICsvEditService"/>), the filter/sort view-state, and exposes RelayCommands for the
/// toolbar. Dialog UI (Open/Save pickers, Filter/Sort/Statistics/Column dialogs) is the View's job -
/// this ViewModel only exposes plain methods dialogs call once the user confirms, so it stays
/// WinUI-free and testable in principle even though this repo has no test project yet.
/// </summary>
public sealed partial class ModuleFViewModel : ObservableObject
{
    private readonly ICsvFileService _fileService;
    private readonly ICsvEditService _editService;
    private readonly SearchService _searchService = new();
    private readonly FilterService _filterService = new();
    private readonly SortService _sortService = new();
    private readonly StatisticsService _statisticsService = new();

    private FilterExpression? _activeFilter;
    private readonly Dictionary<int, string> _quickFilters = new();
    private List<(int ColumnIndex, bool Descending)> _activeSort = new();
    private List<CellRef> _currentMatches = new();
    private int _currentMatchIndex = -1;

    public ModuleFViewModel(ICsvFileService fileService, ICsvEditService editService)
    {
        _fileService = fileService;
        _editService = editService;
        _editService.UndoRedo.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(UndoDescription));
            OnPropertyChanged(nameof(RedoDescription));
            IsDirty = _editService.UndoRedo.IsDirty;
        };
    }

    /// <summary>DataGrid binds ItemsSource here, never to the document's Rows directly - this is the
    /// filtered/sorted view, but it shares CsvRow instances with the document so editing a cell here
    /// still records an undoable command against the real data (see PLAN.md, mục 4).</summary>
    public ObservableCollection<CsvRow> ViewRows { get; } = new();

    public ObservableCollection<ValidationIssue> Issues { get; } = new();

    /// <summary>Raised after Open, or after a structural edit (Add/Remove/Rename Column), so the View
    /// rebuilds its DataGridColumn list.</summary>
    public event EventHandler? ColumnsChanged;

    public IReadOnlyList<CsvColumn> Columns => _editService.GetColumns();

    [ObservableProperty]
    private string _fileName = "(chưa mở file)";

    [ObservableProperty]
    private string _encodingLabel = "-";

    [ObservableProperty]
    private string _delimiterLabel = "-";

    [ObservableProperty]
    private int _rowCount;

    [ObservableProperty]
    private int _columnCount;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng.";

    [ObservableProperty]
    private string _matchStatusMessage = string.Empty;

    public bool CanUndo => _editService.UndoRedo.CanUndo;
    public bool CanRedo => _editService.UndoRedo.CanRedo;
    public string? UndoDescription => _editService.UndoRedo.UndoDescription;
    public string? RedoDescription => _editService.UndoRedo.RedoDescription;

    public Task OpenAsync(string filePath, Func<long, Task<bool>> confirmLargeFile, CancellationToken cancellationToken) =>
        OpenAsync(filePath, null, null, confirmLargeFile, cancellationToken);

    /// <summary>Overload used by the status bar's "đổi encoding/delimiter" affordance to re-open the
    /// same file with a manually chosen encoding/delimiter when auto-detection guessed wrong.</summary>
    public async Task OpenAsync(
        string filePath,
        System.Text.Encoding? encodingOverride,
        char? delimiterOverride,
        Func<long, Task<bool>> confirmLargeFile,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        ProgressPercent = 0;
        StatusMessage = $"Đang mở {Path.GetFileName(filePath)}...";
        try
        {
            var progress = new Progress<int>(p => ProgressPercent = p);
            var result = await _fileService.OpenAsync(filePath, encodingOverride, delimiterOverride, confirmLargeFile, progress, cancellationToken);

            _editService.ReplaceDocument(result.Document);
            _activeFilter = null;
            _activeSort.Clear();
            RebuildView();

            Issues.Clear();
            foreach (var issue in result.Issues)
            {
                Issues.Add(issue);
            }

            FileName = result.Document.FileName;
            EncodingLabel = DescribeEncoding(result.EncodingResult.Encoding);
            DelimiterLabel = DescribeDelimiter(result.DelimiterResult.Delimiter);
            RowCount = result.Document.Rows.Count;
            ColumnCount = result.Document.Columns.Count;
            StatusMessage = $"Đã mở {result.Document.FileName} - {RowCount:N0} dòng, {ColumnCount:N0} cột.";
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã hủy mở file.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<int>(p => ProgressPercent = p);
            await _fileService.SaveAsync(_editService.Document, progress, cancellationToken);
            _editService.UndoRedo.MarkClean();
            IsDirty = _editService.UndoRedo.IsDirty;
            StatusMessage = $"Đã lưu {_editService.Document.FileName}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveAsAsync(string filePath, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<int>(p => ProgressPercent = p);
            await _fileService.SaveAsAsync(_editService.Document, filePath, progress, cancellationToken);
            FileName = _editService.Document.FileName;
            _editService.UndoRedo.MarkClean();
            IsDirty = _editService.UndoRedo.IsDirty;
            StatusMessage = $"Đã lưu {_editService.Document.FileName}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _editService.UndoRedo.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _editService.UndoRedo.Redo();

    /// <summary>viewRowIndex is a plain O(1) index into <see cref="ViewRows"/> (the DataGrid already
    /// knows the row's index from its container) - no need to search the document for it.</summary>
    public void SetCell(int viewRowIndex, int columnIndex, string value)
    {
        var row = ViewRows[viewRowIndex];
        _editService.SetCell(row, columnIndex, value);
    }

    /// <summary>Returns the newly inserted row so the View can restore the DataGrid's selection to it -
    /// RebuildView() below clears and repopulates ViewRows, which drops the DataGrid's selection.</summary>
    public CsvRow AddRow(int? afterViewRowIndex)
    {
        var insertIndex = ResolveDocumentInsertIndex(afterViewRowIndex);
        _editService.AddRow(insertIndex);
        var newRow = _editService.Document.Rows[insertIndex];
        RebuildView();
        RowCount = _editService.Document.Rows.Count;
        return newRow;
    }

    /// <summary>Returns the duplicate row, for the same reason as <see cref="AddRow"/>.</summary>
    public CsvRow? DuplicateRow(int viewRowIndex)
    {
        var documentIndex = _editService.Document.Rows.IndexOf(ViewRows[viewRowIndex]);
        if (documentIndex < 0)
        {
            return null;
        }

        _editService.DuplicateRow(documentIndex);
        var newRow = _editService.Document.Rows[documentIndex + 1];
        RebuildView();
        RowCount = _editService.Document.Rows.Count;
        return newRow;
    }

    /// <summary>Returns the row that should become the new selection (the row that slid into the first
    /// removed row's place, or the new last row if the removal reached the end), for the same reason as
    /// <see cref="AddRow"/>.</summary>
    public CsvRow? RemoveRows(IReadOnlyList<int> viewRowIndexes)
    {
        var documentIndexes = viewRowIndexes
            .Select(i => _editService.Document.Rows.IndexOf(ViewRows[i]))
            .Where(i => i >= 0)
            .OrderByDescending(i => i)
            .ToList();

        if (documentIndexes.Count == 0)
        {
            return null;
        }

        var firstRemovedIndex = documentIndexes[^1];

        foreach (var index in documentIndexes)
        {
            _editService.RemoveRow(index);
        }

        RebuildView();
        RowCount = _editService.Document.Rows.Count;

        var rows = _editService.Document.Rows;
        if (rows.Count == 0)
        {
            return null;
        }

        return rows[Math.Min(firstRemovedIndex, rows.Count - 1)];
    }

    public void AddColumn(int index, string name)
    {
        _editService.AddColumn(index, name);
        ColumnCount = _editService.Document.Columns.Count;
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
        RebuildView();
    }

    public void RemoveColumn(int index)
    {
        _editService.RemoveColumn(index);
        ColumnCount = _editService.Document.Columns.Count;
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
        RebuildView();
    }

    public void RenameColumn(int index, string newName)
    {
        _editService.RenameColumn(index, newName);
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PasteBlock(int viewRowIndex, int columnIndex, IReadOnlyList<IReadOnlyList<string>> block)
    {
        var targetRows = ViewRows.Skip(viewRowIndex).Take(block.Count).ToList();
        _editService.PasteBlock(targetRows, columnIndex, block);
    }

    public void ApplyFilter(FilterExpression? filter)
    {
        _activeFilter = filter is { Conditions.Count: > 0 } ? filter : null;
        RebuildView();
    }

    /// <summary>Per-column "contains" filter typed directly into the DataGrid's column header row
    /// (see MainWindow.RebuildColumns), combined with AND across columns and with <see cref="ApplyFilter"/>'s
    /// advanced expression. Kept separate from <see cref="_activeFilter"/> since it is reset whenever
    /// the column list changes (RebuildColumns creates fresh, empty filter TextBoxes) while the
    /// advanced filter is not.</summary>
    public void SetQuickFilter(int columnIndex, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _quickFilters.Remove(columnIndex);
        }
        else
        {
            _quickFilters[columnIndex] = value;
        }

        RebuildView();
    }

    public void ClearQuickFilters()
    {
        _quickFilters.Clear();
    }

    public void ApplySort(List<(int ColumnIndex, bool Descending)> sortSpec)
    {
        _activeSort = sortSpec;
        RebuildView();
    }

    public void Find(string query, SearchMode mode, bool caseSensitive)
    {
        _currentMatches = _searchService.Find(ViewRows, query, mode, caseSensitive);
        _currentMatchIndex = _currentMatches.Count > 0 ? 0 : -1;
        UpdateMatchStatus();
    }

    public CellRef? CurrentMatch => _currentMatchIndex >= 0 && _currentMatchIndex < _currentMatches.Count
        ? _currentMatches[_currentMatchIndex]
        : null;

    public CellRef? NextMatch()
    {
        if (_currentMatches.Count == 0)
        {
            return null;
        }

        _currentMatchIndex = (_currentMatchIndex + 1) % _currentMatches.Count;
        UpdateMatchStatus();
        return CurrentMatch;
    }

    public CellRef? PreviousMatch()
    {
        if (_currentMatches.Count == 0)
        {
            return null;
        }

        _currentMatchIndex = (_currentMatchIndex - 1 + _currentMatches.Count) % _currentMatches.Count;
        UpdateMatchStatus();
        return CurrentMatch;
    }

    public int ReplaceAllMatches(string replacement)
    {
        if (_currentMatches.Count == 0)
        {
            return 0;
        }

        foreach (var match in _currentMatches)
        {
            SetCell(match.RowIndex, match.ColumnIndex, replacement);
        }

        var count = _currentMatches.Count;
        _currentMatches.Clear();
        _currentMatchIndex = -1;
        UpdateMatchStatus();
        return count;
    }

    public async Task<TableStatistics> ComputeStatisticsAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        return await Task.Run(() => _statisticsService.Compute(_editService.Document, progress, cancellationToken), cancellationToken);
    }

    private void RebuildView()
    {
        IEnumerable<CsvRow> query = _editService.Document.Rows;
        if (_activeFilter is not null)
        {
            query = query.Where(_filterService.Compile(_activeFilter));
        }

        foreach (var (columnIndex, text) in _quickFilters)
        {
            query = query.Where(row => row.GetCell(columnIndex).Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        if (_activeSort.Count > 0)
        {
            query = _sortService.ApplyMultiColumn(query, _activeSort);
        }

        ViewRows.Clear();
        foreach (var row in query)
        {
            ViewRows.Add(row);
        }
    }

    private int ResolveDocumentInsertIndex(int? afterViewRowIndex)
    {
        if (afterViewRowIndex is null || ViewRows.Count == 0)
        {
            return _editService.Document.Rows.Count;
        }

        var anchorRow = ViewRows[afterViewRowIndex.Value];
        var documentIndex = _editService.Document.Rows.IndexOf(anchorRow);
        return documentIndex < 0 ? _editService.Document.Rows.Count : documentIndex + 1;
    }

    private void UpdateMatchStatus()
    {
        MatchStatusMessage = _currentMatches.Count == 0
            ? "Không tìm thấy kết quả."
            : $"Kết quả {_currentMatchIndex + 1}/{_currentMatches.Count}";
    }

    private static string DescribeEncoding(System.Text.Encoding encoding)
    {
        if (encoding is System.Text.UTF8Encoding utf8)
        {
            return utf8.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8";
        }

        if (encoding.CodePage == System.Text.Encoding.Unicode.CodePage)
        {
            return "UTF-16 LE";
        }

        if (encoding.CodePage == System.Text.Encoding.BigEndianUnicode.CodePage)
        {
            return "UTF-16 BE";
        }

        return encoding.EncodingName;
    }

    private static string DescribeDelimiter(char delimiter) => delimiter switch
    {
        ',' => "Dấu phẩy (,)",
        '\t' => "Tab",
        ';' => "Chấm phẩy (;)",
        '|' => "Gạch đứng (|)",
        _ => delimiter.ToString(),
    };
}
