using CommunityToolkit.WinUI.UI.Controls;
using CommunityToolkit.WinUI.UI.Controls.Primitives;
using CsvEditor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CsvEditor;

/// <summary>Giãn độ rộng cột vừa nội dung: bấm đúp vạch ngăn bên phải tiêu đề cột, chuột phải tiêu đề → Giãn vừa nội dung / Giãn
/// vừa tất cả cột, Ctrl+Shift+W. Đo cả cột đang hiện (theo lọc), không chỉ 100 dòng đầu như lúc mở file; sửa ô không tự giãn.</summary>
public sealed partial class MainWindow
{
    /// <summary>Bấm đúp cách vạch ngăn cột trong khoảng này (px) thì tính là bấm đúp vạch ngăn.</summary>
    private const double ColumnEdgeHitWidth = 6;

    private void InitColumnFit() =>
        Grid.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(Grid_ColumnEdgeDoubleTapped), true);

    private void Grid_ColumnEdgeDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindAncestorOrSelf<TextBox>(source) is not null
            || FindAncestorOrSelf<DataGridColumnHeader>(source) is not { } header)
        {
            return;
        }

        var columnIndex = Grid.Columns.ToList().FindIndex(column => ReferenceEquals(column.Header, header.Content));
        if (columnIndex < 0)
        {
            return;
        }

        // Mép trái tiêu đề là vạch ngăn bên phải của cột hiện trước nó.
        var x = e.GetPosition(header).X;
        if (x <= ColumnEdgeHitWidth)
        {
            columnIndex = PreviousVisibleColumn(columnIndex);
        }
        else if (x < header.ActualWidth - ColumnEdgeHitWidth)
        {
            return;
        }

        if (columnIndex >= 0)
        {
            e.Handled = true;
            AutoFitColumn(columnIndex);
        }
    }

    private int PreviousVisibleColumn(int columnIndex)
    {
        for (var c = columnIndex - 1; c >= 0; c--)
        {
            if (Grid.Columns[c].Visibility == Visibility.Visible)
            {
                return c;
            }
        }

        return -1;
    }

    private void AutoFitColumn(int columnIndex)
    {
        var width = FittedColumnWidth(columnIndex);
        Grid.Columns[columnIndex].Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
        ViewModel.StatusMessage = $"Cột {ViewModel.Columns[columnIndex].Name}: rộng {width:N0} px (vừa nội dung).";
    }

    private void AutoFitAllColumns()
    {
        if (Grid.Columns.Count == 0)
        {
            return;
        }

        for (var c = 0; c < Grid.Columns.Count; c++)
        {
            Grid.Columns[c].Width = new DataGridLength(FittedColumnWidth(c), DataGridLengthUnitType.Pixel);
        }

        ViewModel.StatusMessage = $"Đã giãn {Grid.Columns.Count:N0} cột vừa nội dung.";
    }

    /// <summary>Tên cột và các ô của cột ở mọi dòng đang hiện, chỉ đo TextBlock cho vài chục chữ dài nhất (xem
    /// <see cref="ColumnAutoFit"/>).</summary>
    private double FittedColumnWidth(int columnIndex)
    {
        var candidates = ColumnAutoFit.LongestTexts(ViewModel.ViewRows.Select(row => row.GetCell(columnIndex)));
        var widest = candidates.Append(ViewModel.Columns[columnIndex].Name)
            .Max(text => EstimateTextWidth(text));
        return ColumnAutoFit.Fit(widest);
    }

    private void AutoFitColumnItem_Click(object sender, RoutedEventArgs e)
    {
        if (_menuColumnIndex >= 0)
        {
            AutoFitColumn(_menuColumnIndex);
        }
    }

    private void AutoFitCurrentColumn_Click(object sender, RoutedEventArgs e)
    {
        var columnIndex = CurrentColumnIndexOrHint("giãn");
        if (columnIndex >= 0)
        {
            AutoFitColumn(columnIndex);
        }
    }

    private void AutoFitAllColumns_Click(object sender, RoutedEventArgs e) => AutoFitAllColumns();

    private void AutoFitAllAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AutoFitAllColumns();
    }
}
