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

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Thay toàn bộ dữ liệu?", ViewModel.ConfirmText, "Nhập, thay toàn bộ"))
        {
            await ViewModel.ImportCommand.ExecuteAsync(null);
        }
    }
}
