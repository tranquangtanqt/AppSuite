using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using ModuleF.Models;
using ModuleF.ViewModels;

namespace ModuleF.Views;

/// <summary>
/// Modal-only Find/Replace: the dialog itself drives ViewModel.Find/NextMatch/PreviousMatch and shows
/// match progress ("Kết quả N/M"), then raises <see cref="MatchNavigated"/> so MainWindow scrolls the
/// grid to the current match while the dialog stays open (ContentDialog blocks the grid, but we still
/// want the caller to end up looking at the right cell once it closes).
/// </summary>
public sealed partial class FindReplaceDialog : ContentDialog
{
    private readonly ModuleFViewModel _viewModel;
    private readonly DispatcherQueueTimer _debounceTimer;

    public event EventHandler<CellRef>? MatchNavigated;

    public FindReplaceDialog(ModuleFViewModel viewModel)
    {
        // Assigned before InitializeComponent(): ModeComboBox's XAML-declared SelectedIndex="0" fires
        // ModeComboBox_SelectionChanged synchronously while InitializeComponent() runs, which calls
        // RunFind() - with _viewModel still null at that point, that threw a NullReferenceException on
        // the UI thread and crashed the app every time this dialog opened.
        _viewModel = viewModel;
        InitializeComponent();

        _debounceTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            RunFind();
        };

        // Set after InitializeComponent() has fully returned (all named elements connected), not as a
        // XAML-declared SelectedIndex="0" - that would fire ModeComboBox_SelectionChanged mid-parse,
        // while later-declared elements like MatchStatusText aren't wired up yet.
        ModeComboBox.SelectedIndex = 0;
    }

    private void FindTextBox_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RunFind();

    private void CaseSensitiveCheckBox_Changed(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => RunFind();

    private void RunFind()
    {
        var mode = (SearchMode)ModeComboBox.SelectedIndex;
        _viewModel.Find(FindTextBox.Text, mode, CaseSensitiveCheckBox.IsChecked == true);
        MatchStatusText.Text = _viewModel.MatchStatusMessage;
        if (_viewModel.CurrentMatch is { } match)
        {
            MatchNavigated?.Invoke(this, match);
        }
    }

    private void NextButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_viewModel.NextMatch() is { } match)
        {
            MatchStatusText.Text = _viewModel.MatchStatusMessage;
            MatchNavigated?.Invoke(this, match);
        }
    }

    private void PreviousButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_viewModel.PreviousMatch() is { } match)
        {
            MatchStatusText.Text = _viewModel.MatchStatusMessage;
            MatchNavigated?.Invoke(this, match);
        }
    }

    private void ReplaceAllButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var count = _viewModel.ReplaceAllMatches(ReplaceTextBox.Text);
        ReplaceStatusText.Text = $"Đã thay {count} ô.";
        MatchStatusText.Text = _viewModel.MatchStatusMessage;
    }
}
