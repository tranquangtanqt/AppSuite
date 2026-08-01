using Microsoft.UI.Xaml;
using ModuleD.ViewModels;

namespace ModuleD;

/// <summary>
/// Standalone data-dictionary tool for Module D: imports the DBDef Excel workbooks, saves them to
/// SQLite, and exports a browsable static HTML report. Nothing here depends on how this process was
/// started.
/// </summary>
public sealed partial class MainWindow : Window
{
    public ModuleDViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
    }
}
