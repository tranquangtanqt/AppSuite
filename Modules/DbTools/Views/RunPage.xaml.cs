using DbTools.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace DbTools.Views;

public sealed partial class RunPage : Page
{
    public RunViewModel ViewModel { get; } = new();

    public RunPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 560);
        ViewModel.Confirm = ConfirmAsync;
        Loaded += async (_, _) =>
        {
            // Mở app lại: tự đọc thư mục lần trước (chỉ đọc file, không đụng DB).
            if (ViewModel.IsEmpty && Directory.Exists(ViewModel.Folder) && ViewModel.LoadFolderCommand.CanExecute(null))
            {
                await ViewModel.LoadFolderCommand.ExecuteAsync(null);
            }
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
    }

    private async Task<bool> ConfirmAsync(RunConfirmation confirmation)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 560 };
        foreach (var line in confirmation.Lines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                FontWeight = line.StartsWith('⚠') ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            });
        }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = confirmation.Title,
            Content = new ScrollViewer { Content = panel, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = confirmation.PrimaryText,
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close, // Enter không vô tình chạy thật
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ManageConnections_Click(object sender, RoutedEventArgs e) => App.MainWindow?.ShowPage("ConnectionsPage");

    private void FolderBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.LoadFolderCommand.CanExecute(null))
        {
            ViewModel.LoadFolderCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Nhật ký mới ghi thêm → cuộn xuống cuối.</summary>
    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LogBox.FindDescendant<ScrollViewer>() is { } viewer)
        {
            viewer.ChangeView(null, viewer.ScrollableHeight, null, disableAnimation: true);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) && ViewModel.IsIdle)
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = "Đọc thư mục này";
        }
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) || !ViewModel.IsIdle)
        {
            return;
        }
        var items = await e.DataView.GetStorageItemsAsync();
        var folder = items.OfType<StorageFolder>().FirstOrDefault()?.Path
            ?? (items.OfType<StorageFile>().FirstOrDefault() is { } file ? Path.GetDirectoryName(file.Path) : null);
        if (folder is not null)
        {
            ViewModel.Folder = folder;
            await ViewModel.LoadFolderCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Alt+↑ / Alt+↓ trong danh sách = Lên / Xuống (bắt ở Preview: ListView coi Alt+↓ như ↓).</summary>
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
                (FileList.ContainerFromItem(item) as ListViewItem)?.Focus(FocusState.Keyboard);
            }
        }
        e.Handled = true;
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindDescendant<T>(this DependencyObject root) where T : DependencyObject
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }
            if (child.FindDescendant<T>() is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
