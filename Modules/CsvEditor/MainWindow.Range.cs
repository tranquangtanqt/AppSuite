using CommunityToolkit.WinUI.UI.Controls;
using CommunityToolkit.WinUI.UI.Controls.Primitives;
using CsvEditor.Models;
using CsvEditor.Services;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace CsvEditor;

/// <summary>Chọn vùng ô, chép / dán vùng. DataGrid chỉ biết chọn cả dòng nên vùng do cửa sổ tự giữ (<see cref="_range"/>) và vẽ
/// bằng <c>RangeOverlay</c> đè lên bảng; luôn bằng ô hiện tại khi không chọn vùng. Vị trí là số thứ tự trên bảng đang hiện:
/// lọc / sắp xếp làm đổi vị trí nên bỏ vùng. Dòng đang chọn của DataGrid giữ ở 1 dòng để không tô xanh cả loạt dòng.</summary>
public sealed partial class MainWindow
{
    /// <summary>Vùng lớn hơn thì không tính Đếm / Tổng (chọn cả file nhiều triệu ô sẽ đứng cửa sổ mỗi lần bấm mũi tên).</summary>
    private const long MaxStatisticsCells = 2_000_000;

    private CellRange? _range;
    private bool _rangeDragging;
    private bool _rangeDragMoved;
    private bool _changingRange;
    private CellRef? _shiftClickCell;
    private Point _rangePressPoint;

    /// <summary>Chuột di quá chừng này (px) kể từ lúc nhấn mới tính là kéo.</summary>
    private const double DragThreshold = 4;

    private bool HasBlockRange => _range is { IsSingleCell: false };

