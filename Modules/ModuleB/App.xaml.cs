using System;
using Microsoft.UI.Xaml;

namespace ModuleB;

/// <summary>
/// Standalone entry point for Module B. Runs identically whether launched directly by a developer
/// or started by MainLauncher's ProcessManager - it never references MainLauncher.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Without this, any unhandled exception (eg. a filesystem error while walking a folder tree)
        // silently terminates the whole unpackaged WinUI process instead of surfacing an error.
        e.Handled = true;
        System.Diagnostics.Debug.WriteLine(e.Exception);
    }
}
