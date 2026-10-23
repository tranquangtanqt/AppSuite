using MeRon.Core;
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
        _loadingCapture = true;
        KeepDaysBox.Value = Window?.MenuSettings.CaptureKeepDays ?? MenuSettings.DefaultCaptureKeepDays;
        _loadingCapture = false;
        ShowCaptureStatus();
    }

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
