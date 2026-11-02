using FileTools.Core;
using FileTools.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
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
        // handledEventsToo: chữ trong dòng chọn được (IsTextSelectionEnabled) tự nuốt bấm đúp; ListView nuốt Enter.
        HitList.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler((_, e) => ExtractAround(e.OriginalSource)), true);
        HitList.AddHandler(KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                ExtractAround(HitList.SelectedItem);
                e.Handled = true;
            }
        }), true);
    }

    /// <summary>Trích ±N dòng quanh dòng kết quả được bấm đúp / Enter.</summary>
    private void ExtractAround(object? source)
    {
        var hit = source as SearchHit ?? (source as FrameworkElement)?.DataContext as SearchHit ?? HitList.SelectedItem as SearchHit;
        if (hit is not null && ViewModel.ExtractAroundCommand.CanExecute(hit))
        {
            ViewModel.ExtractAroundCommand.Execute(hit);
        }
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
