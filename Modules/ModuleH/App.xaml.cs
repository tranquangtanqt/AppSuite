using Microsoft.UI.Xaml;

namespace ModuleH;

/// <summary>
/// Standalone entry point for Module H. Runs identically whether launched directly by a developer
/// or started by MainLauncher's ProcessManager - it never references MainLauncher.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
