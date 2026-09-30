using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class ReplacePage : Page
{
    public ReplaceViewModel ViewModel { get; } = new();

    public ReplacePage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 900);
    }
}
