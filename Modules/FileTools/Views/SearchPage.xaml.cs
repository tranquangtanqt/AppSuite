using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FileTools.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; } = new();

    public SearchPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 780);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }

    /// <summary>Enter trong ô tìm = bấm Tìm.</summary>
    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SearchCommand.CanExecute(null))
        {
            ViewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
