using MeRon.Core.Import;
using MeRon.Services;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MeRon.Views;

public sealed partial class ImportPage : Page
{
    public ImportViewModel ViewModel { get; } = new();

    public ImportPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel.RefreshGoogle();
    }

    private async void Pick_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            await ViewModel.SetFolderAsync(folder);
        }
    }

    private async void Folder_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.CheckCommand.CanExecute(null))
        {
            e.Handled = true;
            await ViewModel.SetFolderAsync(ViewModel.FolderPath.Trim().Trim('"'));
        }
    }

    /// <summary>Link folder Google Drive vừa dán / gõ xong trong ô → đọc luôn, khỏi phải bấm Enter. Chỉ khi ô đang có con trỏ:
    /// mở trang (ô điền sẵn link lần trước) không tự tải lại.</summary>
    private async void Folder_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { FocusState: not FocusState.Unfocused } box)
        {
            return;
        }
        var text = box.Text.Trim().Trim('"');
        if (text == _autoChecked || GoogleDriveBackupSource.ParseFolderId(text) is null || !ViewModel.CheckCommand.CanExecute(null))
        {
            return;
        }
        _autoChecked = text;
        await ViewModel.SetFolderAsync(text);
    }

    private string? _autoChecked;

    private async void SignIn_Click(object sender, RoutedEventArgs e) => await ViewModel.SignInAsync();

    private async void SignOut_Click(object sender, RoutedEventArgs e) => await ViewModel.SignOutAsync();

    private async void LoadDrive_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadDriveBackupsAsync();

    private async void FolderBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BackupChoiceRow row })
        {
            await ViewModel.CheckFolderBackupAsync(row);
        }
    }

    private async void DriveBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DriveBackupRow row })
        {
            await ViewModel.CheckDriveBackupAsync(row);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Thay toàn bộ dữ liệu?", ViewModel.ConfirmText, "Nhập, thay toàn bộ"))
        {
            await ViewModel.ImportCommand.ExecuteAsync(null);
        }
    }
}
