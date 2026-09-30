using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class FilterPage : Page
{
    public FilterViewModel ViewModel { get; } = new();

    public FilterPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 760);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }
}
