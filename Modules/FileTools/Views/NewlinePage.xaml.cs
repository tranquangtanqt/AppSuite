using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class NewlinePage : Page
{
    public NewlineViewModel ViewModel { get; } = new();

    public NewlinePage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 820);
    }
}
