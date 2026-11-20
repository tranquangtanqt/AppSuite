using DbTools.Core;
using DbTools.Services;
using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
        ViewModel.ShowCommands = ShowCommandsAsync;
    }

    /// <summary>Hộp thoại Xem lệnh: script chọn / bôi đen được, nút Copy chép cả script vào clipboard.</summary>
    private async Task ShowCommandsAsync(string title, string text)
    {
        // AcceptsReturn phải bật TRƯỚC khi gán Text: TextBox 1 dòng cắt mất mọi thứ sau dấu xuống dòng đầu tiên.
        var box = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            MinWidth = 640,
            MaxHeight = 380,
        };
        box.Text = text;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "CommandPreviewBox");
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = box,
            PrimaryButtonText = "Copy",
            CloseButtonText = "Đóng",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900.0;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            ViewModel.Status = "Đã copy các câu lệnh (dán vào file .bat, điền mật khẩu ở dòng set PGPASSWORD để chạy tay).";
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfiles();
    }

    private Task<bool> ConfirmAsync(RunConfirmation confirmation) => Dialogs.ConfirmAsync(XamlRoot, confirmation);

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.Tab = sender.Items.IndexOf(sender.SelectedItem);
        // Ô database của tab đang ẩn lúc trang load chưa dựng xong nên đặt chữ lúc đó không hiện → đặt lại khi tab hiện ra.
        ComboBox? box = ViewModel.Tab switch { 0 => DumpDbBox, 1 => RestoreDbBox, 2 => CopySourceDbBox, _ => null };
        if (box is not null)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Ui.RefreshEditableText(box, new RoutedEventArgs()));
        }
    }

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

/// <summary>Hộp thoại hỏi lại dùng chung (Chạy, Restore, Copy, Ngắt phiên): nút mặc định là Huỷ để Enter không vô tình chạy.
/// Hồ sơ Production (<see cref="RunConfirmation.TypeToConfirm"/>): khung đỏ + ô gõ lại tên, nút chỉ bật khi gõ đúng.</summary>
internal static class Dialogs
{
    private static readonly Brush ProductionRed = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xDC, 0x26, 0x26));

    public static async Task<bool> ConfirmAsync(XamlRoot root, RunConfirmation confirmation)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 560 };
        TextBox? nameBox = null;
        if (confirmation.TypeToConfirm is { } expected)
        {
            nameBox = new TextBox { PlaceholderText = expected, IsSpellCheckEnabled = false };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(nameBox, "ProductionConfirmBox");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nameBox, "Gõ lại tên để xác nhận");
            panel.Children.Add(new Border
            {
                BorderBrush = ProductionRed,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 12),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "🔴 PRODUCTION - hồ sơ quan trọng",
                            Foreground = ProductionRed,
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        },
                        new TextBlock { Text = $"Gõ lại \"{expected}\" để bật nút {confirmation.PrimaryText}.", TextWrapping = TextWrapping.Wrap },
                        nameBox,
                    },
                },
            });
        }
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
            Title = (nameBox is null ? string.Empty : "🔴 ") + confirmation.Title,
            Content = new ScrollViewer { Content = panel, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = confirmation.PrimaryText,
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close, // Enter không vô tình chạy thật
            IsPrimaryButtonEnabled = nameBox is null,
        };
        if (nameBox is not null)
        {
            var name = confirmation.TypeToConfirm!;
            nameBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = ProductionGuard.Matches(nameBox.Text, name);
            nameBox.Loaded += (_, _) => nameBox.Focus(FocusState.Programmatic);
        }
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
