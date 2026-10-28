using FileTools.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

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

    /// <summary>Alt+↑ / Alt+↓ trong danh sách = Lên / Xuống. Phải bắt ở Preview: ListView coi Alt+↓ như ↓ (chuyển dòng chọn)
    /// nên KeyboardAccelerator trên nút không bao giờ chạy.</summary>
    private void FileList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Up or VirtualKey.Down)
            || !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))
        {
            return;
        }
        var command = e.Key == VirtualKey.Up ? ViewModel.MoveUpCommand : ViewModel.MoveDownCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
            if (FileList.SelectedItem is { } item)
            {
                (FileList.ContainerFromItem(item) as ListViewItem)?.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
            }
        }
        e.Handled = true;
    }
}
