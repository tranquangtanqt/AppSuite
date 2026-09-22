using System.ComponentModel;
using System.Numerics;
using CommunityToolkit.WinUI.UI.Controls;
using CommunityToolkit.WinUI.UI.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ModuleF.Models;
using ModuleF.Services;
using ModuleF.ViewModels;
using ModuleF.Views;
using Windows.Foundation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ModuleF;

/// <summary>Used by MainWindow.xaml's FilterHeaderTemplate to only show the filter TextBox's clear
/// button once there is text to clear. A top-level public class (not nested in MainWindow) because
/// XAML's "local:" resource reference needs an instantiable, resolvable type.</summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Owns every dialog/picker interaction (View responsibility) and wires DataGrid events to plain
/// ViewModel method calls - the ViewModel itself never references WinUI types. See PLAN.md for why
/// cell edits use a OneWay indexer binding + <see cref="Grid_CellEditEnding"/> instead of TwoWay:
/// TwoWay would write the new value straight into the model, bypassing the Undo/Redo command that
/// SetCell needs to create.
/// </summary>
public sealed partial class MainWindow : Window
{
    private CancellationTokenSource? _currentOperationCts;
    private string? _currentFilePath;

    // ----- Fill handle drag state (see FillHandleLayer in MainWindow.xaml) -----
    private ScrollViewer? _gridScrollViewer;
    private bool _isFillDragging;
    private int _fillSourceRowIndex = -1;
    private int _fillSourceColumnIndex = -1;
    private string _fillSourceValue = string.Empty;
    private int _fillTargetRowIndex = -1;

    public MainWindow()
    {
        var editService = new CsvEditService();
        var fileService = new CsvFileService(new EncodingDetector(), new DelimiterDetector(), new ValidationService());
        ViewModel = new ModuleFViewModel(fileService, editService);

        InitializeComponent();

        ViewModel.ColumnsChanged += (_, _) => RebuildColumns();
        ViewModel.PropertyChanged += (_, _) => Bindings.Update();
        ViewModel.Issues.CollectionChanged += (_, _) => Bindings.Update();

        RebuildColumns();
    }

    public ModuleFViewModel ViewModel { get; }

    public Visibility IsBusyVisibility => ViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasIssuesVisibility => ViewModel.Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string IssuesHeader => $"Cảnh báo ({ViewModel.Issues.Count})";

    public string SaveStateText => ViewModel.IsDirty ? "● Chưa lưu" : "Đã lưu";

    // ----- DataGrid column/row wiring -----

    /// <summary>Backs each column header: a name label plus a "Filter" TextBox (with an inline clear
    /// button, see ClearFilterButton_Click) stacked underneath, rendered by the "FilterHeaderStyle"/
    /// "FilterHeaderTemplate" resources declared on the DataGrid in MainWindow.xaml. Implements
    /// INotifyPropertyChanged so the clear button setting FilterText back to "" pushes that back into
    /// the TextBox's TwoWay-bound Text (not just into the ViewModel) - without it, WinUI's binding
    /// engine has no way to know the source changed and the TextBox would keep showing the old text.</summary>
    private sealed class FilterHeaderItem : INotifyPropertyChanged
    {
        private readonly Action<string> _onFilterChanged;
        private string _filterText = string.Empty;

        public FilterHeaderItem(string columnName, Action<string> onFilterChanged)
        {
            ColumnName = columnName;
            _onFilterChanged = onFilterChanged;
        }

        public string ColumnName { get; }

