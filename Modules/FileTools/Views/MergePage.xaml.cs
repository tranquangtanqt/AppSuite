using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class MergePage : Page
{
    public MergeViewModel ViewModel { get; } = new();

    public MergePage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 780);
        DropHelper.Attach(Root, ViewModel.AddPaths);
    }
}
