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
        SharedUI.Helpers.WindowIcon.Apply(this);
        Closed += (_, _) => _helpWindow?.Close(); // đóng app thì đóng luôn cửa sổ Hướng dẫn
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

    // ----- Hướng dẫn (F1) -----

    private SharedUI.Help.HelpWindow? _helpWindow;

    /// <summary>Mở cửa sổ Hướng dẫn (1 cửa sổ duy nhất - đang mở thì đưa lên trước). Nội dung: Views/HelpContent.</summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is null)
        {
            _helpWindow = new SharedUI.Help.HelpWindow("Mcf.CrudDiagram.HtmlGenerator", "CRUD図 (Excel) → trang HTML tra cứu logic nào dùng bảng nào.",
                Views.HelpContent.Sections, searchPlaceholder: "Tìm tính năng, vd: bảng, link, C R U D...");
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        _helpWindow.Activate();
    }
}
