using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    // Set when a saved folder is double-tapped, since Hide() alone always resolves
    // ShowAsync() with ContentDialogResult.None - the caller checks this flag alongside
    // the result to treat a double-tap the same as clicking the built-in OK button.
    public bool ConfirmedByDoubleTap { get; private set; }

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

    private void SavedFoldersListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SavedFoldersListView.SelectedItem is not SavedFolder folder)
        {
            return;
        }

        ViewModel.CurrentPath = folder.Path;
        ConfirmedByDoubleTap = true;
        Hide();
    }

    private void EditFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SavedFolder folder })
        {
            ViewModel.BeginEdit(folder);
        }
    }

    private void DeleteFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SavedFolder folder })
        {
            ViewModel.DeleteFolder(folder);
        }
    }
}
