using FileTools.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace FileTools.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; } = new();

    public SearchPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 900);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
    }

    /// <summary>Enter trong ô từ khoá = bấm Tìm; Shift+Enter = xuống dòng thêm từ khoá. Bắt ở PreviewKeyDown vì ô nhiều dòng
    /// (AcceptsReturn) tự nuốt Enter trước KeyDown.</summary>
    private void TermsBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Enter && !shift)
        {
            if (ViewModel.SearchCommand.CanExecute(null))
            {
                ViewModel.SearchCommand.Execute(null);
            }
            e.Handled = true;
        }
    }
}