        public string FilterText
        {
            get => _filterText;
            set
            {
                _filterText = value;
                _onFilterChanged(value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilterText)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>Rebuilds the DataGrid's columns and their filter headers together, index-for-index.
    /// Width is a fixed pixel value computed once (EstimateColumnWidth), not SizeToCells/Auto: those
    /// resize the column to whatever rows the DataGrid currently has *realized* (it virtualizes rows),
    /// so as the user scrolled or typed - which re-realizes rows - the column could suddenly shrink to
    /// fit only short values momentarily on screen, clipping the header/filter box with it. A one-time
    /// fixed width sidesteps that entirely, at the cost of not re-fitting itself if a cell is later
    /// edited to much longer text (already an accepted trade-off - see HeaderStyle comment in
    /// MainWindow.xaml for why the native drag-to-resize is also gone).</summary>
    private void RebuildColumns()
    {
        Grid.Columns.Clear();
        ViewModel.ClearQuickFilters();

        var filterHeaderStyle = (Style)Grid.Resources["FilterHeaderStyle"];
        var columns = ViewModel.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            var columnIndex = i;

            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = new FilterHeaderItem(columns[i].Name, text => ViewModel.SetQuickFilter(columnIndex, text)),
                HeaderStyle = filterHeaderStyle,
                Width = new DataGridLength(EstimateColumnWidth(columnIndex, columns[i].Name), DataGridLengthUnitType.Pixel),
                Binding = new Binding
                {
                    Path = new PropertyPath($"[{i}]"),
                    Mode = BindingMode.OneWay,
                },
            });
        }
    }

    /// <summary>Widest of the column name and a sample of its cell values (measured the same way their
    /// TextBlocks would render, so plain character counts don't mislead), clamped to a sane range so
    /// one long outlier cell or a one-letter header can't make the grid unusable.</summary>
    private double EstimateColumnWidth(int columnIndex, string headerName)
    {
        var maxWidth = EstimateTextWidth(headerName);
        var sampled = 0;
        foreach (var row in ViewModel.ViewRows)
        {
            if (sampled++ >= 100)
            {
                break;
            }

            var cellWidth = EstimateTextWidth(row.GetCell(columnIndex));
            if (cellWidth > maxWidth)
            {
                maxWidth = cellWidth;
            }
        }

        return Math.Clamp(maxWidth + 24, 100, 400);
    }

    private static double EstimateTextWidth(string text)
    {
        var measuring = new TextBlock { Text = text };
        measuring.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return measuring.DesiredSize.Width;
    }

    /// <summary>The header's own template does not stretch the filter TextBox to the column's full
    /// width (see comment in MainWindow.xaml), so this sets it directly from the realized
    /// DataGridColumnHeader ancestor's ActualWidth, and keeps tracking it via SizeChanged in case the
    /// header is re-measured after this TextBox's own Loaded already fired (e.g. window resize).</summary>
    private void FilterTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        var textBox = (TextBox)sender;
        if (FindAncestor<DataGridColumnHeader>(textBox) is not { } header)
        {
            return;
        }

