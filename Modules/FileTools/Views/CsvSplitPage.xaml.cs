using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class CsvSplitPage : Page
{
    public CsvSplitViewModel ViewModel { get; } = new();

    public CsvSplitPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 620);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }
}
