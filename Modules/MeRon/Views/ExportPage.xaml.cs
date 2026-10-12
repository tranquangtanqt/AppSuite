using System.Diagnostics;
using MeRon.Services;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class ExportPage : Page
{
    public ExportViewModel ViewModel { get; } = new();

    public ExportPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.RefreshCounts();

    private async void Pick_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            ViewModel.FolderPath = folder;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e) => await ViewModel.ExportAsync();

    private void OpenExport_Click(object sender, RoutedEventArgs e) => OpenFolder(ViewModel.LastExport);

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ViewModel.BackupsFolder);
        OpenFolder(ViewModel.BackupsFolder);
    }

    private static void OpenFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }

    /// <summary>Xác nhận 2 lần liên tiếp như điện thoại.</summary>
    private async void Wipe_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Xoá toàn bộ dữ liệu?", ViewModel.WipeQuestion1, "Tiếp tục")
            && await Dialogs.ConfirmAsync(XamlRoot, "Xác nhận lần 2", ExportViewModel.WipeQuestion2, "Xoá toàn bộ"))
        {
            ViewModel.Wipe();
        }
    }
}
