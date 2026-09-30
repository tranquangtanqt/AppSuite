using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class InfoPage : Page
{
    public InfoViewModel ViewModel { get; } = new();

    public InfoPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 620);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }
}
