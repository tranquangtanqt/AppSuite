using Microsoft.UI.Xaml;
using ScreenCapture.Views;

namespace ScreenCapture;

/// <summary>
/// Standalone entry point for the ScreenCapture module. Runs identically whether launched directly by
/// a developer or started by MainLauncher's ProcessManager - it never references MainLauncher.
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
        _window = new CaptureLauncherWindow();
        _window.Activate();
    }
}
