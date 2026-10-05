using Microsoft.UI.Xaml;
using Mcf.DbDef.HtmlGenerator.ViewModels;

namespace Mcf.DbDef.HtmlGenerator;

/// <summary>
/// Standalone data-dictionary tool for Module D: imports the DBDef Excel workbooks, saves them to
/// SQLite, and exports a browsable static HTML report. Nothing here depends on how this process was
/// started.
/// </summary>
public sealed partial class MainWindow : Window
{
    public McfDbDefHtmlGeneratorViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        Closed += (_, _) => _helpWindow?.Close(); // đóng app thì đóng luôn cửa sổ Hướng dẫn
    }

    // ----- Hướng dẫn (F1) -----

    private SharedUI.Help.HelpWindow? _helpWindow;

    /// <summary>Mở cửa sổ Hướng dẫn (1 cửa sổ duy nhất - đang mở thì đưa lên trước). Nội dung: Views/HelpContent.</summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is null)
        {
            _helpWindow = new SharedUI.Help.HelpWindow("Mcf.DbDef.HtmlGenerator", "Từ điển dữ liệu từ workbook DBDef (Excel) → trang HTML tra cứu bảng / cột / khoá ngoại.",
                Views.HelpContent.Sections, searchPlaceholder: "Tìm tính năng, vd: tên cột, khoá ngoại, $...$");
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        _helpWindow.Activate();
    }
}
