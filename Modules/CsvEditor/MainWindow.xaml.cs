using System.ComponentModel;
using System.Numerics;
using CommunityToolkit.WinUI.UI.Controls;
using CommunityToolkit.WinUI.UI.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using CsvEditor.Models;
using CsvEditor.Services;
using CsvEditor.ViewModels;
using CsvEditor.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CsvEditor;

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
    /// <summary>File đang mở (đổi theo Save As) - "mở lại" (đổi tiêu đề / encoding) đọc lại đúng file này.</summary>
    private string? CurrentFilePath => ViewModel.CurrentFilePath;

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
        ViewModel = new CsvEditorViewModel(fileService, editService);

        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);

        ViewModel.ColumnsChanged += (_, _) => RebuildColumns();
        ViewModel.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(CsvEditorViewModel.FileName) or nameof(CsvEditorViewModel.IsDirty):
                    UpdateTitle();
                    break;
                case nameof(CsvEditorViewModel.ActiveFilter) or nameof(CsvEditorViewModel.ActiveSort) or nameof(CsvEditorViewModel.RowCount):
                    RefreshChips();
                    break;
                case nameof(CsvEditorViewModel.CanUndo):
                    UpdateRecordPane(); // mọi sửa / hoàn tác / làm lại đều đổi CanUndo → cập nhật giá trị trong khung Xem dọc
                    break;
            }
        };

        // Bấm X khi còn thay đổi chưa lưu: hỏi Lưu / Không lưu / Hủy (trước đây đóng luôn, mất thay đổi).
        AppWindow.Closing += async (_, args) =>
        {
            if (_closeConfirmed || !ViewModel.IsDirty)
            {
                return;
            }
            args.Cancel = true;
            if (await ConfirmSaveChangesAsync("đóng"))
            {
                _closeConfirmed = true;
                Close();
            }
        };

        Closed += (_, _) => _helpWindow?.Close(); // đóng app thì đóng luôn cửa sổ Hướng dẫn

        RebuildColumns();
    }

    public CsvEditorViewModel ViewModel { get; }

    // Hàm cho x:Bind function binding: XAML truyền ViewModel.IsDirty / ViewModel.Issues.Count (đều phát
    // PropertyChanged) nên tự cập nhật, không cần Bindings.Update() chạy lại mọi binding của cửa sổ
    // sau mỗi thay đổi của ViewModel (cách cũ, kèm cảnh báo WMC1506). IsBusy bind thẳng bool → Visibility.
    public Visibility VisibleIfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string IssuesHeaderText(int count) => $"Cảnh báo ({count})";

    public string SaveStateText(bool isDirty) => isDirty ? "● Chưa lưu" : "Đã lưu";

    public string UndoToolTip(string? description) => description is null ? "Hoàn tác (Ctrl+Z)" : $"Hoàn tác: {description} (Ctrl+Z)";

    public string RedoToolTip(string? description) => description is null ? "Làm lại (Ctrl+Y)" : $"Làm lại: {description} (Ctrl+Y)";

    /// <summary>Tiêu đề cửa sổ = tên file (● khi chưa lưu) - nhìn taskbar / Alt+Tab là biết file nào.</summary>
    private void UpdateTitle()
    {
        var name = string.IsNullOrEmpty(ViewModel.FileName) ? null : ViewModel.FileName;
        Title = name is null && !ViewModel.IsDirty
            ? "CsvEditor"
            : $"{(ViewModel.IsDirty ? "● " : string.Empty)}{name ?? "Bảng mới"} - CsvEditor";
    }

    // ----- Màn hình bắt đầu -----

    /// <summary>Bấm "Bảng trống" ở màn hình bắt đầu: ẩn màn hình đó dù chưa mở file.</summary>
    private bool _startDismissed;

    /// <summary>Hiện màn hình bắt đầu khi chưa có file / cột nào (lúc mới mở app), dựng lại danh sách file gần đây.</summary>
    private void UpdateStartScreen()
    {
        var show = !_startDismissed && CurrentFilePath is null && ViewModel.Columns.Count == 0;
        StartScreen.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Grid.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show && IsRecordPaneOpen)
        {
            SetRecordPaneOpen(false);
        }
        if (!show)
        {
            return;
        }

        StartRecentList.Children.Clear();
        var paths = _recentFiles.Paths;
        StartRecentPanel.Visibility = paths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var path in paths)
        {
            var button = new Button
            {
                Tag = path,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6, 8, 6),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new FontIcon { Glyph = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = Path.GetFileName(path), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock
                        {
                            Text = Path.GetDirectoryName(path),
                            Opacity = 0.6,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxWidth = 340,
                        },
                    },
                },
            };
            ToolTipService.SetToolTip(button, path);
            AutomationProperties.SetName(button, Path.GetFileName(path));
            button.Click += RecentItem_Click;
            StartRecentList.Children.Add(button);
        }
    }

    private void NewTableButton_Click(object sender, RoutedEventArgs e)
    {
        _startDismissed = true;
        UpdateStartScreen();
        AddColumnButton_Click(sender, e);
    }

    // ----- Thẻ lọc / sắp xếp đang áp dụng -----

    /// <summary>Hiện thẻ cho lọc nâng cao, ô lọc ở tiêu đề cột, sắp xếp đang áp dụng + "Hiện x / y dòng" khi đang lọc.
    /// Gọi sau mỗi lần đổi lọc / sắp xếp (ViewModel đã dựng lại ViewRows).</summary>
    private void RefreshChips()
    {
        var filter = ViewModel.ActiveFilter;
        FilterChip.Visibility = filter is null ? Visibility.Collapsed : Visibility.Visible;
        if (filter is not null)
        {
            var join = filter.Join == FilterJoin.And ? " và " : " hoặc ";
            SetChipText(FilterChipText, "Lọc: " + string.Join(join, filter.Conditions.Select(DescribeCondition)));
        }

        var quick = Grid.Columns
            .Select(column => column.Header)
            .OfType<FilterHeaderItem>()
            .Where(item => !string.IsNullOrEmpty(item.FilterText))
            .Select(item => $"{item.ColumnName} chứa \"{item.FilterText}\"")
            .ToList();
        QuickFilterChip.Visibility = quick.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SetChipText(QuickFilterChipText, "Ô lọc: " + string.Join(", ", quick));

        var sort = ViewModel.ActiveSort;
        SortChip.Visibility = sort.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SetChipText(SortChipText, "Sắp xếp: " + string.Join(", ", sort.Select(s => $"{ColumnName(s.ColumnIndex)} {(s.Descending ? "↓" : "↑")}")));

        var hidden = Grid.Columns.Where(column => column.Visibility == Visibility.Collapsed).ToList();
        HiddenColumnsChip.Visibility = hidden.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SetChipText(HiddenColumnsChipText, $"Đang ẩn {hidden.Count} cột: "
            + string.Join(", ", hidden.Take(5).Select(column => ((FilterHeaderItem)column.Header).ColumnName))
            + (hidden.Count > 5 ? ", …" : string.Empty));

        var frozen = Grid.FrozenColumnCount;
        FrozenColumnsChip.Visibility = frozen == 0 ? Visibility.Collapsed : Visibility.Visible;
        SetChipText(FrozenColumnsChipText, frozen == 0 ? string.Empty
            : $"Cố định {frozen} cột đầu (đến {ColumnName(frozen - 1)})");

        var filtered = filter is not null || quick.Count > 0;
        ShownRowsText.Text = filtered ? $"Hiện {ViewModel.ViewRows.Count:N0} / {ViewModel.RowCount:N0} dòng" : string.Empty;
        ShownRowsText.Visibility = filtered ? Visibility.Visible : Visibility.Collapsed;
        ChipsBar.Visibility = filtered || sort.Count > 0 || hidden.Count > 0 || frozen > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetChipText(TextBlock textBlock, string text)
    {
        textBlock.Text = text;
        ToolTipService.SetToolTip(textBlock, text);
    }

    private string ColumnName(int columnIndex) =>
        columnIndex >= 0 && columnIndex < ViewModel.Columns.Count ? ViewModel.Columns[columnIndex].Name : $"Cột {columnIndex + 1}";

    private string DescribeCondition(FilterCondition condition)
    {
        var op = condition.Operator switch
        {
            FilterOperator.Equal => "=",
            FilterOperator.NotEqual => "≠",
            FilterOperator.GreaterThan => ">",
            FilterOperator.LessThan => "<",
            FilterOperator.GreaterOrEqual => "≥",
            FilterOperator.LessOrEqual => "≤",
            FilterOperator.Contains => "chứa",
            FilterOperator.Regex => "khớp",
            _ => "?",
        };
        var text = $"{ColumnName(condition.ColumnIndex)} {op} \"{condition.Value}\"";
        return condition.Negate ? $"không ({text})" : text;
    }

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

        // Ẩn / cố định cột là cách xem của từng file: mở file khác thì bỏ; thêm / xoá / đổi tên cột, mở lại cùng file (đổi
        // encoding...) thì giữ - ẩn theo tên cột (vị trí dịch đi khi thêm / xoá cột), cố định theo số cột đầu.
        if (!string.Equals(_columnViewPath, CurrentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            _columnViewPath = CurrentFilePath;
            _hiddenColumnNames.Clear();
            _frozenColumnCount = 0;
        }

        var filterHeaderStyle = (Style)Grid.Resources["FilterHeaderStyle"];
        var columns = ViewModel.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            var columnIndex = i;

            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = new FilterHeaderItem(columns[i].Name, text =>
                {
                    ViewModel.SetQuickFilter(columnIndex, text);
                    RefreshChips();
                }),
                HeaderStyle = filterHeaderStyle,
                Visibility = _hiddenColumnNames.Contains(columns[i].Name) ? Visibility.Collapsed : Visibility.Visible,
                Width = new DataGridLength(EstimateColumnWidth(columnIndex, columns[i].Name), DataGridLengthUnitType.Pixel),
                Binding = new Binding
                {
                    Path = new PropertyPath($"[{i}]"),
                    Mode = BindingMode.OneWay,
                },
            });
        }

        Grid.FrozenColumnCount = Math.Min(_frozenColumnCount, columns.Count);
        RefreshChips();
        UpdateStartScreen();
        UpdateRecordPane(rebuild: true);
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

    /// <summary>Template tiêu đề cột của Toolkit (đo trong Sandbox): nội dung nằm trong Grid 2 cột - `*` cho nội dung,
    /// `Auto` MinWidth 32 cho mũi tên sắp xếp, chừa chỗ cả khi không sắp xếp bằng bấm tiêu đề (CanUserSortColumns=False) -
    /// và ContentPresenter căn trái (chỉ rộng bằng nội dung). Hậu quả: ô lọc chỉ rộng bằng MinWidth 64 của TextBox, hụt so
    /// với cột 1 đoạn, tên cột bị cắt sớm, nút ✕ xoá ô lọc rơi vào phần bị cắt. Bỏ cột mũi tên + giãn ContentPresenter (giá
    /// trị đặt trực tiếp thắng giá trị của template) để tên cột và ô lọc lấp đầy cột, kể cả khi đổi cỡ.</summary>
    private void FilterTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (FindAncestor<ContentPresenter>((TextBox)sender) is not { } presenter)
        {
            return;
        }

        presenter.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (VisualTreeHelper.GetParent(presenter) is Microsoft.UI.Xaml.Controls.Grid { ColumnDefinitions.Count: 2 } headerGrid)
        {
            headerGrid.ColumnDefinitions[1].MinWidth = 0;
            headerGrid.ColumnDefinitions[1].Width = new GridLength(0);
        }
    }

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

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFillHandlePosition();
        UpdateRecordPane();
    }

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
    /// (5001, 5002, 5003, ...). Routed through <see cref="CsvEditorViewModel.PasteBlock"/> so the whole
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
        if (!await ConfirmSaveChangesAsync("mở file khác"))
        {
            return;
        }

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

        await OpenPathAsync(file.Path);
    }

    private static readonly string[] OpenableExtensions = [".csv", ".tsv", ".txt"];

    private readonly RecentFilesStore _recentFiles = new();

    /// <summary>Mở <paramref name="filePath"/> (bên gọi đã hỏi lưu thay đổi), ghi vào danh sách gần đây. Lỗi đọc file
    /// (không tồn tại, bị khoá, không có quyền...) báo hộp thoại thay vì văng app.</summary>
    private Task<bool> OpenPathAsync(string filePath) =>
        RunOpenAsync(filePath, ct => ViewModel.OpenAsync(filePath, ConfirmLargeFileAsync, ct));

    private async Task<bool> RunOpenAsync(string filePath, Func<CancellationToken, Task<bool>> open)
    {
        _currentOperationCts = new CancellationTokenSource();
        try
        {
            if (!await open(_currentOperationCts.Token))
            {
                return false;
            }
            _recentFiles.Add(filePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.StatusMessage = $"Không mở được {Path.GetFileName(filePath)}: {ex.Message}";
            await new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Không mở được file",
                Content = SharedUI.Helpers.DialogContent.Scrollable($"{filePath}\n\n{ex.Message}"),
                CloseButtonText = "Đóng",
            }.ShowAsync();
            return false;
        }
    }

    // ----- Kéo-thả file vào cửa sổ -----

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Mở file";
        }
    }

    /// <summary>Thả 1 hoặc nhiều file: mở file .csv/.tsv/.txt đầu tiên (Editor chỉ giữ 1 file mỗi lúc). Lấy danh sách
    /// file xong mới hỏi lưu / mở - hiện hộp thoại khi thao tác thả chưa kết thúc dễ treo Explorer bên kia.</summary>
    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem> items;
        var deferral = e.GetDeferral();
        try
        {
            items = await e.DataView.GetStorageItemsAsync();
        }
        finally
        {
            deferral.Complete();
        }

        var files = items.OfType<IStorageFile>().Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        var path = files.FirstOrDefault(p => OpenableExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));
        if (path is null)
        {
            ViewModel.StatusMessage = "Chỉ mở được file .csv, .tsv, .txt.";
            return;
        }

        if (!await ConfirmSaveChangesAsync("mở file khác"))
        {
            return;
        }
        if (await OpenPathAsync(path) && files.Count > 1)
        {
            ViewModel.StatusMessage += $" (thả {files.Count} file - chỉ mở file đầu tiên)";
        }
    }

    // ----- File gần đây -----

    /// <summary>Dựng lại menu mỗi lần mở: tên file (trùng tên thì kèm thư mục), di chuột thấy đường dẫn đầy đủ.</summary>
    private void RecentMenu_Opening(object sender, object e)
    {
        RecentMenu.Items.Clear();
        var paths = _recentFiles.Paths;
        if (paths.Count == 0)
        {
            RecentMenu.Items.Add(new MenuFlyoutItem { Text = "(chưa có file nào)", IsEnabled = false });
            return;
        }

        var duplicateNames = paths.GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            var name = Path.GetFileName(path);
            if (duplicateNames.Contains(name))
            {
                name += $"  ({Path.GetFileName(Path.GetDirectoryName(path))})";
            }
            var item = new MenuFlyoutItem { Text = $"{i + 1}. {name}", Tag = path };
            ToolTipService.SetToolTip(item, path);
            item.Click += RecentItem_Click;
            RecentMenu.Items.Add(item);
        }
        RecentMenu.Items.Add(new MenuFlyoutSeparator());
        var clear = new MenuFlyoutItem { Text = "Xoá danh sách", Icon = new FontIcon { Glyph = "" } };
        clear.Click += (_, _) => _recentFiles.Clear();
        RecentMenu.Items.Add(clear);
    }

    private async void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string path)
        {
            return;
        }
        if (!File.Exists(path))
        {
            _recentFiles.Remove(path);
            UpdateStartScreen();
            ViewModel.StatusMessage = $"Không còn file {Path.GetFileName(path)} - đã bỏ khỏi danh sách gần đây.";
            await new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Không tìm thấy file",
                Content = $"{path}\n\nFile đã bị xoá, đổi tên hoặc chuyển chỗ - đã bỏ khỏi danh sách gần đây.",
                CloseButtonText = "Đóng",
            }.ShowAsync();
            return;
        }
        if (await ConfirmSaveChangesAsync("mở file khác"))
        {
            await OpenPathAsync(path);
        }
    }

    /// <summary>"Dòng đầu là tiêu đề": đổi lựa chọn khi đang mở file → mở lại file đó (giữ encoding / delimiter). Có
    /// thay đổi chưa lưu thì hỏi trước; huỷ thì trả checkbox về như cũ.</summary>
    private async void HasHeaderBox_Click(object sender, RoutedEventArgs e)
    {
        var hasHeader = HasHeaderBox.IsChecked == true;
        if (CurrentFilePath is not { } filePath)
        {
            ViewModel.HasHeader = hasHeader; // chưa mở file: áp dụng cho lần mở tới
            return;
        }

        if (!await ConfirmSaveChangesAsync("mở lại file"))
        {
            HasHeaderBox.IsChecked = !hasHeader;
            return;
        }

        ViewModel.HasHeader = hasHeader;
        await RunOpenAsync(filePath, ct => ViewModel.ReopenAsync(filePath, ConfirmLargeFileAsync, ct));
    }

    /// <summary>Status bar "Encoding"/"Delimiter" labels are tappable - re-opens the same file with a
    /// manually chosen encoding/delimiter when auto-detection guessed wrong.</summary>
    private async void EncodingLabel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (CurrentFilePath is not { } filePath)
        {
            return;
        }

        var dialog = new EncodingPickerDialog { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!await ConfirmSaveChangesAsync("mở lại file"))
        {
            return;
        }

        await RunOpenAsync(filePath, ct => ViewModel.OpenAsync(filePath, dialog.SelectedEncoding, dialog.SelectedDelimiter, ConfirmLargeFileAsync, ct));
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

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveCurrentAsync();

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e) => await RunSaveAsync(SaveAsWithPickerAsync);

    /// <summary>Lưu file đang mở (chưa có đường dẫn → Save As). True nếu đã lưu xong.</summary>
    private Task<bool> SaveCurrentAsync() => RunSaveAsync(async () =>
    {
        if (CurrentFilePath is null)
        {
            return await SaveAsWithPickerAsync();
        }
        await ViewModel.SaveAsync(_currentOperationCts!.Token);
        return true;
    });

    /// <summary>Chọn file rồi lưu. False nếu người dùng huỷ hộp thoại chọn file.</summary>
    private async Task<bool> SaveAsWithPickerAsync()
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        picker.FileTypeChoices.Add("TSV", new List<string> { ".tsv" });
        picker.SuggestedFileName = "data";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return false;
        }

        await ViewModel.SaveAsAsync(file.Path, _currentOperationCts!.Token);
        _recentFiles.Add(file.Path);
        return true;
    }

    /// <summary>Xuất các dòng đang hiện ra .xlsx (tên gợi ý = tên file đang mở). Lỗi ghi (file đang mở trong Excel...)
    /// báo hộp thoại như khi lưu.</summary>
    private async void ExportXlsxButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Columns.Count == 0)
        {
            ViewModel.StatusMessage = "Chưa có dữ liệu để xuất.";
            return;
        }

        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeChoices.Add("Excel", new List<string> { ".xlsx" });
        picker.SuggestedFileName = CurrentFilePath is { } current ? Path.GetFileNameWithoutExtension(current) : "data";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await RunSaveAsync(async () =>
        {
            await ViewModel.ExportXlsxAsync(file.Path, _currentOperationCts!.Token);
            return true;
        });
    }

    /// <summary>Chạy 1 lần lưu, báo lỗi (file đang bị app khác khoá, hết chỗ...) thay vì văng app. True nếu đã lưu.</summary>
    private async Task<bool> RunSaveAsync(Func<Task<bool>> save)
    {
        _currentOperationCts = new CancellationTokenSource();
        try
        {
            return await save();
        }
        catch (OperationCanceledException)
        {
            ViewModel.StatusMessage = "Đã hủy lưu.";
            return false;
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Không lưu được: {ex.Message}";
            await new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Không lưu được file",
                Content = SharedUI.Helpers.DialogContent.Scrollable(ex.Message),
                CloseButtonText = "Đóng",
            }.ShowAsync();
            return false;
        }
    }

    private bool _closeConfirmed;
    private bool _savePromptOpen;

    /// <summary>Còn thay đổi chưa lưu trước khi <paramref name="action"/> (đóng, mở file khác, mở lại file): hỏi Lưu /
    /// Không lưu / Hủy. True = tiếp tục (đã lưu xong hoặc chọn bỏ thay đổi); false = dừng lại (Hủy, huỷ Save As, lưu
    /// lỗi). Không có thay đổi thì không hỏi.</summary>
    private async Task<bool> ConfirmSaveChangesAsync(string action)
    {
        if (!ViewModel.IsDirty)
        {
            return true;
        }
        if (_savePromptOpen)
        {
            return false; // đang hỏi rồi (vd bấm X 2 lần) - 1 XamlRoot chỉ hiện được 1 ContentDialog
        }
        _savePromptOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Lưu thay đổi?",
                Content = $"{ViewModel.FileName} có thay đổi chưa lưu. Lưu trước khi {action}?",
                PrimaryButtonText = "Lưu",
                SecondaryButtonText = "Không lưu",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Primary,
            };
            return await dialog.ShowAsync() switch
            {
                ContentDialogResult.Primary => await SaveCurrentAsync(),
                ContentDialogResult.Secondary => true,
                _ => false,
            };
        }
        finally
        {
            _savePromptOpen = false;
        }
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

    private async void AddColumnButton_Click(object sender, RoutedEventArgs e) => await AddColumnAsync(ViewModel.Columns.Count);

    private async Task AddColumnAsync(int index)
    {
        var dialog = new ColumnDialog("Thêm cột") { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ColumnName))
        {
            ViewModel.AddColumn(index, dialog.ColumnName);
        }
    }

    /// <summary>Cột của ô đang chọn (Cột ▾ trên thanh công cụ); chưa chọn ô nào thì báo ở thanh trạng thái, trả -1.</summary>
    private int CurrentColumnIndexOrHint(string action)
    {
        var columnIndex = Grid.CurrentColumn is { } currentColumn ? Grid.Columns.IndexOf(currentColumn) : -1;
        if (columnIndex < 0)
        {
            ViewModel.StatusMessage = $"Chọn 1 ô trong cột cần {action} trước (hoặc chuột phải vào tiêu đề cột).";
        }
        return columnIndex;
    }

    private async void DeleteColumnButton_Click(object sender, RoutedEventArgs e) => await DeleteColumnAsync(CurrentColumnIndexOrHint("xoá"));

    private async void RenameColumnButton_Click(object sender, RoutedEventArgs e) => await RenameColumnAsync(CurrentColumnIndexOrHint("đổi tên"));

    private async Task DeleteColumnAsync(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= ViewModel.Columns.Count)
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

    private async Task RenameColumnAsync(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= ViewModel.Columns.Count)
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

    /// <summary>Ctrl+G: đi tới dòng số N theo số ở đầu dòng của bảng (tức là theo lọc / sắp xếp đang hiện), giữ cột
    /// đang chọn. Enter = Đi tới; số ngoài phạm vi thì báo ngay trong hộp thoại, không đóng.</summary>
    private async void GoToButton_Click(object sender, RoutedEventArgs e)
    {
        var rowCount = ViewModel.ViewRows.Count;
        if (rowCount == 0)
        {
            ViewModel.StatusMessage = "Không có dòng nào để đi tới.";
            return;
        }

        var input = new TextBox
        {
            Header = $"Số dòng (1 – {rowCount:N0})",
            Text = Grid.SelectedIndex >= 0 ? (Grid.SelectedIndex + 1).ToString() : string.Empty,
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } },
        };
        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Đi tới dòng",
            Content = new StackPanel { Spacing = 8, MinWidth = 280, Children = { input, error } },
            PrimaryButtonText = "Đi tới",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Primary,
        };

        int? target = null;
        bool TryAccept()
        {
            var text = input.Text.Trim().Replace(".", string.Empty).Replace(",", string.Empty);
            if (int.TryParse(text, out var line) && line >= 1 && line <= rowCount)
            {
                target = line;
                return true;
            }
            error.Text = $"Nhập số từ 1 đến {rowCount:N0}.";
            input.SelectAll();
            return false;
        }

        dialog.PrimaryButtonClick += (_, args) => args.Cancel = !TryAccept();
        input.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                args.Handled = true;
                if (TryAccept())
                {
                    dialog.Hide();
                }
            }
        };
        dialog.Opened += (_, _) =>
        {
            input.Focus(FocusState.Programmatic);
            input.SelectAll();
        };

        await dialog.ShowAsync();
        if (target is not { } lineNumber)
        {
            return;
        }

        var columnIndex = Grid.CurrentColumn is { } column ? Grid.Columns.IndexOf(column) : 0;
        NavigateToCell(new CellRef(lineNumber - 1, Math.Max(columnIndex, 0)));
        Grid.Focus(FocusState.Programmatic);
        ViewModel.StatusMessage = $"Dòng {lineNumber:N0}/{rowCount:N0}.";
    }

    /// <summary>1 gợi ý trong hộp thoại Đi tới cột - AutoSuggestBox hiện (và điền vào ô) bằng ToString.</summary>
    private sealed record ColumnChoice(int Index, string Name)
    {
        public override string ToString() => $"{Index + 1}. {Name}";
    }

    private void GoToColumnAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        GoToColumn_Click(sender, new RoutedEventArgs());
    }

    /// <summary>Ctrl+Shift+G / Cột ▾ → Đi tới cột: gõ 1 phần tên cột (hoặc số thứ tự), chọn trong gợi ý hoặc Enter = cột khớp
    /// nhất (<see cref="ColumnFinder"/>) → cuộn ngang tới cột đó, giữ dòng đang chọn. Không khớp thì báo ngay trong hộp thoại.</summary>
    private async void GoToColumn_Click(object sender, RoutedEventArgs e)
    {
        var columns = ViewModel.Columns;
        if (columns.Count == 0)
        {
            ViewModel.StatusMessage = "Chưa có cột nào để đi tới.";
            return;
        }

        var box = new AutoSuggestBox
        {
            Header = $"Tên cột (gõ 1 phần) hoặc số thứ tự cột (1 – {columns.Count:N0})",
            PlaceholderText = "vd: EXT_DATA",
            MinWidth = 340,
        };
        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Đi tới cột",
            Content = new StackPanel { Spacing = 8, Children = { box, error } },
            PrimaryButtonText = "Đi tới",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Primary,
        };

        ColumnChoice? chosen = null;
        int? target = null;
        var otherMatches = 0;
        bool TryAccept()
        {
            if (chosen is not null && box.Text == chosen.ToString())
            {
                target = chosen.Index;
                otherMatches = 0;
                return true;
            }
            var matches = ColumnFinder.Find(columns, box.Text);
            if (matches.Count > 0)
            {
                target = matches[0];
                otherMatches = matches.Count - 1;
                return true;
            }
            error.Text = box.Text.Trim().Length == 0 ? "Gõ tên cột hoặc số thứ tự cột." : $"Không có cột nào tên chứa \"{box.Text.Trim()}\".";
            return false;
        }

        box.TextChanged += (_, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                error.Text = string.Empty;
                box.ItemsSource = ColumnFinder.Find(columns, box.Text).Take(30).Select(i => new ColumnChoice(i, columns[i].Name)).ToList();
            }
        };
        box.SuggestionChosen += (_, args) => chosen = args.SelectedItem as ColumnChoice;
        box.QuerySubmitted += (_, args) =>
        {
            if (args.ChosenSuggestion is ColumnChoice choice)
            {
                chosen = choice;
            }
            if (TryAccept())
            {
                dialog.Hide();
            }
        };
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = !TryAccept();
        dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);

        await dialog.ShowAsync();
        if (target is not { } columnIndex)
        {
            return;
        }

        var column = Grid.Columns[columnIndex];
        if (column.Visibility == Visibility.Collapsed)
        {
            SetColumnsHidden(new HashSet<string>(_hiddenColumnNames.Where(name => name != columns[columnIndex].Name)));
        }
        if (ViewModel.ViewRows.Count > 0)
        {
            NavigateToCell(new CellRef(Math.Max(Grid.SelectedIndex, 0), columnIndex));
            Grid.Focus(FocusState.Programmatic);
        }
        else
        {
            Grid.ScrollIntoView(null, column);
        }
        ViewModel.StatusMessage = $"Cột {columnIndex + 1:N0}/{columns.Count:N0}: {columns[columnIndex].Name}"
            + (otherMatches > 0 ? $" (còn {otherMatches} cột khác khớp - chọn trong gợi ý để tới cột khác)." : ".");
    }

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

        var row = ViewModel.ViewRows[cell.RowIndex];
        Grid.SelectedItem = row;

        var column = cell.ColumnIndex >= 0 && cell.ColumnIndex < Grid.Columns.Count ? Grid.Columns[cell.ColumnIndex] : null;
        if (column is not null)
        {
            Grid.CurrentColumn = column;
        }

        // Scrolls both vertically (to the row) and horizontally (to the column) so the match is
        // actually visible, not just selected - ScrollIntoView(item, column) does exist here despite
        // what an earlier comment claimed (it's used the same way in SelectRow above).
        Grid.ScrollIntoView(row, column);
    }

    private async void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        // Passes the currently applied filter (not null) so re-opening the dialog shows the conditions
        // that are actually active right now, instead of always starting from a blank row.
        var dialog = new FilterDialog(ViewModel.Columns, ViewModel.ActiveFilter) { XamlRoot = Content.XamlRoot };
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

    /// <summary>Clears the active advanced filter without opening the dialog - the dialog itself closes
    /// as soon as you apply a filter, so this is the fast path to undo it afterwards instead of
    /// reopening the dialog just to press "Bỏ lọc" again.</summary>
    private void ClearActiveFilterButton_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyFilter(null);

    private async void SortButton_Click(object sender, RoutedEventArgs e)
    {
        // Passes the currently applied sort (not an empty list) so re-opening the dialog shows the
        // spec that's actually active right now, instead of always starting from a blank row.
        var dialog = new SortDialog(ViewModel.Columns, ViewModel.ActiveSort.ToList()) { XamlRoot = Content.XamlRoot };
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

    /// <summary>Clears the active sort without opening the dialog - same fast path as
    /// <see cref="ClearActiveFilterButton_Click"/>, for the same reason (the dialog closes as soon as
    /// you apply a sort).</summary>
    private void ClearActiveSortButton_Click(object sender, RoutedEventArgs e) => ViewModel.ApplySort(new List<(int, bool)>());

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

    // ----- Hướng dẫn (F1) -----

    private SharedUI.Help.HelpWindow? _helpWindow;

    /// <summary>Mở cửa sổ Hướng dẫn (1 cửa sổ duy nhất - đang mở thì đưa lên trước). Nội dung: Views/HelpContent.</summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is null)
        {
            _helpWindow = new SharedUI.Help.HelpWindow("CsvEditor", "Mở, sửa, lọc, sắp xếp file CSV / TSV và xuất sang Excel.",
                HelpContent.Sections, searchPlaceholder: "Tìm tính năng, vd: lọc, encoding, Ctrl+G...");
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        _helpWindow.Activate();
    }

    // ----- Context menu -----

    /// <summary>Ô / tiêu đề cột vừa bấm chuột phải - các mục menu làm việc trên đó (không phải ô đang chọn trước đó).</summary>
    private int _menuRowIndex = -1;
    private int _menuColumnIndex = -1;

    /// <summary>Chuột phải trong bảng: trên tiêu đề cột → menu cột (sắp xếp, đổi tên, chèn, xoá); trên ô → menu ô (chép,
    /// dán, lọc theo giá trị, dòng). Ô bấm được chọn luôn (ngoài vùng đang chọn) để Dán / Xoá dòng làm đúng chỗ. Shift+F10 /
    /// phím Menu (không có toạ độ) → menu ô cho ô đang chọn.</summary>
    private void Grid_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        DataGridColumnHeader? header = null;
        DataGridCell? cell = null;
        DataGridRow? row = null;
        var hasPoint = args.TryGetPosition(Grid, out var point);
        if (hasPoint)
        {
            var windowPoint = Grid.TransformToVisual(null).TransformPoint(point);
            foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(windowPoint, Grid))
            {
                header ??= FindAncestorOrSelf<DataGridColumnHeader>(element);
                cell ??= FindAncestorOrSelf<DataGridCell>(element);
                row ??= FindAncestorOrSelf<DataGridRow>(element);
            }
        }

        var showOptions = hasPoint ? new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point } : new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions();
        if (header is not null)
        {
            _menuColumnIndex = Grid.Columns.ToList().FindIndex(column => ReferenceEquals(column.Header, header.Content));
            if (_menuColumnIndex < 0)
            {
                return;
            }
            var headerMenu = (MenuFlyout)RootGrid.Resources["HeaderMenu"];
            MenuItem(headerMenu, "ClearColumnFilter").IsEnabled =
                Grid.Columns[_menuColumnIndex].Header is FilterHeaderItem { FilterText.Length: > 0 };
            MenuItem(headerMenu, "FreezeTo").Text = _menuColumnIndex == 0
                ? "Cố định cột này (cột đầu)"
                : $"Cố định cột 1 – {_menuColumnIndex + 1} (đến cột này)";
            MenuItem(headerMenu, "Unfreeze").IsEnabled = Grid.FrozenColumnCount > 0;
            headerMenu.ShowAt(Grid, showOptions);
            return;
        }

        if (row is not null)
        {
            _menuRowIndex = row.GetIndex();
            _menuColumnIndex = cell is null ? -1 : Grid.Columns.ToList().FindIndex(column => column.GetCellContent(row) is { } content
                && FindAncestor<DataGridCell>(content) == cell);
            if (row.DataContext is CsvRow item && !Grid.SelectedItems.Contains(item))
            {
                Grid.SelectedItem = item;
            }
            if (_menuColumnIndex >= 0)
            {
                Grid.CurrentColumn = Grid.Columns[_menuColumnIndex];
            }
        }
        else if (hasPoint)
        {
            return; // vùng trống / tiêu đề dòng ngoài bảng
        }
        else
        {
            _menuRowIndex = Grid.SelectedIndex;
            _menuColumnIndex = Grid.CurrentColumn is { } current ? Grid.Columns.IndexOf(current) : -1;
        }

        var cellMenu = (MenuFlyout)RootGrid.Resources["CellMenu"];
        var hasCell = _menuRowIndex >= 0 && _menuRowIndex < ViewModel.ViewRows.Count && _menuColumnIndex >= 0;
        var value = hasCell ? ViewModel.ViewRows[_menuRowIndex].GetCell(_menuColumnIndex) : string.Empty;
        var shown = value.Length > 24 ? value[..24] + "…" : value;
        var filterBy = MenuItem(cellMenu, "FilterByValue");
        var exclude = MenuItem(cellMenu, "ExcludeValue");
        filterBy.IsEnabled = exclude.IsEnabled = hasCell;
        filterBy.Text = hasCell ? $"Lọc: {ColumnName(_menuColumnIndex)} = \"{shown}\"" : "Lọc theo giá trị này";
        exclude.Text = hasCell ? $"Lọc bỏ: {ColumnName(_menuColumnIndex)} ≠ \"{shown}\"" : "Lọc bỏ giá trị này";
        cellMenu.ShowAt(Grid, showOptions);
    }

    private static MenuFlyoutItem MenuItem(MenuFlyout menu, string tag) =>
        menu.Items.OfType<MenuFlyoutItem>().First(item => item.Tag as string == tag);

    private void FilterByValueItem_Click(object sender, RoutedEventArgs e) => FilterByMenuCell(FilterOperator.Equal);

    private void ExcludeValueItem_Click(object sender, RoutedEventArgs e) => FilterByMenuCell(FilterOperator.NotEqual);

    /// <summary>Thêm điều kiện "cột = / ≠ giá trị ô vừa bấm" vào lọc nâng cao đang có (nối bằng VÀ, như Excel lọc tiếp trên
    /// kết quả đang hiện). Lọc đang có là HOẶC nhiều điều kiện thì không nối được - thay bằng điều kiện mới và báo.</summary>
    private void FilterByMenuCell(FilterOperator op)
    {
        if (_menuRowIndex < 0 || _menuRowIndex >= ViewModel.ViewRows.Count || _menuColumnIndex < 0)
        {
            return;
        }

        var condition = new FilterCondition
        {
            ColumnIndex = _menuColumnIndex,
            Operator = op,
            Value = ViewModel.ViewRows[_menuRowIndex].GetCell(_menuColumnIndex),
        };
        var current = ViewModel.ActiveFilter;
        var filter = new FilterExpression();
        var replaced = current is { Join: FilterJoin.Or, Conditions.Count: > 1 };
        if (current is not null && !replaced)
        {
            filter.Conditions.AddRange(current.Conditions);
        }
        filter.Conditions.Add(condition);
        ViewModel.ApplyFilter(filter);
        if (replaced)
        {
            ViewModel.StatusMessage = "Lọc cũ dạng HOẶC không nối thêm được - đã thay bằng lọc theo giá trị vừa chọn.";
        }
    }

    private void SortAscendingItem_Click(object sender, RoutedEventArgs e) => SortByMenuColumn(descending: false);

    private void SortDescendingItem_Click(object sender, RoutedEventArgs e) => SortByMenuColumn(descending: true);

    private void SortByMenuColumn(bool descending)
    {
        if (_menuColumnIndex >= 0)
        {
            ViewModel.ApplySort(new List<(int, bool)> { (_menuColumnIndex, descending) });
        }
    }

    private void ClearColumnFilterItem_Click(object sender, RoutedEventArgs e)
    {
        if (_menuColumnIndex >= 0 && _menuColumnIndex < Grid.Columns.Count && Grid.Columns[_menuColumnIndex].Header is FilterHeaderItem item)
        {
            item.FilterText = string.Empty;
        }
    }

    private async void HeaderRenameItem_Click(object sender, RoutedEventArgs e) => await RenameColumnAsync(_menuColumnIndex);

    private async void HeaderDeleteItem_Click(object sender, RoutedEventArgs e) => await DeleteColumnAsync(_menuColumnIndex);

    private async void InsertColumnRightItem_Click(object sender, RoutedEventArgs e)
    {
        if (_menuColumnIndex >= 0)
        {
            await AddColumnAsync(_menuColumnIndex + 1);
        }
    }

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedRows = Grid.SelectedItems.Cast<CsvRow>().ToList();
        if (selectedRows.Count == 0)
        {
            return;
        }

        // Dạng Tab như Excel: ô có tab / xuống dòng được bọc ngoặc kép nên dán sang Excel vẫn đúng 1 ô.
        var text = TabularClipboard.Format(selectedRows.Select(row => Enumerable.Range(0, ViewModel.Columns.Count).Select(row.GetCell)));
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private async void PasteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var clipboardContent = Clipboard.GetContent();
        if (!clipboardContent.Contains(StandardDataFormats.Text))
        {
            return;
        }

        var text = await clipboardContent.GetTextAsync();
        var block = TabularClipboard.Parse(text);
        if (block.Count == 0)
        {
            return;
        }

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

    // ----- Xem dọc 1 dòng -----

    private const double RecordPaneGap = 8;

    /// <summary>Dòng đang hiện trong khung (giữ tham chiếu, không giữ số thứ tự: lọc / sắp xếp đổi vị trí dòng).</summary>
    private CsvRow? _recordRow;
    private readonly List<RecordField> _recordFields = new();

    private bool IsRecordPaneOpen => RecordPane.Visibility == Visibility.Visible;

    private void RecordViewToggle_Click(object sender, RoutedEventArgs e) => SetRecordPaneOpen(RecordViewToggle.IsChecked == true);

    private void RecordViewMenuItem_Click(object sender, RoutedEventArgs e) => SetRecordPaneOpen(true);

    private void RecordCloseButton_Click(object sender, RoutedEventArgs e) => SetRecordPaneOpen(false);

    private void SetRecordPaneOpen(bool open)
    {
        RecordViewToggle.IsChecked = open;
        RecordPane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Grid.Margin = new Thickness(0, 0, open ? RecordPane.Width + RecordPaneGap : 0, 0);
        UpdateFillHandlePosition();
        UpdateRecordPane(rebuild: true);
    }

    /// <summary>Đưa dòng đang chọn vào khung. Cùng dòng, cùng số cột → chỉ cập nhật giá trị đã đổi (sau sửa / hoàn tác),
    /// giữ nguyên ô đang gõ và vị trí cuộn; khác dòng hoặc <paramref name="rebuild"/> → dựng lại danh sách.</summary>
    private void UpdateRecordPane(bool rebuild = false)
    {
        if (!IsRecordPaneOpen)
        {
            return;
        }

        var index = Grid.SelectedIndex;
        var row = index >= 0 && index < ViewModel.ViewRows.Count ? ViewModel.ViewRows[index] : null;
        RecordTitle.Text = row is null ? "Xem dọc" : $"Dòng {index + 1:N0} / {ViewModel.ViewRows.Count:N0}";
        RecordEmptyText.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;

        if (!rebuild && ReferenceEquals(row, _recordRow) && _recordFields.Count == ViewModel.Columns.Count)
        {
            foreach (var field in _recordFields)
            {
                var value = row?.GetCell(field.ColumnIndex) ?? string.Empty;
                if (value != field.Value)
                {
                    field.Value = value;
                    field.EditText = value;
                }
            }
            return;
        }

        _recordRow = row;
        _recordFields.Clear();
        if (row is not null)
        {
            var columns = ViewModel.Columns;
            for (var i = 0; i < columns.Count; i++)
            {
                _recordFields.Add(new RecordField(i, columns[i].Name, row.GetCell(i)));
            }
        }
        ApplyRecordSearch();
    }

    private void RecordSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyRecordSearch();

    /// <summary>Ô tìm: giữ các dòng có tên cột hoặc giá trị chứa chữ đã gõ (không phân biệt hoa thường).</summary>
    private void ApplyRecordSearch()
    {
        var query = RecordSearchBox.Text.Trim();
        RecordFieldList.ItemsSource = query.Length == 0
            ? _recordFields.ToList()
            : _recordFields.Where(field => field.ColumnName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || field.Value.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void RecordValue_LostFocus(object sender, RoutedEventArgs e) => CommitRecordField((FrameworkElement)sender);

    /// <summary>Ghi chữ đã sửa vào bảng (1 bước Hoàn tác, như sửa ô trên bảng). Dòng đã bị lọc mất khỏi bảng thì trả lại
    /// giá trị cũ và báo ở thanh trạng thái.</summary>
    private void CommitRecordField(FrameworkElement box)
    {
        if (box.DataContext is not RecordField field || _recordRow is null || field.EditText == field.Value)
        {
            return;
        }

        var viewIndex = ViewModel.ViewRows.IndexOf(_recordRow);
        if (viewIndex < 0)
        {
            field.EditText = field.Value;
            ViewModel.StatusMessage = "Dòng này không còn trong bảng đang hiện - chưa ghi thay đổi.";
            return;
        }

        field.Value = field.EditText; // trước SetCell: UpdateRecordPane (qua CanUndo) thấy giá trị đã khớp, không ghi đè ô
        ViewModel.SetCell(viewIndex, field.ColumnIndex, field.EditText);
    }

    /// <summary>Enter = ghi + sang ô dưới; Shift+Enter = xuống dòng trong ô; Esc = trả lại giá trị đang có trong bảng.</summary>
    private void RecordValue_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: RecordField field } box)
        {
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            field.EditText = field.Value;
            box.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter
            && !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            CommitRecordField(box);
            FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = RecordPane });
            e.Handled = true;
        }
    }

    private void RecordPrevButton_Click(object sender, RoutedEventArgs e) => StepRecord(-1);

    private void RecordNextButton_Click(object sender, RoutedEventArgs e) => StepRecord(1);

    private void StepRecord(int delta)
    {
        var index = Grid.SelectedIndex + delta;
        if (index >= 0 && index < ViewModel.ViewRows.Count)
        {
            SelectRow(ViewModel.ViewRows[index]);
        }
    }

    // ----- Ẩn / hiện cột, cố định cột -----

    /// <summary>File mà <see cref="_hiddenColumnNames"/> / <see cref="_frozenColumnCount"/> thuộc về (RebuildColumns bỏ khi
    /// mở file khác). Chỉ là cách xem: lưu file / xuất Excel vẫn đủ mọi cột.</summary>
    private string? _columnViewPath;
    private readonly HashSet<string> _hiddenColumnNames = new();
    private int _frozenColumnCount;

    private void SetColumnsHidden(HashSet<string> hiddenNames)
    {
        _hiddenColumnNames.Clear();
        _hiddenColumnNames.UnionWith(hiddenNames);
        foreach (var column in Grid.Columns)
        {
            column.Visibility = _hiddenColumnNames.Contains(((FilterHeaderItem)column.Header).ColumnName) ? Visibility.Collapsed : Visibility.Visible;
        }
        RefreshChips();
    }

    private void HideColumnItem_Click(object sender, RoutedEventArgs e)
    {
        if (_menuColumnIndex < 0 || _menuColumnIndex >= Grid.Columns.Count)
        {
            return;
        }
        if (Grid.Columns.Count(column => column.Visibility == Visibility.Visible) <= 1)
        {
            ViewModel.StatusMessage = "Không ẩn được cột cuối cùng đang hiện.";
            return;
        }
        SetColumnsHidden(new HashSet<string>(_hiddenColumnNames) { ViewModel.Columns[_menuColumnIndex].Name });
    }

    private void ShowAllColumns_Click(object sender, RoutedEventArgs e) => SetColumnsHidden(new HashSet<string>());

    /// <summary>Cột ▾ → Ẩn / hiện cột... (hoặc bấm thẻ "Đang ẩn"): danh sách cột có ô tick (tick = hiện), ô tìm theo tên,
    /// nút Hiện tất cả / Bỏ tick tất cả (theo các cột đang lọc thấy). Phải còn ít nhất 1 cột hiện.</summary>
    private async void ShowHideColumns_Click(object sender, RoutedEventArgs e)
    {
        var columns = ViewModel.Columns;
        if (columns.Count == 0)
        {
            ViewModel.StatusMessage = "Chưa có cột nào.";
            return;
        }

        var boxes = columns.Select((column, index) => new CheckBox
        {
            Content = $"{index + 1}. {column.Name}",
            Tag = column.Name,
            IsChecked = !_hiddenColumnNames.Contains(column.Name),
            MinWidth = 0,
        }).ToList();
        var list = new StackPanel();
        boxes.ForEach(list.Children.Add);

        var search = new TextBox { PlaceholderText = "Tìm cột..." };
        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            foreach (var box in boxes)
            {
                box.Visibility = query.Length == 0 || ((string)box.Tag).Contains(query, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        Button SetAllButton(string text, bool isChecked)
        {
            var button = new Button { Content = text };
            button.Click += (_, _) => boxes.Where(box => box.Visibility == Visibility.Visible).ToList().ForEach(box => box.IsChecked = isChecked);
            return button;
        }

        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Ẩn / hiện cột",
            Content = new StackPanel
            {
                Spacing = 8,
                MinWidth = 340,
                Children =
                {
                    new TextBlock { Text = "Bỏ tick cột muốn ẩn khi xem - lưu file / xuất Excel vẫn đủ mọi cột.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                    search,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { SetAllButton("Hiện tất cả", true), SetAllButton("Bỏ tick tất cả", false) } },
                    // ContentDialog không tự cuộn: danh sách dài (file 100+ cột) tự cuộn trong khung này.
                    new ScrollViewer { Content = list, MaxHeight = 320 },
                    error,
                },
            },
            PrimaryButtonText = "Áp dụng",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (boxes.All(box => box.IsChecked != true))
            {
                error.Text = "Phải còn ít nhất 1 cột hiện.";
                args.Cancel = true;
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            SetColumnsHidden(new HashSet<string>(boxes.Where(box => box.IsChecked != true).Select(box => (string)box.Tag)));
        }
    }

    /// <summary>Cố định cột 1..n khi cuộn ngang (DataGrid.FrozenColumnCount) - n = cột vừa chuột phải / cột của ô đang chọn.</summary>
    private void SetFrozenColumns(int count)
    {
        _frozenColumnCount = Math.Clamp(count, 0, Grid.Columns.Count);
        Grid.FrozenColumnCount = _frozenColumnCount;
        RefreshChips();
    }

    private void FreezeToColumnItem_Click(object sender, RoutedEventArgs e)
    {
        if (_menuColumnIndex >= 0)
        {
            SetFrozenColumns(_menuColumnIndex + 1);
        }
    }

    private void FreezeToCurrentColumn_Click(object sender, RoutedEventArgs e)
    {
        var columnIndex = CurrentColumnIndexOrHint("cố định đến");
        if (columnIndex >= 0)
        {
            SetFrozenColumns(columnIndex + 1);
        }
    }

    private void UnfreezeColumns_Click(object sender, RoutedEventArgs e) => SetFrozenColumns(0);
}
