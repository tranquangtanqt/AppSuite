using System.Collections.Specialized;
using MeRon.Core.Reports;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MeRon.Views;

public sealed partial class ReportTable : UserControl
{
    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines), typeof(IEnumerable<ReportLine>), typeof(ReportTable), new PropertyMetadata(null, (d, e) => ((ReportTable)d).OnLinesChanged(e)));

    public static readonly DependencyProperty NameHeaderProperty = DependencyProperty.Register(
        nameof(NameHeader), typeof(string), typeof(ReportTable), new PropertyMetadata("Món", (d, _) => ((ReportTable)d).Bindings.Update()));

    public static readonly DependencyProperty QuantityHeaderProperty = DependencyProperty.Register(
        nameof(QuantityHeader), typeof(string), typeof(ReportTable), new PropertyMetadata("Số lượng", (d, _) => ((ReportTable)d).Bindings.Update()));

    public static readonly DependencyProperty ShowTrendProperty = DependencyProperty.Register(
        nameof(ShowTrend), typeof(bool), typeof(ReportTable), new PropertyMetadata(false, (d, _) => ((ReportTable)d).Bindings.Update()));

    public static readonly DependencyProperty TrendHeaderTipProperty = DependencyProperty.Register(
        nameof(TrendHeaderTip), typeof(string), typeof(ReportTable), new PropertyMetadata(null, (d, _) => ((ReportTable)d).Bindings.Update()));

    public ReportTable()
    {
        InitializeComponent();
    }

    /// <summary>Danh sách là ObservableCollection, trang xoá rồi nạp lại nên phải theo dõi để hiện / ẩn lời nhắc trống.</summary>
    private void OnLinesChanged(DependencyPropertyChangedEventArgs e)
    {
        Bindings.Update();
        if (e.OldValue is INotifyCollectionChanged oldLines)
        {
            oldLines.CollectionChanged -= Lines_CollectionChanged;
        }
        if (e.NewValue is INotifyCollectionChanged newLines)
        {
            newLines.CollectionChanged += Lines_CollectionChanged;
        }
        UpdateEmptyText();
    }

    private void Lines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyText();

    private void UpdateEmptyText() =>
        EmptyText.Visibility = Lines?.Any() == true ? Visibility.Collapsed : Visibility.Visible;

    public IEnumerable<ReportLine> Lines
    {
        get => (IEnumerable<ReportLine>)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public string NameHeader
    {
        get => (string)GetValue(NameHeaderProperty);
        set => SetValue(NameHeaderProperty, value);
    }

    public string QuantityHeader
    {
        get => (string)GetValue(QuantityHeaderProperty);
        set => SetValue(QuantityHeaderProperty, value);
    }

    /// <summary>Hiện tiêu đề cột ▲▼ so với kỳ trước (dòng nào có thì <see cref="ReportLine.HasTrend"/>).</summary>
    public bool ShowTrend
    {
        get => (bool)GetValue(ShowTrendProperty);
        set => SetValue(ShowTrendProperty, value);
    }

    public string? TrendHeaderTip
    {
        get => (string?)GetValue(TrendHeaderTipProperty);
        set => SetValue(TrendHeaderTipProperty, value);
    }

    /// <summary>Lên xanh lá, xuống / kỳ này không bán đỏ, món mới xanh dương, bằng: màu chữ thường.</summary>
    public static Brush TrendBrush(TrendKind kind) => kind switch
    {
        TrendKind.Up => (Brush)Application.Current.Resources["GreenBrush"],
        TrendKind.Down or TrendKind.Gone => (Brush)Application.Current.Resources["RedBrush"],
        TrendKind.New => (Brush)Application.Current.Resources["BlueBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };
}
