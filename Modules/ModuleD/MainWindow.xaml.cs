using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModuleD;

/// <summary>
/// Simple standalone data-entry form for Module D. No business logic besides local
/// validation/clearing - nothing here depends on how this process was started.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowResult("Vui long nhap ho ten.", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(EmailBox.Text))
        {
            ShowResult("Vui long nhap email.", InfoBarSeverity.Warning);
            return;
        }

        ShowResult($"Da luu thong tin cho \"{NameBox.Text}\".", InfoBarSeverity.Success);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        NameBox.Text = string.Empty;
        EmailBox.Text = string.Empty;
        NoteBox.Text = string.Empty;
        ResultInfoBar.IsOpen = false;
    }

    private void ShowResult(string message, InfoBarSeverity severity)
    {
        ResultInfoBar.Severity = severity;
        ResultInfoBar.Message = message;
        ResultInfoBar.IsOpen = true;
    }
}
