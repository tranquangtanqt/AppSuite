using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModuleC.Models;
using ModuleC.Services;
using ModuleC.ViewModels;

namespace ModuleC;

public sealed partial class MainWindow : Window
{
    private readonly TableColumnCatalog _catalog = new();

    public SearchViewModel ViewModel { get; }
    public TableNameSearchViewModel TableNameSearchViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        Closed += (_, _) => _helpWindow?.Close(); // đóng app thì đóng luôn cửa sổ Hướng dẫn

        ViewModel = new SearchViewModel(_catalog);
        TableNameSearchViewModel = new TableNameSearchViewModel(_catalog);

        _ = ViewModel.InitializeAsync();
    }

    private void RemoveRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AliasRow row })
        {
            ViewModel.RemoveRowCommand.Execute(row);
        }
    }

    private async void OpenTableNameSearch_Click(object sender, RoutedEventArgs e)
    {
        TableNameSearchDialog.XamlRoot = Content.XamlRoot;
        await TableNameSearchDialog.ShowAsync();
    }

    private void CloseTableNameSearchDialog_Click(object sender, RoutedEventArgs e)
    {
        TableNameSearchDialog.Hide();
    }

    // ----- Hướng dẫn (F1) -----

    private SharedUI.Help.HelpWindow? _helpWindow;

    /// <summary>Mở cửa sổ Hướng dẫn (1 cửa sổ duy nhất - đang mở thì đưa lên trước). Nội dung: Views/HelpContent.</summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is null)
        {
            _helpWindow = new SharedUI.Help.HelpWindow("ModuleC - Tìm kiếm", "Đổi danh sách cột theo nhãn tiếng Nhật (Alias.Nhãn) sang tên cột thật theo từ điển dữ liệu.",
                Views.HelpContent.Sections, searchPlaceholder: "Tìm tính năng, vd: alias, copy, tên bảng...");
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        _helpWindow.Activate();
    }
}
