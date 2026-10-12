using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MeRon.Views;

public sealed partial class ReportTable : UserControl
{
    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines), typeof(IEnumerable<ReportLine>), typeof(ReportTable), new PropertyMetadata(null, (d, _) => ((ReportTable)d).Bindings.Update()));

    public static readonly DependencyProperty NameHeaderProperty = DependencyProperty.Register(
        nameof(NameHeader), typeof(string), typeof(ReportTable), new PropertyMetadata("Món", (d, _) => ((ReportTable)d).Bindings.Update()));

    public static readonly DependencyProperty QuantityHeaderProperty = DependencyProperty.Register(
        nameof(QuantityHeader), typeof(string), typeof(ReportTable), new PropertyMetadata("Số lượng", (d, _) => ((ReportTable)d).Bindings.Update()));

    public ReportTable()
    {
        InitializeComponent();
    }

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
}
