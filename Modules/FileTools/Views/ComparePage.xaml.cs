using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class ComparePage : Page
{
    public CompareViewModel ViewModel { get; } = new();

    public ComparePage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 760);
        DropHelper.Attach(Root, ViewModel.AddPaths);
    }
}
