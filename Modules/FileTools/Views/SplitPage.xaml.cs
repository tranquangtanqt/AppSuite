using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class SplitPage : Page
{
    public SplitViewModel ViewModel { get; } = new();

    public SplitPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 720);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }
}
