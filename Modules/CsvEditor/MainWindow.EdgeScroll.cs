using CommunityToolkit.WinUI.UI.Controls.Primitives;
using CsvEditor.Models;
using CsvEditor.Services;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace CsvEditor;

/// <summary>Kéo chọn vùng hoặc kéo núm điền ra ngoài mép vùng ô: bảng tự cuộn liên tục (nhịp <see cref="EdgeScrollInterval"/>), càng
/// xa mép càng nhanh (<see cref="EdgeScroll"/>), vùng / phần điền nới theo. Dừng khi thả chuột hoặc kéo vào lại trong bảng.</summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan EdgeScrollInterval = TimeSpan.FromMilliseconds(60);

    private DispatcherTimer? _edgeScrollTimer;

    /// <summary>Vị trí con trỏ lúc kéo, toạ độ FillHandleLayer.</summary>
    private Point _dragPoint;

    /// <summary>Phần lẻ số cột cuộn ngang (<see cref="EdgeScroll.TakeWhole"/>).</summary>
    private double _edgeColumnCarry;

    /// <summary>Vùng ô đang thấy (toạ độ FillHandleLayer): dưới tiêu đề cột, phải số dòng và các cột cố định (các cột đó không cuộn:
    /// kéo vào đó là cuộn ngang về trái). Riêng vùng bắt đầu từ cột cố định thì tính cả cột cố định, không thì vừa kéo đã cuộn.</summary>
    private Rect CellArea()
    {
        var headerBottom = FindDescendants<DataGridColumnHeader>(Grid).FirstOrDefault() is { } header
            ? header.TransformToVisual(FillHandleLayer).TransformPoint(new Point(0, header.ActualHeight)).Y
            : 0;
        var startsInFrozen = _rangeDragging && _range is { } range && range.Anchor.ColumnIndex < Grid.FrozenColumnCount;
        var frozenWidth = startsInFrozen ? 0 : Grid.Columns.Take(Grid.FrozenColumnCount)
            .Where(column => column.Visibility == Visibility.Visible)
            .Sum(column => column.ActualWidth);
        var left = Grid.RowHeaderWidth + frozenWidth;
        return new Rect(left, headerBottom, Math.Max(Grid.ActualWidth - left, 1), Math.Max(Grid.ActualHeight - headerBottom, 1));
    }

    /// <summary>Gọi mỗi lần chuột di khi đang kéo: con trỏ ngoài vùng ô thì bật nhịp cuộn.</summary>
    private void UpdateEdgeScroll(Point point)
    {
        _dragPoint = point;
        var area = CellArea();
        var outside = EdgeScroll.Overshoot(point.X, area.Left, area.Right) != 0
            || EdgeScroll.Overshoot(point.Y, area.Top, area.Bottom) != 0;
        if (!outside)
        {
            StopEdgeScroll();
            return;
        }

        if (_edgeScrollTimer is null)
        {
            _edgeScrollTimer = new DispatcherTimer { Interval = EdgeScrollInterval };
            _edgeScrollTimer.Tick += (_, _) => EdgeScrollTick();
        }

        if (!_edgeScrollTimer.IsEnabled)
        {
            EdgeScrollTick(); // cuộn ngay, không chờ nhịp đầu
            _edgeScrollTimer.Start();
        }
    }

    private void StopEdgeScroll()
    {
        _edgeScrollTimer?.Stop();
        _edgeColumnCarry = 0;
    }

    private void EdgeScrollTick()
    {
        if (_rangeDragging && _range is { } range)
        {
            EdgeScrollRange(range);
        }
        else if (_isFillDragging)
        {
            EdgeScrollFill();
        }
        else
        {
            StopEdgeScroll();
        }
    }

    private void EdgeScrollRange(CellRange range)
    {
        var area = CellArea();
        var rowStep = EdgeScroll.Step(EdgeScroll.Overshoot(_dragPoint.Y, area.Top, area.Bottom));
        var columnRate = EdgeScroll.ColumnRate(EdgeScroll.Overshoot(_dragPoint.X, area.Left, area.Right));
        if (rowStep == 0 && columnRate == 0)
        {
            StopEdgeScroll();
            return;
        }

        var columnStep = EdgeScroll.TakeWhole(ref _edgeColumnCarry, columnRate);

        // Ô ở mép ngay trong vùng, thẳng hàng với con trỏ: chuột ra mép dưới mà lệch phải thì vẫn chọn đúng cột đang chỉ.
        var edge = CellAt(ClampInto(_dragPoint, area)) ?? range.Active;
        var row = Math.Clamp(edge.RowIndex + rowStep, 0, ViewModel.ViewRows.Count - 1);
        var column = StepVisibleColumn(edge.ColumnIndex, columnStep);
        var active = new CellRef(row, column);
        if (active != range.Active)
        {
            _rangeDragMoved = true;
            SetRange(range with { Active = active });
        }

        Grid.ScrollIntoView(ViewModel.ViewRows[row], Grid.Columns[column]);
    }

    /// <summary>Núm điền chỉ kéo xuống: ra mép dưới thì nới thêm dòng, ra mép trên thì rút lại (không lên quá ô nguồn).</summary>
    private void EdgeScrollFill()
    {
        var area = CellArea();
        var rowStep = EdgeScroll.Step(EdgeScroll.Overshoot(_dragPoint.Y, area.Top, area.Bottom));
        if (rowStep == 0)
        {
            StopEdgeScroll();
            return;
        }

        var edgeRow = FindRowIndexAtPoint(FillHandleLayer.TransformToVisual(null).TransformPoint(ClampInto(_dragPoint, area)))
            ?? _fillTargetRowIndex;
        _fillTargetRowIndex = Math.Clamp(edgeRow + rowStep, _fillSourceRowIndex, ViewModel.ViewRows.Count - 1);
        Grid.ScrollIntoView(ViewModel.ViewRows[_fillTargetRowIndex], Grid.Columns[_fillSourceColumnIndex]);
        Grid.UpdateLayout(); // dựng dòng vừa cuộn tới, không thì khung xem trước không tìm thấy dòng đích và ẩn suốt lúc cuộn
        UpdateFillPreview();
    }

    /// <summary>Điểm nằm trong vùng ô, cách mép 2 px: ô ở mép (dòng cuối có thể bị thanh cuộn ngang che một phần).</summary>
    private static Point ClampInto(Point point, Rect area) => new(
        Math.Clamp(point.X, area.Left + 2, Math.Max(area.Left + 2, area.Right - 2)),
        Math.Clamp(point.Y, area.Top + 2, Math.Max(area.Top + 2, area.Bottom - 2)));

    /// <summary>Đi <paramref name="step"/> cột đang hiện (bỏ cột ẩn) từ cột đang có, dừng ở cột hiện đầu / cuối.</summary>
    private int StepVisibleColumn(int columnIndex, int step)
    {
        var direction = Math.Sign(step);
        var remaining = Math.Abs(step);
        for (var c = columnIndex + direction; remaining > 0 && c >= 0 && c < Grid.Columns.Count; c += direction)
        {
            if (Grid.Columns[c].Visibility == Visibility.Visible)
            {
                columnIndex = c;
                remaining--;
            }
        }

        return columnIndex;
    }
}
