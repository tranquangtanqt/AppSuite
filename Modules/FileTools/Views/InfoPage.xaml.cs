using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class InfoPage : Page
{
    public InfoViewModel ViewModel { get; } = new();

    public InfoPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 960);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }

    private void Mode_Checked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => ViewModel.ModeIndex = ModeChoice.IndexOf(sender);
}
