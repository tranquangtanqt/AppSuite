using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class ExtractPage : Page
{
    public ExtractViewModel ViewModel { get; } = new();

    public ExtractPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 700);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }
}
