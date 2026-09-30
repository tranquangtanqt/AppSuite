using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class DedupePage : Page
{
    public DedupeViewModel ViewModel { get; } = new();

    public DedupePage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 720);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }
}
