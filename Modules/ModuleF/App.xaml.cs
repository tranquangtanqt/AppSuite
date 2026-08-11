using System.Text;
using Microsoft.UI.Xaml;

namespace ModuleF;

/// <summary>
/// Standalone entry point for Module F. Runs identically whether launched directly by a developer
/// or started by MainLauncher's ProcessManager - it never references MainLauncher.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // .NET has dropped legacy code pages (Shift-JIS/932 included) from its default Encoding
        // table - GetEncoding(932) throws NotSupportedException without this. Must run before
        // EncodingPickerDialog ever resolves Shift-JIS.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
