using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class DelimiterPage : Page
{
    public DelimiterViewModel ViewModel { get; } = new();

    public DelimiterPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 560);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }
}