        SetFilterBoxWidth(textBox, header.ActualWidth);
        header.SizeChanged += (_, args) => SetFilterBoxWidth(textBox, args.NewSize.Width);
    }

    private static void SetFilterBoxWidth(TextBox textBox, double headerWidth) =>
        textBox.Width = Math.Max(0, headerWidth - 4);

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(element);
        while (parent is not null && parent is not T)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        return parent as T;
    }

    /// <summary>The clear button's DataContext is the same FilterHeaderItem as its sibling TextBox
    /// (both come from the same DataTemplate instance) - setting FilterText here clears both the
    /// ViewModel-side filter and, via FilterHeaderItem's PropertyChanged, the TextBox's own text.</summary>
    private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FilterHeaderItem item)
        {
            item.FilterText = string.Empty;
        }
    }

    private void ClearAllFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var column in Grid.Columns)
        {
            if (column.Header is FilterHeaderItem item)
            {
                item.FilterText = string.Empty;
            }
        }
    }

    private void Grid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        if (e.Column.GetCellContent(e.Row) is not TextBox textBox)
        {
            return;
        }

        var viewRowIndex = e.Row.GetIndex();
        var columnIndex = Grid.Columns.IndexOf(e.Column);
        ViewModel.SetCell(viewRowIndex, columnIndex, textBox.Text);
    }

    // ----- Fill handle (Excel-style drag-to-fill) -----

    private void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        _gridScrollViewer ??= FindDescendants<ScrollViewer>(Grid).FirstOrDefault();
        if (_gridScrollViewer is not null)
        {
            _gridScrollViewer.ViewChanged += (_, _) => UpdateFillHandlePosition();
        }
    }

    private void Grid_CurrentCellChanged(object? sender, object e) => UpdateFillHandlePosition();

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFillHandlePosition();

    /// <summary>Repositions the handle onto the bottom-right corner of the current cell, or hides it
    /// when there is no valid current cell or its row isn't realized on screen right now (DataGrid
    /// virtualizes rows, so <see cref="DataGrid.GetRowFromItem"/> returns null once a row scrolls out
    /// of view).</summary>
    private void UpdateFillHandlePosition()
    {
        if (_isFillDragging)
        {
            return;
        }

        FillHandle.Visibility = Visibility.Collapsed;

        var rowIndex = Grid.SelectedIndex;
        var column = Grid.CurrentColumn;
        if (rowIndex < 0 || rowIndex >= ViewModel.ViewRows.Count || column is null)
        {
            return;
        }

        if (GetCellRect(rowIndex, column) is not { } rect || rect.Bottom < 0 || rect.Top > FillHandleLayer.ActualHeight)
        {
            return;
        }

        Canvas.SetLeft(FillHandle, rect.Right - (FillHandle.Width / 2));
        Canvas.SetTop(FillHandle, rect.Bottom - (FillHandle.Height / 2));
        FillHandle.Visibility = Visibility.Visible;
    }

    /// <summary>The realized cell's bounds, translated into <see cref="FillHandleLayer"/>'s coordinate
    /// space - null if the row isn't currently realized (scrolled out of view). Height comes from the
    /// row container, not the cell's content element (a TextBlock/TextBox that's normally vertically
    /// centered and shorter than the row) - using the content element's own height would put the
    /// corner mid-cell instead of on the actual gridline. Width/X still come from the content element:
    /// unlike <see cref="DataGridColumn.ActualWidth"/>, that matches what's actually rendered.</summary>
    private Rect? GetCellRect(int viewRowIndex, DataGridColumn column)
    {
        if (viewRowIndex < 0 || viewRowIndex >= ViewModel.ViewRows.Count)
        {
            return null;
        }

        if (FindRowContainer(viewRowIndex) is not { } container || column.GetCellContent(container) is not FrameworkElement cellContent)
        {
            return null;
        }

        var rowTop = container.TransformToVisual(FillHandleLayer).TransformPoint(new Point(0, 0)).Y;
        var cellTopLeft = cellContent.TransformToVisual(FillHandleLayer).TransformPoint(new Point(0, 0));
        return new Rect(cellTopLeft.X, rowTop, cellContent.ActualWidth, container.ActualHeight);
    }

    /// <summary>Finds the realized <see cref="DataGridRow"/> for a view-row index by walking the visual
    /// tree rather than going through the item (the DataGrid virtualizes rows, so only realized ones
    /// exist as containers at all) - null if that row is currently scrolled out of view.</summary>
    private DataGridRow? FindRowContainer(int viewRowIndex) =>
        FindDescendants<DataGridRow>(Grid).FirstOrDefault(row => row.GetIndex() == viewRowIndex);

    private void FillHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var rowIndex = Grid.SelectedIndex;
        var column = Grid.CurrentColumn;
        if (rowIndex < 0 || rowIndex >= ViewModel.ViewRows.Count || column is null)
        {
            return;
        }

        _isFillDragging = true;
        _fillSourceRowIndex = rowIndex;
        _fillSourceColumnIndex = Grid.Columns.IndexOf(column);
        _fillSourceValue = ViewModel.ViewRows[rowIndex].GetCell(_fillSourceColumnIndex);
        _fillTargetRowIndex = rowIndex;

        FillHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void FillHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isFillDragging)
        {
            return;
        }

        // FindElementsInHostCoordinates needs the point in the *window's* coordinate space, not the
        // Grid's - GetCurrentPoint(null) is what returns that (GetCurrentPoint(Grid) would silently
        // hit-test the wrong location and this drag would never register a target row).
        var position = e.GetCurrentPoint(null).Position;
        if (FindRowIndexAtPoint(position) is { } hoveredRowIndex)
        {
            _fillTargetRowIndex = Math.Max(_fillSourceRowIndex, hoveredRowIndex);
            UpdateFillPreview();
        }

        e.Handled = true;
    }

    private void FillHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isFillDragging)
        {
            return;
        }

        // Order matters: ReleasePointerCapture() synchronously raises PointerCaptureLost, whose handler
        // resets all the drag state below - calling it first would wipe out the source/target indices
        // before CompleteFillDrag gets to read them.
        CompleteFillDrag(e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control));
        FillHandle.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void FillHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isFillDragging)
        {
            ResetFillDragState();
        }
    }

    /// <summary>Hit-tests for the <see cref="DataGridRow"/> under a point (in the app window's
    /// coordinate space - see the caller) rather than computing a row index from pixel offsets: robust
    /// regardless of per-row height and unaffected by virtualization/scroll position, since it only
    /// asks about whatever is actually rendered at that point right now.</summary>
    private int? FindRowIndexAtPoint(Point pointInWindow)
    {
        foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(pointInWindow, Grid))
        {
            if (FindAncestorOrSelf<DataGridRow>(element) is { } row)
            {
                return row.GetIndex();
            }
        }

        return null;
    }

    private void UpdateFillPreview()
    {
        var column = Grid.Columns[_fillSourceColumnIndex];
        if (GetCellRect(_fillSourceRowIndex, column) is not { } start || GetCellRect(_fillTargetRowIndex, column) is not { } end)
        {
            FillPreviewBorder.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(FillPreviewBorder, start.X);
        Canvas.SetTop(FillPreviewBorder, start.Y);
        FillPreviewBorder.Width = start.Width;
        FillPreviewBorder.Height = Math.Max(0, end.Bottom - start.Top);
        FillPreviewBorder.Visibility = Visibility.Visible;
    }

    /// <summary>Applies the drag: rows below the source get the source cell's value, or - holding Ctrl,
    /// and only when that value parses as an integer - the value plus their offset from the source row
    /// (5001, 5002, 5003, ...). Routed through <see cref="ModuleFViewModel.PasteBlock"/> so the whole
    /// fill is one undoable command, same as an actual paste.</summary>
    private void CompleteFillDrag(bool incrementNumeric)
    {
        var sourceRowIndex = _fillSourceRowIndex;
        var columnIndex = _fillSourceColumnIndex;
        var targetRowIndex = _fillTargetRowIndex;
        var sourceValue = _fillSourceValue;

        ResetFillDragState();

        if (targetRowIndex <= sourceRowIndex)
        {
            return;
        }

        var baseNumber = BigInteger.Zero;
        var canIncrement = incrementNumeric && BigInteger.TryParse(sourceValue, out baseNumber);
        var rowCount = targetRowIndex - sourceRowIndex;
        var block = new List<IReadOnlyList<string>>(rowCount);
        for (var offset = 1; offset <= rowCount; offset++)
        {
            block.Add(new[] { canIncrement ? (baseNumber + offset).ToString() : sourceValue });
        }

        ViewModel.PasteBlock(sourceRowIndex + 1, columnIndex, block);
    }

    private void ResetFillDragState()
    {
        _isFillDragging = false;
        _fillSourceRowIndex = -1;
        _fillSourceColumnIndex = -1;
        _fillSourceValue = string.Empty;
        _fillTargetRowIndex = -1;
        FillPreviewBorder.Visibility = Visibility.Collapsed;
        UpdateFillHandlePosition();
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject element) where T : DependencyObject =>
        element as T ?? FindAncestor<T>(element);

    // ----- Toolbar: Open/Save -----

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".tsv");
        picker.FileTypeFilter.Add(".txt");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        _currentFilePath = file.Path;
        _currentOperationCts = new CancellationTokenSource();
        await ViewModel.OpenAsync(file.Path, ConfirmLargeFileAsync, _currentOperationCts.Token);
    }

    /// <summary>Status bar "Encoding"/"Delimiter" labels are tappable - re-opens the same file with a
    /// manually chosen encoding/delimiter when auto-detection guessed wrong.</summary>
    private async void EncodingLabel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_currentFilePath is null)
        {
            return;
        }

        var dialog = new EncodingPickerDialog { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _currentOperationCts = new CancellationTokenSource();
        await ViewModel.OpenAsync(_currentFilePath, dialog.SelectedEncoding, dialog.SelectedDelimiter, ConfirmLargeFileAsync, _currentOperationCts.Token);
    }

    private async Task<bool> ConfirmLargeFileAsync(long estimatedRows)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "File rất lớn",
            Content = $"Ước tính khoảng {estimatedRows:N0} dòng - có thể vượt RAM khả dụng khi nạp toàn bộ. Vẫn mở?",
            PrimaryButtonText = "Vẫn mở",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _currentOperationCts = new CancellationTokenSource();
        try
        {
            await ViewModel.SaveAsync(_currentOperationCts.Token);
        }
        catch (InvalidOperationException)
        {
            SaveAsButton_Click(sender, e);
        }
    }

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        picker.FileTypeChoices.Add("TSV", new List<string> { ".tsv" });
        picker.SuggestedFileName = "data";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        _currentOperationCts = new CancellationTokenSource();
        await ViewModel.SaveAsAsync(file.Path, _currentOperationCts.Token);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _currentOperationCts?.Cancel();

    // ----- Toolbar: rows/columns -----

    private void AddRowButton_Click(object sender, RoutedEventArgs e)
    {
        int? anchor = Grid.SelectedIndex >= 0 ? Grid.SelectedIndex : null;
        SelectRow(ViewModel.AddRow(anchor));
    }

    private void DeleteRowButton_Click(object sender, RoutedEventArgs e) => DeleteSelectedRows();

    private void DuplicateRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedIndex >= 0)
        {
            SelectRow(ViewModel.DuplicateRow(Grid.SelectedIndex));
        }
    }

    private void DeleteSelectedRows()
    {
        var indexes = Grid.SelectedItems.Cast<CsvRow>().Select(row => ViewModel.ViewRows.IndexOf(row)).Where(i => i >= 0).ToList();
        if (indexes.Count > 0)
        {
            SelectRow(ViewModel.RemoveRows(indexes));
        }
    }

    /// <summary>RebuildView() (called by AddRow/DuplicateRow/RemoveRows) clears and repopulates
    /// ViewRows, which drops the DataGrid's selection/focus - this puts it back on the row the
    /// operation cares about (the new row, the duplicate, or whatever slid into a deleted row's
    /// place) so the user doesn't lose their place after every edit.</summary>
    private void SelectRow(CsvRow? row)
    {
        if (row is null)
        {
            return;
        }

        var index = ViewModel.ViewRows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        Grid.SelectedIndex = index;
        Grid.ScrollIntoView(row, null);
    }

    private async void AddColumnButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColumnDialog("Thêm cột") { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ColumnName))
        {
            ViewModel.AddColumn(ViewModel.Columns.Count, dialog.ColumnName);
        }
    }

    private async void DeleteColumnButton_Click(object sender, RoutedEventArgs e)
    {
        var columnIndex = Grid.CurrentColumn is { } currentColumn ? Grid.Columns.IndexOf(currentColumn) : -1;
        if (columnIndex < 0)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Xóa cột",
            Content = $"Xóa cột '{ViewModel.Columns[columnIndex].Name}'?",
            PrimaryButtonText = "Xóa",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.RemoveColumn(columnIndex);
        }
    }

    private async void RenameColumnButton_Click(object sender, RoutedEventArgs e)
    {
        var columnIndex = Grid.CurrentColumn is { } currentColumn ? Grid.Columns.IndexOf(currentColumn) : -1;
        if (columnIndex < 0)
        {
            return;
        }

        var dialog = new ColumnDialog("Đổi tên cột", ViewModel.Columns[columnIndex].Name) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ColumnName))
        {
            ViewModel.RenameColumn(columnIndex, dialog.ColumnName);
        }
    }

    // ----- Toolbar: find/filter/sort/statistics -----

    private async void FindButton_Click(object sender, RoutedEventArgs e) => await ShowFindReplaceDialogAsync();

    private async void ReplaceButton_Click(object sender, RoutedEventArgs e) => await ShowFindReplaceDialogAsync();

    private async Task ShowFindReplaceDialogAsync()
    {
        var dialog = new FindReplaceDialog(ViewModel) { XamlRoot = Content.XamlRoot };
        dialog.MatchNavigated += (_, match) => NavigateToCell(match);
        await dialog.ShowAsync();
    }

    private void NavigateToCell(CellRef cell)
    {
        if (cell.RowIndex < 0 || cell.RowIndex >= ViewModel.ViewRows.Count)
        {
            return;
        }

        // CommunityToolkit.WinUI.UI.Controls.DataGrid 7.1.2 has no public ScrollIntoView(item, column)
        // overload - selecting the row/column is enough to highlight the match; the grid does not
        // reliably auto-scroll to it for very large result sets (documented limitation in PLAN.md).
        var row = ViewModel.ViewRows[cell.RowIndex];
        Grid.SelectedItem = row;
        if (cell.ColumnIndex < Grid.Columns.Count)
        {
            Grid.CurrentColumn = Grid.Columns[cell.ColumnIndex];
        }
    }

    private async void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FilterDialog(ViewModel.Columns, null) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.ApplyFilter(dialog.BuildExpression());
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ViewModel.ApplyFilter(null);
        }
    }

    private async void SortButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SortDialog(ViewModel.Columns, new List<(int, bool)>()) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.ApplySort(dialog.BuildSortSpec());
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ViewModel.ApplySort(new List<(int, bool)>());
        }
    }

    private async void StatisticsButton_Click(object sender, RoutedEventArgs e)
    {
        _currentOperationCts = new CancellationTokenSource();
        var progress = new Progress<int>(p => ViewModel.ProgressPercent = p);
        ViewModel.IsBusy = true;
        try
        {
            var statistics = await ViewModel.ComputeStatisticsAsync(progress, _currentOperationCts.Token);
            var dialog = new StatisticsDialog(statistics) { XamlRoot = Content.XamlRoot };
            await dialog.ShowAsync();
        }
        catch (OperationCanceledException)
        {
            ViewModel.StatusMessage = "Đã hủy tính thống kê.";
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    // ----- Context menu -----

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedRows = Grid.SelectedItems.Cast<CsvRow>().ToList();
        if (selectedRows.Count == 0)
        {
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var row in selectedRows)
        {
            sb.AppendLine(string.Join('\t', Enumerable.Range(0, ViewModel.Columns.Count).Select(row.GetCell)));
        }

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private async void PasteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var clipboardContent = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (!clipboardContent.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            return;
        }

        var text = await clipboardContent.GetTextAsync();
        var block = text.TrimEnd('\r', '\n').Split('\n')
            .Select(line => (IReadOnlyList<string>)line.TrimEnd('\r').Split('\t'))
            .ToList();

        var anchorRowIndex = Grid.SelectedIndex >= 0 ? Grid.SelectedIndex : 0;
        var currentColumnIndex = Grid.CurrentColumn is { } pasteColumn ? Grid.Columns.IndexOf(pasteColumn) : -1;
        var anchorColumnIndex = Math.Max(currentColumnIndex, 0);
        ViewModel.PasteBlock(anchorRowIndex, anchorColumnIndex, block);
    }

    private void DeleteRowsMenuItem_Click(object sender, RoutedEventArgs e) => DeleteSelectedRows();

    private void InsertRowMenuItem_Click(object sender, RoutedEventArgs e) => AddRowButton_Click(sender, e);

    private void DuplicateRowMenuItem_Click(object sender, RoutedEventArgs e) => DuplicateRowButton_Click(sender, e);

    private void IssuesListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IssuesListView.SelectedItem is ValidationIssue { RowIndex: { } rowIndex })
        {
            NavigateToCell(new CellRef(rowIndex, 0));
        }
    }
}
