using FileTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class CsvSourcePanel : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(CsvSourceViewModel), typeof(CsvSourcePanel), new PropertyMetadata(null, (d, _) => ((CsvSourcePanel)d).Bindings.Update()));

    public CsvSourcePanel() => InitializeComponent();

    public CsvSourceViewModel? Source
    {
        get => (CsvSourceViewModel?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
}
