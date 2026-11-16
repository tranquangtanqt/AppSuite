using MeRon.Core;
using MeRon.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>Cài đặt: ẩn / hiện từng mục menu trái (đổi ô tích là lưu + áp dụng lên menu ngay); số ngày giữ ảnh chụp.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private static MainWindow? Window => App.MainWindow;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Build();
        _loadingTheme = true;
        ThemeChoice.SelectedIndex = (int)(Window?.MenuSettings.Theme ?? AppTheme.System);
        _loadingTheme = false;
        ShowRestart();
        _loadingCapture = true;
        KeepDaysBox.Value = Window?.MenuSettings.CaptureKeepDays ?? MenuSettings.DefaultCaptureKeepDays;
        _loadingCapture = false;
        ShowCaptureStatus();
        _loadingUnpin = true;
        AskUnpinBox.IsChecked = Window?.MenuSettings.AskBeforeUnpin ?? false;
        _loadingUnpin = false;
        _loadingHistory = true;
        HistoryDaysBox.Value = Window?.MenuSettings.HistoryKeepDays ?? MenuSettings.DefaultHistoryKeepDays;
        _loadingHistory = false;
        ShowHistoryStatus();
        _loadingBackup = true;
        DailyBackupBox.Value = Window?.MenuSettings.DailyBackupKeep ?? Core.Data.MeRonDatabase.KeepDailyCopies;
        AutoDailyBox.IsChecked = Window?.MenuSettings.AutoDailyBackup ?? true;
        BeforeActionsBox.IsChecked = Window?.MenuSettings.BackupBeforeActions ?? true;
        DailyKeepPanel.Visibility = AutoDailyBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _loadingBackup = false;
        ShowBackups();
    }

    // ---------- Sao lưu tự động ----------

    private bool _loadingBackup;

    /// <summary>2 ô tích Tự sao lưu mỗi ngày / khi thao tác: đổi là lưu ngay. Tắt không xoá các bản đã có.</summary>
    private void BackupOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingBackup || Window is not { } window)
        {
            return;
        }
        window.SetBackupOptions(AutoDailyBox.IsChecked == true, BeforeActionsBox.IsChecked == true);
        DailyKeepPanel.Visibility = AutoDailyBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Đổi số bản là lưu + xoá ngay bản hằng ngày thừa; ô trống / số lạ → về mặc định 14.</summary>
    private void DailyBackup_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingBackup || Window is not { } window)
        {
            return;
        }
        var keep = double.IsNaN(args.NewValue) ? Core.Data.MeRonDatabase.KeepDailyCopies : (int)Math.Round(args.NewValue);
        window.SetDailyBackupKeep(keep);
        if (sender.Value != window.MenuSettings.DailyBackupKeep)
        {
            sender.Value = window.MenuSettings.DailyBackupKeep;
        }
        ShowBackups();
    }

    private async void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        var folder = Services.AppServices.Database.BackupsFolder;
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Không mở được thư mục", ex.Message);
        }
    }

    /// <summary>Danh sách bản sao (mới nhất trước): loại, thời gian, số đơn / khách, dung lượng, nút Khôi phục.</summary>
    private void ShowBackups(string? result = null)
    {
        BackupList.Children.Clear();
        IReadOnlyList<Core.Data.BackupCopy> copies;
        try
        {
            copies = Services.AppServices.Database.ListCopies();
        }
        catch (Exception ex)
        {
            BackupStatus.Text = "Không đọc được thư mục Backups: " + ex.Message;
            return;
        }
        var daily = copies.Count(c => c.Reason == Core.Data.MeRonDatabase.DailyCopyReason);
        BackupStatus.Text = (result is null ? "" : result + "  ") +
            (copies.Count == 0 ? "Chưa có bản sao nào." : $"Có {VnFormat.Number(copies.Count)} bản sao ({VnFormat.Number(daily)} bản hằng ngày).");
        foreach (var copy in copies)
        {
            BackupList.Children.Add(BackupRow(copy));
        }
    }

    private Grid BackupRow(Core.Data.BackupCopy copy)
    {
        var counts = Core.Data.MeRonDatabase.CountIn(copy.Path);
        var detail = (counts is { } c ? $"{VnFormat.Number(c.Orders)} đơn · {VnFormat.Number(c.Customers)} khách" : "không đọc được")
                     + $" · {copy.Size / 1024.0 / 1024.0:0.0} MB";
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var time = new TextBlock { Text = copy.Time.ToString("dd/MM/yyyy HH:mm"), VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var info = new TextBlock { Text = $"{copy.Label} · {detail}", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        Grid.SetColumn(info, 1);
        var restore = new Button { Content = "Khôi phục", Tag = copy, IsEnabled = counts is not null };
        restore.Click += Restore_Click;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(restore, $"Khôi phục bản {copy.Time:dd/MM/yyyy HH:mm} {copy.Label}");
        Grid.SetColumn(restore, 2);
        row.Children.Add(time);
        row.Children.Add(info);
        row.Children.Add(restore);
        return row;
    }

    /// <summary>Hỏi lại → thay toàn bộ dữ liệu bằng bản sao (dữ liệu hiện tại chép ra bản "Trước khi lấy lại bản sao") → mở lại app
    /// để mọi trang đọc dữ liệu mới.</summary>
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Core.Data.BackupCopy copy })
        {
            return;
        }
        var counts = Core.Data.MeRonDatabase.CountIn(copy.Path);
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Khôi phục bản sao lưu",
                $"Thay TOÀN BỘ dữ liệu hiện tại bằng bản sao lúc {copy.Time:dd/MM/yyyy HH:mm} ({copy.Label}" +
                (counts is { } c ? $", {VnFormat.Number(c.Orders)} đơn, {VnFormat.Number(c.Customers)} khách" : "") + ").\n\n" +
                "Thay đổi sau lúc đó sẽ không còn. Dữ liệu hiện tại được chép ra 1 bản \"Trước khi lấy lại bản sao\" trước, nên vẫn lấy lại được. " +
                "Xong MeRon sẽ tự mở lại.",
                "Khôi phục"))
        {
            return;
        }
        string? before;
        try
        {
            before = Services.AppServices.Database.RestoreCopy(copy.Path, DateTime.Now);
        }
        catch (Exception ex)
        {
            AppLog.For("App").LogError(ex, "Khôi phục bản sao {Path} lỗi", copy.Path);
            await Dialogs.ShowMessageAsync(XamlRoot, "Không khôi phục được", ex.Message);
            ShowBackups();
            return;
        }
        AppLog.For("App").LogInformation("Khôi phục bản sao {Path}; dữ liệu trước đó: {Before}", copy.Path, before);
        await Dialogs.ShowMessageAsync(XamlRoot, "Đã khôi phục",
            $"Đã lấy lại dữ liệu lúc {copy.Time:dd/MM/yyyy HH:mm}. Bấm Đóng để MeRon mở lại." +
            (before is null ? "" : $"\n\nDữ liệu ngay trước đó: {before}"));
        App.Restart();
    }

    // ---------- Lịch sử chỉnh sửa ----------

    private bool _loadingHistory;

    /// <summary>Đổi số ngày là lưu ngay (xoá thật lúc mở app / Dọn ngay); ô trống / số lạ → về mặc định 90.</summary>
    private void HistoryDays_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingHistory || Window is not { } window)
        {
            return;
        }
        var days = double.IsNaN(args.NewValue) ? MenuSettings.DefaultHistoryKeepDays : (int)Math.Round(args.NewValue);
        window.SetHistoryKeepDays(days);
        if (sender.Value != window.MenuSettings.HistoryKeepDays)
        {
            sender.Value = window.MenuSettings.HistoryKeepDays;
        }
        ShowHistoryStatus();
    }

    private void CleanHistory_Click(object sender, RoutedEventArgs e)
    {
        if (Window is not { } window)
        {
            return;
        }
        if (window.MenuSettings.HistoryKeepDays == 0)
        {
            ShowHistoryStatus("Đang chọn giữ hết (0) - không xoá gì.");
            return;
        }
        var deleted = window.CleanUpHistory();
        ShowHistoryStatus(deleted < 0 ? "Dọn lỗi (xem log)."
            : deleted == 0 ? $"Không có lịch sử nào cũ hơn {window.MenuSettings.HistoryKeepDays} ngày."
            : $"Đã xoá {deleted} dòng lịch sử cũ.");
    }

    private void ShowHistoryStatus(string? result = null)
    {
        long count;
        try
        {
            count = Services.AppServices.History.Count();
        }
        catch (Exception)
        {
            count = 0;
        }
        HistoryStatus.Text = (result is null ? "" : result + "  ") + $"Hiện có {VnFormat.Number(count)} dòng lịch sử.";
    }

    // ---------- Nhập đơn hàng loạt ----------

    private bool _loadingUnpin;

    private void AskUnpin_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loadingUnpin)
        {
            Window?.SetAskBeforeUnpin(AskUnpinBox.IsChecked == true);
        }
    }

    // ---------- Giao diện ----------

    private bool _loadingTheme;

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingTheme && ThemeChoice.SelectedIndex >= 0)
        {
            Window?.SetTheme((AppTheme)ThemeChoice.SelectedIndex);
            ShowRestart();
        }
    }

    private void ShowRestart() =>
        RestartPanel.Visibility = Window?.MenuSettings.Theme != App.Theme ? Visibility.Visible : Visibility.Collapsed;

    private void Restart_Click(object sender, RoutedEventArgs e) => App.Restart();

    // ---------- Ảnh chụp ----------

    private bool _loadingCapture;

    /// <summary>Đổi số ngày là lưu ngay; ô trống / số lạ → về mặc định 30.</summary>
    private void KeepDays_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingCapture || Window is not { } window)
        {
            return;
        }
        var days = double.IsNaN(args.NewValue) ? MenuSettings.DefaultCaptureKeepDays : (int)Math.Round(args.NewValue);
        window.SetCaptureKeepDays(days);
        if (sender.Value != window.MenuSettings.CaptureKeepDays)
        {
            sender.Value = window.MenuSettings.CaptureKeepDays;
        }
        ShowCaptureStatus();
    }

    private void CleanNow_Click(object sender, RoutedEventArgs e)
    {
        if (Window is not { } window)
        {
            return;
        }
        if (window.MenuSettings.CaptureKeepDays == 0)
        {
            CaptureStatus.Text = "Đang chọn giữ hết (0) - không xoá ảnh nào.";
            return;
        }
        var (deleted, failed) = window.CleanUpCaptures();
        ShowCaptureStatus(deleted == 0 && failed == 0
            ? "Không có ảnh nào cũ hơn " + window.MenuSettings.CaptureKeepDays + " ngày."
            : $"Đã đưa {deleted} ảnh vào Thùng rác" + (failed > 0 ? $", {failed} ảnh không xoá được (đang mở?)." : "."));
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(CaptureFiles.Folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{CaptureFiles.Folder}\"") { UseShellExecute = true });
    }

    /// <summary>Số ảnh đang có trong thư mục (+ kết quả lần dọn vừa rồi).</summary>
    private void ShowCaptureStatus(string? result = null)
    {
        var count = Directory.Exists(CaptureFiles.Folder)
            ? Directory.EnumerateFiles(CaptureFiles.Folder, "*.png").Count(f => CaptureFiles.Prefixes.Any(p => Path.GetFileName(f).StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            : 0;
        CaptureStatus.Text = (result is null ? "" : result + "  ") + $"Hiện có {count} ảnh MeRon trong thư mục.";
    }

    /// <summary>Mỗi nhóm menu (Bán hàng, Thống kê...) 1 tiêu đề + các ô tích, xếp ngang tự xuống dòng.</summary>
    private void Build()
    {
        MenuGroups.Children.Clear();
        if (Window is not { } window)
        {
            return;
        }
        foreach (var group in window.MenuEntries().GroupBy(m => m.Group))
        {
            var boxes = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 220 };
            foreach (var entry in group)
            {
                var locked = MenuSettings.AlwaysVisible.Contains(entry.Page);
                var box = new CheckBox
                {
                    Content = locked ? $"{entry.Name} (luôn hiện)" : entry.Name,
                    Tag = entry.Page,
                    IsChecked = window.MenuSettings.IsVisible(entry.Page),
                    IsEnabled = !locked,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, entry.Name);
                box.Checked += Box_Changed;
                box.Unchecked += Box_Changed;
                boxes.Children.Add(box);
            }
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(new TextBlock { Text = group.Key, Opacity = 0.7, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
            panel.Children.Add(boxes);
            MenuGroups.Children.Add(panel);
        }
        ShowStatus();
    }

    private void Box_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string page } box && Window is { } window)
        {
            window.SetMenuVisible(page, box.IsChecked == true);
            ShowStatus();
        }
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        Window?.ShowAllMenu();
        Build();
    }

    private void ShowStatus()
    {
        var hidden = Window?.MenuSettings.Hidden.Count ?? 0;
        MenuStatus.Text = hidden == 0 ? "Đang hiện tất cả các mục." : $"Đang ẩn {hidden} mục.";
    }
}
