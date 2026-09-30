using FileTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class OutputPanel : UserControl
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(
        nameof(Settings), typeof(OutputSettings), typeof(OutputPanel), new PropertyMetadata(null, (d, _) => ((OutputPanel)d).Bindings.Update()));

    public OutputPanel() => InitializeComponent();

    public OutputSettings? Settings
    {
        get => (OutputSettings?)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }
}
