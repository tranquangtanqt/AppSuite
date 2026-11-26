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
        Ui.KeepTextIfNothingPicked(SchemaBox);
        ScrollFit.Attach(Scroller, Root, 560);
        ViewModel.Confirm = ConfirmAsync;
        ViewModel.ChooseUtf8Target = ChooseUtf8TargetAsync;
        ViewModel.ShowUtf8Result = ShowUtf8ResultAsync;
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

    // Hộp hỏi lại dùng chung với Sao lưu / Phiên kết nối (có khung Production gõ lại tên DB).
    private Task<bool> ConfirmAsync(RunConfirmation confirmation) => Dialogs.ConfirmAsync(XamlRoot, confirmation);

    /// <summary>Hộp chọn chỗ ghi bản UTF-8: thư mục tạm / thư mục dump / ghi đè file gốc (cảnh báo svn).</summary>
    private async Task<Core.Utf8Target?> ChooseUtf8TargetAsync(int need, int total, Core.Utf8Target current)
    {
        var dump = Path.Combine(AppState.Settings.DumpFolder, "UTF8");
        var options = new RadioButtons();
        string[] labels =
        [
            $"Lưu bản UTF-8 vào thư mục tạm: {Path.Combine(Path.GetTempPath(), "AppSuite", "DbTools", "UTF8")}\\<thư mục>_<giờ>",
            $"Lưu bản UTF-8 vào thư mục dump: {dump}\\<thư mục>_<giờ>",
            "Ghi đè file gốc (chỉ các file không phải UTF-8)",
        ];
        foreach (var label in labels)
        {
            options.Items.Add(new RadioButton { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap } });
        }
        options.SelectedIndex = (int)current;
        var overwriteNote = new TextBlock
        {
            Text = "⚠ Ghi đè: file trong svn sẽ thành \"đã sửa\" (svn diff thấy cả file đổi). Không hoàn tác trong app được - muốn lấy lại bản cũ thì svn revert.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xD9, 0x77, 0x06)),
            Visibility = current == Core.Utf8Target.Overwrite ? Visibility.Visible : Visibility.Collapsed,
        };
        options.SelectionChanged += (_, _) => overwriteNote.Visibility = options.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(options, "Utf8TargetOptions");
        var panel = new StackPanel { Spacing = 10, MaxWidth = 560 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{need} / {total} file trong danh sách không phải UTF-8 (Shift-JIS...). Tạo bản UTF-8 (không BOM) để mở / chạy bằng psql, DBeaver... "
                   + "DbTools tự chạy các file này được, không cần bước này. Dòng \\encoding SJIS (nếu có) đổi thành \\encoding UTF8.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(options);
        panel.Children.Add(overwriteNote);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Chuyển sang UTF-8",
            Content = SharedUI.Helpers.DialogContent.Scrollable(panel),
            PrimaryButtonText = "Chuyển",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && options.SelectedIndex >= 0
            ? (Core.Utf8Target)options.SelectedIndex
            : null;
    }

    /// <summary>Kết quả Chuyển UTF-8, nút Mở thư mục (thư mục bản UTF-8, hoặc thư mục script khi ghi đè).</summary>
    private async Task ShowUtf8ResultAsync(string title, string message, string folder)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = SharedUI.Helpers.DialogContent.Scrollable(message),
            PrimaryButtonText = "Mở thư mục",
            CloseButtonText = "Đóng",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Services.Shell.Open(folder);
        }
    }

    private void ManageConnections_Click(object sender, RoutedEventArgs e) => App.MainWindow?.ShowPage("ConnectionsPage");

    /// <summary>Mở ô Schema → đọc lại danh sách schema của DB (có thể vừa tạo schema mới).</summary>
    private async void SchemaBox_DropDownOpened(object sender, object e) => await ViewModel.LoadSchemasAsync();

    /// <summary>ComboBox gõ được không đẩy chữ rỗng qua binding (xoá trống để về mặc định không lưu) → chốt tay khi Enter /
    /// rời ô.</summary>
    private void SchemaBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args) => ViewModel.SchemaInput = args.Text;

    private void SchemaBox_Loaded(object sender, RoutedEventArgs e) => Ui.RefreshEditableText(sender, e);

    private void SchemaBox_LostFocus(object sender, RoutedEventArgs e) => ViewModel.SchemaInput = SchemaBox.Text;

    /// <summary>Nút ✕: về schema mặc định. Xoá chữ trong ComboBox gõ được không đủ - nó tự trả lại chữ của mục đang chọn.</summary>
    private void ClearSchema_Click(object sender, RoutedEventArgs e)
    {
        Ui.ClearEditable(SchemaBox);
        ViewModel.SchemaInput = string.Empty;
    }

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