    private void InitRangeSelection()
    {
        // handledEventsToo: DataGrid đã xử lý nhấn / rê chuột trên ô nên sự kiện không nổi lên theo đường thường.
        Grid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Grid_RangePointerPressed), true);
        Grid.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Grid_RangePointerMoved), true);
        Grid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Grid_RangePointerReleased), true);
        Grid.AddHandler(UIElement.TappedEvent, new TappedEventHandler(Grid_RangeTapped), true);
        ViewModel.ViewRows.CollectionChanged += (_, _) => ClearRange();
    }

    private CellRef? CurrentCell()
    {
        var column = Grid.CurrentColumn is { } current ? Grid.Columns.IndexOf(current) : -1;
        return Grid.SelectedIndex >= 0 && column >= 0 ? new CellRef(Grid.SelectedIndex, column) : null;
    }

    private void ClearRange()
    {
        if (_range is null)
        {
            return;
        }

        _range = null;
        _rangeDragging = false;
        RefreshRange();
    }

    private void CollapseRangeToCurrentCell()
    {
        _range = CurrentCell() is { } cell ? new CellRange(cell, cell) : null;
        RefreshRange();
    }

    private void SetRange(CellRange range)
    {
        _range = range;
        _changingRange = true;
        try
        {
            Grid.SelectedItem = ViewModel.ViewRows[range.Active.RowIndex];
            Grid.CurrentColumn = Grid.Columns[range.Active.ColumnIndex];
        }
        finally
        {
            _changingRange = false;
        }

        RefreshRange();
    }

    private void RefreshRange()
    {
        UpdateRangeOverlay();
        UpdateFillHandlePosition();
        RangeStatsText.Visibility = HasBlockRange ? Visibility.Visible : Visibility.Collapsed;
        if (_range is { IsSingleCell: false } range)
        {
            var cells = (long)(range.Bottom - range.Top + 1) * (range.Right - range.Left + 1);
            RangeStatsText.Text = cells > MaxStatisticsCells
                ? $"Đã chọn {cells:N0} ô"
                : ViewModel.ComputeRangeStatistics(range).Describe();
        }
    }

    /// <summary>Ô ở vị trí (toạ độ FillHandleLayer), null nếu không trúng ô: tiêu đề, số dòng, vùng trống.</summary>
    private CellRef? CellAt(Point point)
    {
        foreach (var row in FindDescendants<DataGridRow>(Grid))
        {
            if (row.Visibility != Visibility.Visible || row.DataContext is not CsvRow)
            {
                continue;
            }

            var rowTop = row.TransformToVisual(FillHandleLayer).TransformPoint(new Point(0, 0)).Y;
            if (point.Y < rowTop || point.Y >= rowTop + row.ActualHeight)
            {
                continue;
            }

            for (var c = 0; c < Grid.Columns.Count; c++)
            {
                if (CellBounds(row, c) is { } bounds && point.X >= bounds.Left && point.X < bounds.Right)
                {
                    return new CellRef(row.GetIndex(), c);
                }
            }

            return null;
        }

        return null;
    }

    private Rect? CellBounds(DataGridRow row, int columnIndex)
    {
        if (Grid.Columns[columnIndex].Visibility != Visibility.Visible
            || Grid.Columns[columnIndex].GetCellContent(row) is not FrameworkElement content
            || FindAncestor<DataGridCell>(content) is not { } cell)
        {
            return null;
        }

        var topLeft = cell.TransformToVisual(FillHandleLayer).TransformPoint(new Point(0, 0));
        return new Rect(topLeft.X, topLeft.Y, cell.ActualWidth, cell.ActualHeight);
    }

    private void UpdateRangeOverlay()
    {
        if (_range is not { IsSingleCell: false } range)
        {
            RangeOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        // Chỉ các dòng đang dựng trên màn hình mới có phần tử: vùng cao hơn màn hình thì vẽ phần đang thấy.
        var rows = FindDescendants<DataGridRow>(Grid)
            .Where(row => row.Visibility == Visibility.Visible && row.DataContext is CsvRow
                && row.GetIndex() >= range.Top && row.GetIndex() <= range.Bottom)
            .ToList();
        var columnBounds = rows.Count == 0
            ? new List<Rect>()
            : Enumerable.Range(range.Left, range.Right - range.Left + 1)
                .Select(column => CellBounds(rows[0], column))
                .OfType<Rect>()
                .ToList();
        if (columnBounds.Count == 0)
        {
            RangeOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var headerBottom = FindDescendants<DataGridColumnHeader>(Grid).FirstOrDefault() is { } header
            ? header.TransformToVisual(FillHandleLayer).TransformPoint(new Point(0, header.ActualHeight)).Y
            : 0;
        var tops = rows.Select(row => row.TransformToVisual(FillHandleLayer).TransformPoint(new Point(0, 0)).Y).ToList();
        var top = Math.Max(tops.Min(), headerBottom);
        var bottom = Math.Min(rows.Zip(tops, (row, rowTop) => rowTop + row.ActualHeight).Max(), FillHandleLayer.ActualHeight);
        var left = Math.Max(columnBounds.Min(bounds => bounds.Left), Grid.RowHeaderWidth);
        var right = Math.Min(columnBounds.Max(bounds => bounds.Right), Grid.ActualWidth);
        if (bottom <= top || right <= left)
        {
            RangeOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        // Đổi vị trí gây LayoutUpdated, mà hàm này chạy trong LayoutUpdated: chỉ đặt khi khác.
        if (Canvas.GetLeft(RangeOverlay) != left || Canvas.GetTop(RangeOverlay) != top
            || RangeOverlay.Width != right - left || RangeOverlay.Height != bottom - top)
        {
            Canvas.SetLeft(RangeOverlay, left);
            Canvas.SetTop(RangeOverlay, top);
            RangeOverlay.Width = right - left;
            RangeOverlay.Height = bottom - top;
        }

        RangeOverlay.Visibility = Visibility.Visible;
    }

    private void Grid_RangePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(FillHandleLayer);
        if (!point.Properties.IsLeftButtonPressed || e.OriginalSource is TextBox)
        {
            return;
        }

        if (CellAt(point.Position) is not { } cell)
        {
            return;
        }

        if (IsDown(VirtualKey.Shift) && _range is not null)
        {
            // Nối vùng ở Tapped, sau khi DataGrid xử lý xong cú bấm. Đặt ô bấm thành ô hiện tại ngay lúc nhấn thì DataGrid coi là bấm
            // lại ô hiện tại và vào chế độ sửa, vùng mất.
            _shiftClickCell = cell;
            return;
        }

        _range = new CellRange(cell, cell);
        _rangeDragging = true;
        _rangeDragMoved = false;
        _rangePressPoint = point.Position;
        RefreshRange();
    }

    private void Grid_RangePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_rangeDragging || _range is not { } range)
        {
            return;
        }

        var point = e.GetCurrentPoint(FillHandleLayer);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _rangeDragging = false;
            StopEdgeScroll();
            return;
        }

        if (CellAt(point.Position) is { } cell && cell != range.Active)
        {
            _rangeDragMoved = true;
            Grid.CapturePointer(e.Pointer);
            SetRange(range with { Active = cell });
        }

        // Kéo quá ngưỡng là giữ con trỏ ngay, kể cả còn trong ô vừa nhấn: kéo nhanh vọt thẳng ra ngoài bảng thì vẫn nhận được di
        // chuột bên ngoài để tự cuộn. Rung tay khi bấm gọn (dưới ngưỡng) không giữ - bấm gọn để DataGrid tự lo (xem PointerReleased).
        if (Math.Abs(point.Position.X - _rangePressPoint.X) > DragThreshold || Math.Abs(point.Position.Y - _rangePressPoint.Y) > DragThreshold)
        {
            Grid.CapturePointer(e.Pointer);
        }

        UpdateEdgeScroll(point.Position);
    }

    private void Grid_RangePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_rangeDragging)
        {
            _rangeDragging = false;
            StopEdgeScroll();
            Grid.ReleasePointerCapture(e.Pointer);

            // DataGrid chỉ nhận con trỏ khi bấm gọn, kéo chuột thì Ctrl+C / Esc không tới bảng. Bấm gọn phải để DataGrid tự lo: đang sửa ô
            // nó chờ ô sửa mất tiêu điểm rồi mới đổi dòng, gọi Focus lúc đó làm cú bấm mất tác dụng và tiêu điểm nhảy sang ô Lọc đầu tiên.
            if (_rangeDragMoved)
            {
                Grid.CommitEdit(DataGridEditingUnit.Row, true);
                Grid.Focus(FocusState.Programmatic);
            }
        }
    }

    private void Grid_RangeTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_shiftClickCell is { } cell && _range is { } range)
        {
            SetRange(range with { Active = cell });
        }

        _shiftClickCell = null;
    }

    private async void Grid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return; // đang gõ trong ô sửa / ô lọc: phím thuộc về ô nhập
        }

        var ctrl = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        if (ctrl && e.Key == VirtualKey.C)
        {
            e.Handled = true;
            CopySelection();
        }
        else if (ctrl && e.Key == VirtualKey.V)
        {
            e.Handled = true;
            await PasteFromClipboardAsync();
        }
        else if (e.Key == VirtualKey.Escape && HasBlockRange)
        {
            e.Handled = true;
            CollapseRangeToCurrentCell();
        }
        else if (shift && e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right)
        {
            e.Handled = true;
            ExtendRange(e.Key, toEdge: ctrl);
        }
    }

    /// <summary>Đọc trạng thái phím của Windows: InputKeyboardSource trả "không giữ" cho Shift khi tiêu điểm ở DataGrid (Shift+mũi tên
    /// thành mũi tên thường, đã ghi log trong Sandbox).</summary>
    private static bool IsDown(VirtualKey key) => GetAsyncKeyState((int)key) < 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    /// <summary>Shift+mũi tên nới vùng 1 ô, Ctrl+Shift+mũi tên nới tới mép bảng. Bỏ qua cột đang ẩn.</summary>
    private void ExtendRange(VirtualKey key, bool toEdge)
    {
        if ((_range ?? (CurrentCell() is { } cell ? new CellRange(cell, cell) : null)) is not { } range)
        {
            return;
        }

        var row = range.Active.RowIndex;
        var column = range.Active.ColumnIndex;
        switch (key)
        {
            case VirtualKey.Up:
                row = toEdge ? 0 : Math.Max(row - 1, 0);
                break;
            case VirtualKey.Down:
                row = toEdge ? ViewModel.ViewRows.Count - 1 : Math.Min(row + 1, ViewModel.ViewRows.Count - 1);
                break;
            default:
                var step = key == VirtualKey.Left ? -1 : 1;
                for (var c = column + step; c >= 0 && c < Grid.Columns.Count; c += step)
                {
                    if (Grid.Columns[c].Visibility == Visibility.Visible)
                    {
                        column = c;
                        if (!toEdge)
                        {
                            break;
                        }
                    }
                }
                break;
        }

        SetRange(range with { Active = new CellRef(row, column) });
        Grid.ScrollIntoView(ViewModel.ViewRows[row], Grid.Columns[column]);
    }

    /// <summary>Ctrl+C / menu Sao chép: vùng đang chọn; nhiều dòng chọn từ số đầu dòng thì chép cả các dòng; còn lại chép 1 ô.</summary>
    private void CopySelection()
    {
        string text;
        if (_range is { IsSingleCell: false } range)
        {
            text = ViewModel.FormatRange(range);
        }
        else if (Grid.SelectedItems.Count > 1)
        {
            text = FormatSelectedRows();
        }
        else if (CurrentCell() is { } cell)
        {
            text = ViewModel.FormatRange(new CellRange(cell, cell));
        }
        else
        {
            return;
        }

        SetClipboardText(text);
    }

    private string FormatSelectedRows() =>
        TabularClipboard.Format(Grid.SelectedItems.Cast<CsvRow>().Select(row =>
            Enumerable.Range(0, ViewModel.Columns.Count).Select(row.GetCell)));

    private static void SetClipboardText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    /// <summary>Ctrl+V / menu Dán: dán từ góc trái trên của vùng (hoặc ô hiện tại). Clipboard 1 ô mà đang chọn vùng thì điền cả
    /// vùng, như Excel. Dán xong chọn đúng phần vừa dán để thấy rõ đã đổi những ô nào.</summary>
    private async Task PasteFromClipboardAsync()
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text) || ViewModel.ViewRows.Count == 0)
        {
            return;
        }

        var block = TabularClipboard.Parse(await content.GetTextAsync());
        if (block.Count == 0)
        {
            return;
        }

        if (_range is { IsSingleCell: false } range && block is [{ Count: 1 }])
        {
            var filled = ViewModel.FillRange(range, block[0][0]);
            ViewModel.StatusMessage = $"Đã điền {filled:N0} ô.";
            RefreshRange();
            return;
        }

        var start = _range is { } current
            ? new CellRef(current.Top, current.Left)
            : CurrentCell() ?? new CellRef(0, FirstVisibleColumn());
        var result = ViewModel.PasteRange(start, block);
        var skipped = result.SkippedRows > 0 || result.SkippedColumns > 0
            ? $" Bỏ qua {result.SkippedRows:N0} dòng, {result.SkippedColumns:N0} cột vượt ngoài bảng."
            : string.Empty;
        ViewModel.StatusMessage = $"Đã dán {result.Rows:N0} dòng, {result.Columns:N0} cột.{skipped}";
        if (result.LastCell is { } last)
        {
            SetRange(new CellRange(start, last));
        }
    }

    private int FirstVisibleColumn() =>
        Math.Max(Grid.Columns.ToList().FindIndex(column => column.Visibility == Visibility.Visible), 0);
}
