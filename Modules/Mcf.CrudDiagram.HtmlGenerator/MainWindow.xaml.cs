using Microsoft.UI.Xaml;
using Mcf.CrudDiagram.HtmlGenerator.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Mcf.CrudDiagram.HtmlGenerator;

/// <summary>
/// Standalone CRUD図 documentation tool for Module H: imports every CRUD図 workbook under a
/// user-chosen folder, saves 1 record per logic sheet to SQLite, and exports a browsable static HTML
/// site. Nothing here depends on how this process was started.
/// </summary>
public sealed partial class MainWindow : Window
{
    public McfCrudDiagramHtmlGeneratorViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        ViewModel.SetSourceFolder(folder.Path);
    }
}
