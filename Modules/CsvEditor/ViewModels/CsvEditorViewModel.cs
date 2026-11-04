using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CsvEditor.Models;
using CsvEditor.Services;

namespace CsvEditor.ViewModels;

/// <summary>Kết quả dán: số dòng / cột đã dán, phần bị bỏ vì vượt mép bảng, ô cuối của phần đã dán.</summary>
public readonly record struct RangePasteResult(int Rows, int Columns, int SkippedRows, int SkippedColumns, CellRef? LastCell);

/// <summary>
/// Orchestrates the whole editor: owns the loaded <see cref="CsvDocument"/> (through
/// <see cref="ICsvEditService"/>), the filter/sort view-state, and exposes RelayCommands for the
/// toolbar. Dialog UI (Open/Save pickers, Filter/Sort/Statistics/Column dialogs) is the View's job -
/// this ViewModel only exposes plain methods dialogs call once the user confirms, so it stays
/// WinUI-free and testable in principle even though this repo has no test project yet.
/// </summary>
public sealed partial class CsvEditorViewModel : ObservableObject
{
    private readonly ICsvFileService _fileService;
    private readonly ICsvEditService _editService;
    private readonly SearchService _searchService = new();
    private readonly FilterService _filterService = new();
    private readonly SortService _sortService = new();
    private readonly StatisticsService _statisticsService = new();
    private readonly IValidationService _validationService = new ValidationService();

    private FilterExpression? _activeFilter;
    private readonly Dictionary<int, string> _quickFilters = new();
    private List<(int ColumnIndex, bool Descending)> _activeSort = new();
    private List<CellRef> _currentMatches = new();
    private int _currentMatchIndex = -1;
    private string _findQuery = string.Empty;
    private SearchMode _findMode;
    private bool _findCaseSensitive;
    private bool _findInvalidRegex;
    private HashSet<int> _hiddenColumns = new();

    public CsvEditorViewModel(ICsvFileService fileService, ICsvEditService editService)
    {
        _fileService = fileService;
        _editService = editService;
        _editService.UndoRedo.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(UndoDescription));
            OnPropertyChanged(nameof(RedoDescription));
            // Nút Undo/Redo chỉ bật/tắt khi RelayCommand báo CanExecuteChanged - PropertyChanged(CanUndo)
            // không đủ. Trước đây nút cập nhật "ké" nhờ MainWindow gọi Bindings.Update() (gán lại Command)
            // mỗi lần ViewModel đổi; bỏ cơ chế đó thì phải báo trực tiếp ở đây.
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            IsDirty = _editService.UndoRedo.IsDirty;
            RefreshMatches();
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

    /// <summary>Đường dẫn file đang mở (cập nhật khi Save As), null nếu chưa mở / chưa lưu file nào.</summary>
    public string? CurrentFilePath => string.IsNullOrEmpty(_editService.Document.FilePath) ? null : _editService.Document.FilePath;

    [ObservableProperty]
    private string _fileName = "(chưa mở file)";

    [ObservableProperty]
    private string _encodingLabel = "-";

    [ObservableProperty]
    private string _delimiterLabel = "-";

    /// <summary>Dòng đầu file là tiêu đề - người dùng chọn (checkbox trên toolbar), áp dụng cho lần mở file tiếp theo /
    /// <see cref="ReopenAsync"/>. Không tự đoán: file toàn chữ hay toàn số đều có thể có hoặc không có tiêu đề.</summary>
    [ObservableProperty]
    private bool _hasHeader = true;

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

    public Task<bool> OpenAsync(string filePath, Func<long, Task<bool>> confirmLargeFile, CancellationToken cancellationToken) =>
        OpenAsync(filePath, null, null, confirmLargeFile, cancellationToken);

