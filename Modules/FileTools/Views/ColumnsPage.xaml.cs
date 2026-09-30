using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class ColumnsPage : Page
{
    public ColumnsViewModel ViewModel { get; } = new();

    public ColumnsPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 860);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }
}
