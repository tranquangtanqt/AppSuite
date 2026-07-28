using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModuleB.Models;
using ModuleB.Services;
using ModuleB.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ModuleB;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly Window _ownerWindow;

    public SettingsViewModel ViewModel { get; }

    public string? SelectedPath => ViewModel.CurrentPath;

    public SettingsDialog(Window ownerWindow, string? initialPath)
    {
        _ownerWindow = ownerWindow;
        ViewModel = new SettingsViewModel(new SavedFolderRepository(), initialPath);
        InitializeComponent();
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(_ownerWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.CurrentPath = folder.Path;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveCurrentPath();
    }

    private void SavedFoldersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedFoldersListView.SelectedItem is SavedFolder folder)
        {
            ViewModel.CurrentPath = folder.Path;
        }
    }
}
