using DbTools.Services;
using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DbTools.Views;

public sealed partial class BackupPage : Page
{
    public BackupViewModel ViewModel { get; } = new();

    public BackupPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 560);
        ViewModel.Confirm = ConfirmAsync;
        ViewModel.PickFile = () => Shell.PickFileAsync(".dump", ".backup", ".sql", ".tar", "*");
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfiles();
    }

    private Task<bool> ConfirmAsync(RunConfirmation confirmation) => Dialogs.ConfirmAsync(XamlRoot, confirmation);

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.Tab = sender.Items.IndexOf(sender.SelectedItem);

    private void DbBox_Loaded(object sender, RoutedEventArgs e) => Ui.RefreshEditableText(sender, e);

    private async void DumpDbBox_DropDownOpened(object sender, object e) =>
        await ViewModel.LoadDatabasesAsync(ViewModel.DumpProfile, ViewModel.DumpDatabases);

    private async void RestoreDbBox_DropDownOpened(object sender, object e) =>
        await ViewModel.LoadDatabasesAsync(ViewModel.RestoreProfile, ViewModel.RestoreDatabases);

    private async void CopySourceDbBox_DropDownOpened(object sender, object e) =>
        await ViewModel.LoadDatabasesAsync(ViewModel.CopySourceProfile, ViewModel.CopySourceDatabases);

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LogBox.FindDescendant<ScrollViewer>() is { } viewer)
        {
            viewer.ChangeView(null, viewer.ScrollableHeight, null, disableAnimation: true);
        }
    }
}

/// <summary>Hộp thoại hỏi lại dùng chung (Chạy, Restore, Copy, Ngắt phiên): nút mặc định là Huỷ để Enter không vô tình chạy.</summary>
internal static class Dialogs
{
    public static async Task<bool> ConfirmAsync(XamlRoot root, RunConfirmation confirmation)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 560 };
        foreach (var line in confirmation.Lines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                FontWeight = line.StartsWith('⚠') ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            });
        }
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = confirmation.Title,
            Content = new ScrollViewer { Content = panel, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = confirmation.PrimaryText,
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
