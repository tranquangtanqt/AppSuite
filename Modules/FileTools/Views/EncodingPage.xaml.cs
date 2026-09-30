using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class EncodingPage : Page
{
    public EncodingViewModel ViewModel { get; } = new();

    public EncodingPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 860);
    }
}