    /// <summary>Overload used by the status bar's "đổi encoding/delimiter" affordance to re-open the
    /// same file with a manually chosen encoding/delimiter when auto-detection guessed wrong. True nếu đã mở xong,
    /// false nếu huỷ; lỗi đọc file (không tồn tại, bị khoá...) ném ra cho View báo.</summary>
    public async Task<bool> OpenAsync(
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
            var result = await _fileService.OpenAsync(filePath, encodingOverride, delimiterOverride, confirmLargeFile, progress, cancellationToken, HasHeader);

            _editService.ReplaceDocument(result.Document);
            _activeFilter = null;
            _activeSort.Clear();
            // Nút Clear Filter / Clear Sort bind IsEnabled vào 2 cờ này - phải báo, không thì vẫn sáng
            // sau khi mở file mới (trước đây Bindings.Update() ở MainWindow che mất thiếu sót này).
            OnPropertyChanged(nameof(ActiveFilter));
            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(ActiveSort));
            OnPropertyChanged(nameof(IsSortActive));
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
            StatusMessage = $"Đã mở {result.Document.FileName} - {RowCount:N0} dòng, {ColumnCount:N0} cột" + (HasHeader ? "." : " (không có dòng tiêu đề).");
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã huỷ mở file.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Mở lại file đang mở với encoding / delimiter hiện tại và <see cref="HasHeader"/> mới (người dùng đổi
    /// lựa chọn "Dòng đầu là tiêu đề" khi file đang mở). Bỏ các thay đổi chưa lưu - bên gọi hỏi trước.</summary>
    /// <summary>Tài liệu chưa gắn file (chưa mở / chưa lưu): lựa chọn áp dụng ngay cho lần Save As tới. Đã có file thì
    /// chỉ đổi khi mở lại (<see cref="ReopenAsync"/>) - tài liệu đang hiện vẫn đúng với cách nó được đọc.</summary>
    partial void OnHasHeaderChanged(bool value)
    {
        if (CurrentFilePath is null)
        {
            _editService.Document.HasHeader = value;
        }
    }

    public Task<bool> ReopenAsync(string filePath, Func<long, Task<bool>> confirmLargeFile, CancellationToken cancellationToken) =>
        OpenAsync(filePath, _editService.Document.Encoding, _editService.Document.Delimiter, confirmLargeFile, cancellationToken);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<int>(p => ProgressPercent = p);
            await _fileService.SaveAsync(_editService.Document, progress, cancellationToken);
            _editService.UndoRedo.MarkClean();
            IsDirty = _editService.UndoRedo.IsDirty;
            RevalidateColumns();
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
            RevalidateColumns();
            StatusMessage = $"Đã lưu {_editService.Document.FileName}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Xuất ra .xlsx đúng các dòng đang hiện (theo lọc / sắp xếp hiện tại) - không đổi file đang mở, không
    /// đổi trạng thái đã lưu. Lỗi ghi file ném ra cho View báo.</summary>
    public async Task<XlsxExportResult> ExportXlsxAsync(string filePath, CancellationToken cancellationToken)
    {
        IsBusy = true;
        ProgressPercent = 0;
        try
        {
            var progress = new Progress<int>(p => ProgressPercent = p);
            var columnNames = Columns.Select(c => c.Name).ToList();
            var rows = ViewRows.ToList(); // chụp lại trên UI thread, ghi ở luồng nền
            var writeHeader = _editService.Document.HasHeader;
            var sheetName = Path.GetFileNameWithoutExtension(_editService.Document.FileName);
            var result = await Task.Run(
                () => XlsxExporter.Export(filePath, sheetName, columnNames, writeHeader, rows, progress, cancellationToken),
                cancellationToken);

            var notes = new List<string>();
            if (rows.Count < _editService.Document.Rows.Count)
            {
                notes.Add($"đang lọc: {rows.Count:N0}/{_editService.Document.Rows.Count:N0} dòng");
            }
            if (result.SkippedRows > 0)
            {
                notes.Add($"bỏ {result.SkippedRows:N0} dòng vượt giới hạn Excel");
            }
            if (result.SkippedColumns > 0)
            {
                notes.Add($"bỏ {result.SkippedColumns:N0} cột vượt giới hạn Excel");
            }
            if (result.TruncatedCells > 0)
            {
                notes.Add($"cắt {result.TruncatedCells:N0} ô dài quá 32.767 ký tự");
            }
            StatusMessage = $"Đã xuất {Path.GetFileName(filePath)} - {result.RowsWritten:N0} dòng" + (notes.Count > 0 ? $" ({string.Join("; ", notes)})." : ".");
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Undo/Redo only mutate <see cref="ICsvEditService.Document"/> directly (see
    /// UndoRedoStack.Undo/Redo) - a plain cell edit's Undo is cheap (the CsvRow raises its own
    /// PropertyChanged, the DataGrid updates just that cell), but Undo/Redo of a row/column
    /// add/remove/rename needs <see cref="RebuildView"/> and a DataGrid column rebuild to not go stale,
    /// same as the forward action already does. Gated on IEditCommand.ChangesStructure rather than
    /// unconditionally refreshing after every Undo/Redo, which would make even undoing a single cell
    /// edit pay for a full RebuildView/RevalidateColumns pass (O(rows) or worse) on a large file.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_editService.UndoRedo.Undo() is { ChangesStructure: true })
        {
            RefreshAfterStructuralChange();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_editService.UndoRedo.Redo() is { ChangesStructure: true })
        {
            RefreshAfterStructuralChange();
        }
    }

    /// <summary>For Add/Remove/Rename Column - also rebuilds the DataGrid's columns, unlike
    /// <see cref="RefreshAfterRowsChanged"/>.</summary>
    private void RefreshAfterStructuralChange()
    {
        RefreshAfterRowsChanged();
        ColumnCount = _editService.Document.Columns.Count;
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>For Add/Duplicate/Remove Row - deliberately does not raise <see cref="ColumnsChanged"/>:
    /// that would make MainWindow.RebuildColumns() run on every row add/delete, which also resets the
    /// per-column quick filter textboxes (see RebuildColumns' ClearQuickFilters call) - an unwanted
    /// reset for an operation that never touches columns.</summary>
    private void RefreshAfterRowsChanged()
    {
        RebuildView();
        RowCount = _editService.Document.Rows.Count;
        RevalidateColumns();
    }

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
        RefreshAfterRowsChanged();
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
        RefreshAfterRowsChanged();
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

        RefreshAfterRowsChanged();

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
        RefreshAfterStructuralChange();
    }

    public void RemoveColumn(int index)
    {
        _editService.RemoveColumn(index);
        RefreshAfterStructuralChange();
    }

    public void RenameColumn(int index, string newName)
    {
        _editService.RenameColumn(index, newName);
        RefreshAfterStructuralChange();
    }

    public void PasteBlock(int viewRowIndex, int columnIndex, IReadOnlyList<IReadOnlyList<string>> block)
    {
        var targetRows = ViewRows.Skip(viewRowIndex).Take(block.Count).ToList();
        _editService.PasteBlock(targetRows, columnIndex, block);
    }

    /// <summary>The advanced filter dialog's own state, so MainWindow can re-open it pre-filled with
    /// the currently applied conditions, and so toolbar buttons can show/enable a quick "clear" action
    /// without the dialog being open.</summary>
    public FilterExpression? ActiveFilter => _activeFilter;

    public bool IsFilterActive => _activeFilter is not null;

    public void ApplyFilter(FilterExpression? filter)
    {
        _activeFilter = filter is { Conditions.Count: > 0 } ? filter : null;
        RebuildView();
        OnPropertyChanged(nameof(ActiveFilter));
        OnPropertyChanged(nameof(IsFilterActive));
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

    /// <summary>So MainWindow can re-open the sort dialog pre-filled with the currently applied sort
    /// spec, instead of always starting from a blank row.</summary>
    public IReadOnlyList<(int ColumnIndex, bool Descending)> ActiveSort => _activeSort;

    public bool IsSortActive => _activeSort.Count > 0;

    public void ApplySort(List<(int ColumnIndex, bool Descending)> sortSpec)
    {
        _activeSort = sortSpec;
        RebuildView();
        OnPropertyChanged(nameof(ActiveSort));
        OnPropertyChanged(nameof(IsSortActive));
    }

    /// <summary>Tìm trên các dòng đang hiện. Thanh tìm không chặn bảng nên điều kiện tìm được nhớ lại: mỗi lần bảng đổi
    /// (sửa ô, lọc, sắp xếp, mở file...) <see cref="RefreshMatches"/> tính lại - kết quả cũ trỏ theo vị trí dòng, để nguyên
    /// thì Thay thế tất cả sẽ ghi vào nhầm ô.</summary>
    public void Find(string query, SearchMode mode, bool caseSensitive)
    {
        _findQuery = query;
        _findMode = mode;
        _findCaseSensitive = caseSensitive;
        ComputeMatches();
        _currentMatchIndex = _currentMatches.Count > 0 ? 0 : -1;
        UpdateMatchStatus();
    }

    /// <summary>Ẩn / hiện cột là việc của giao diện (DataGrid), báo vào đây để Tìm bỏ qua cột đang ẩn.</summary>
    public void SetHiddenColumns(IEnumerable<int> columnIndexes)
    {
        var hidden = columnIndexes.ToHashSet();
        if (!hidden.SetEquals(_hiddenColumns))
        {
            _hiddenColumns = hidden;
            RefreshMatches();
        }
    }

    public RangeStatistics ComputeRangeStatistics(CellRange range) => RangeStatistics.Compute(ViewRows, range, _hiddenColumns);

    private IEnumerable<int> VisibleColumnsFrom(int columnIndex) =>
        Enumerable.Range(columnIndex, Math.Max(ColumnCount - columnIndex, 0)).Where(column => !_hiddenColumns.Contains(column));

    /// <summary>Text dạng Tab của vùng ô (dán được sang Excel), bỏ cột đang ẩn.</summary>
    public string FormatRange(CellRange range)
    {
        var columns = VisibleColumnsFrom(range.Left).TakeWhile(column => column <= range.Right).ToList();
        return TabularClipboard.Format(ViewRows.Skip(range.Top).Take(range.Bottom - range.Top + 1)
            .Select(row => columns.Select(row.GetCell)));
    }

    /// <summary>Dán khối chữ từ ô <paramref name="start"/> theo bảng đang hiện: dòng đang bị lọc và cột đang ẩn được bỏ qua, không
    /// bị ghi đè lúc người dùng không thấy. Phần vượt mép bảng thì bỏ và báo lại. 1 bước Hoàn tác.</summary>
    public RangePasteResult PasteRange(CellRef start, IReadOnlyList<IReadOnlyList<string>> block)
    {
        var columns = VisibleColumnsFrom(start.ColumnIndex).ToList();
        var rowCount = Math.Min(block.Count, Math.Max(ViewRows.Count - start.RowIndex, 0));
        var widest = block.Count == 0 ? 0 : block.Max(row => row.Count);
        var columnCount = Math.Min(widest, columns.Count);
        var replacements = new List<(CsvRow, int, string)>();
        for (var r = 0; r < rowCount; r++)
        {
            for (var c = 0; c < block[r].Count && c < columns.Count; c++)
            {
                replacements.Add((ViewRows[start.RowIndex + r], columns[c], block[r][c]));
            }
        }

        _editService.ReplaceCells(replacements, $"Dán {replacements.Count} ô");
        CellRef? last = rowCount > 0 && columnCount > 0 ? new CellRef(start.RowIndex + rowCount - 1, columns[columnCount - 1]) : null;
        return new RangePasteResult(rowCount, columnCount, block.Count - rowCount, widest - columnCount, last);
    }

    /// <summary>Điền 1 giá trị vào mọi ô thấy được của vùng (dán 1 ô lên vùng nhiều ô, như Excel). Trả về số ô.</summary>
    public int FillRange(CellRange range, string value)
    {
        var columns = VisibleColumnsFrom(range.Left).TakeWhile(column => column <= range.Right).ToList();
        var replacements = ViewRows.Skip(range.Top).Take(range.Bottom - range.Top + 1)
            .SelectMany(row => columns.Select(column => (row, column, value)))
            .ToList();
        _editService.ReplaceCells(replacements, $"Dán {replacements.Count} ô");
        return replacements.Count;
    }

    /// <summary>Đóng thanh tìm: bỏ kết quả, thôi tính lại khi bảng đổi.</summary>
    public void ClearFind() => Find(string.Empty, _findMode, _findCaseSensitive);

    private void ComputeMatches()
    {
        _findInvalidRegex = false;
        try
        {
            _currentMatches = _searchService.Find(ViewRows, _findQuery, _findMode, _findCaseSensitive, _hiddenColumns);
        }
        catch (ArgumentException)
        {
            // Regex đang gõ dở, vd "(".
            _findInvalidRegex = true;
            _currentMatches = new List<CellRef>();
        }
    }

    /// <summary>Tính lại sau khi bảng đổi, giữ kết quả đang đứng nếu ô đó vẫn khớp (không đổi dòng đang chọn).</summary>
    private void RefreshMatches()
    {
        if (_findQuery.Length == 0)
        {
            return;
        }

        var current = CurrentMatch;
        ComputeMatches();
        var sameCell = current is { } cell ? _currentMatches.IndexOf(cell) : -1;
        _currentMatchIndex = sameCell >= 0 ? sameCell : Math.Min(Math.Max(_currentMatchIndex, 0), _currentMatches.Count - 1);
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

    /// <summary>Routed through <see cref="ICsvEditService.ReplaceCells"/> rather than calling
    /// <see cref="SetCell"/> per match, so replacing N matches is one undoable command instead of N -
    /// otherwise undoing a "Replace All" of 50 matches took 50 presses of Ctrl+Z.</summary>
    public int ReplaceAllMatches(string replacement)
    {
        if (_currentMatches.Count == 0)
        {
            return 0;
        }

        var replace = SearchService.CreateReplacer(_findQuery, _findMode, _findCaseSensitive, replacement);
        var replacements = _currentMatches
            .Select(match => (ViewRows[match.RowIndex], match.ColumnIndex, replace(ViewRows[match.RowIndex].GetCell(match.ColumnIndex))))
            .ToList();
        _editService.ReplaceCells(replacements); // lịch sử Hoàn tác đổi → RefreshMatches tìm lại trên nội dung mới
        return replacements.Count;
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

        RefreshMatches();
    }

    /// <summary>Re-runs the checks that can be re-derived from the current in-memory document - "column
    /// looks suspiciously empty" and "row's field count doesn't match the header" - after a structural
    /// edit or a Save. These were only ever computed once, right after Open, so neither deleting rows
    /// nor fixing a malformed row (typing into its missing trailing cells) ever updated the "Cảnh báo"
    /// list. Only discards issues marked <see cref="ValidationIssue.IsRecomputable"/> - leaves alone the
    /// encoding/delimiter warnings and unclosed-quote errors from Open, which describe the original file
    /// text and can't be re-derived from parsed rows.</summary>
    private void RevalidateColumns()
    {
        for (var i = Issues.Count - 1; i >= 0; i--)
        {
            if (Issues[i].IsRecomputable)
            {
                Issues.RemoveAt(i);
            }
        }

        foreach (var issue in _validationService.ValidateColumns(_editService.Document))
        {
            Issues.Add(issue);
        }

        foreach (var issue in _validationService.ValidateFieldCounts(_editService.Document))
        {
            Issues.Add(issue);
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
        MatchStatusMessage = _findQuery.Length == 0 ? string.Empty
            : _findInvalidRegex ? "Regex không hợp lệ."
            : _currentMatches.Count == 0 ? "Không tìm thấy kết quả."
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

        if (encoding.CodePage == 932)
        {
            return "Shift-JIS";
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
