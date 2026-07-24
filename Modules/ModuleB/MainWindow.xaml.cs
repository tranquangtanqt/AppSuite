using System;
using Microsoft.UI.Xaml;

namespace ModuleB;

/// <summary>
/// Shows the PID and command-line arguments this instance was started with, so it is easy to see
/// MainLauncher's ProcessManager passing arguments through (per modules.json's "Arguments" field).
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var args = Environment.GetCommandLineArgs();
        var passedArguments = args.Length > 1 ? string.Join(' ', args[1..]) : "(none)";

        ProcessIdText.Text = $"Process ID: {Environment.ProcessId}";
        ArgumentsText.Text = $"Arguments received: {passedArguments}";
    }
}
