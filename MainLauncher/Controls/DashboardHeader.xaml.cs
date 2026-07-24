using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MainLauncher.Controls;

public sealed partial class DashboardHeader : UserControl
{
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(DashboardHeader), new PropertyMetadata(string.Empty));

    public DashboardHeader()
    {
        InitializeComponent();
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
