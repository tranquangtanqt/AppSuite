using MeRon.Core.Data;
using MeRon.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace MeRon;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        App.ApplyTitleBarTheme(AppWindow);
        AppWindow.Resize(new SizeInt32(1280, 860));
        AppLog.For("App").LogInformation("Khởi động MeRon");
        Closed += (_, _) => _help?.Close(); // đóng app thì đóng luôn cửa sổ Hướng dẫn
        MenuSettings = Core.MenuSettings.Load(MenuSettingsPath);
        AppServices.Database.CopyBeforeActions = MenuSettings.BackupBeforeActions;
        ApplyMenuSettings();
        _ = Task.Run(CleanUpCaptures); // ảnh Chụp ảnh cũ → Thùng rác, không chặn lúc mở app
        _ = Task.Run(CleanUpHistory);
        var start = StartPage();
        Nav.SelectedItem = FindItem(MenuSettings.IsVisible(start) ? start : "OrdersPage");
        StartNotices();
    }

    // ---------- Ẩn / hiện mục menu (trang Cài đặt) ----------

    public Core.MenuSettings MenuSettings { get; }

    private static string MenuSettingsPath => System.IO.Path.Combine(AppServices.Database.Folder, Core.MenuSettings.FileName);

    /// <summary>1 mục menu ẩn được: nhóm, tên, tag trang.</summary>
    public sealed record MenuEntry(string Group, string Name, string Page);

    public IReadOnlyList<MenuEntry> MenuEntries()
    {
        var entries = new List<MenuEntry>();
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.MenuItems.Count > 0)
            {
                var group = item.Content as string ?? string.Empty;
                entries.AddRange(item.MenuItems.OfType<NavigationViewItem>()
                    .Select(child => new MenuEntry(group, child.Content as string ?? string.Empty, child.Tag as string ?? string.Empty)));
            }
            else if (item is { Tag: string tag, Content: string name })
            {
                entries.Add(new MenuEntry("Bán hàng", name, tag));
            }
        }
        return entries;
    }

    public void SetTheme(Core.AppTheme theme)
    {
        MenuSettings.Theme = theme;
        SaveMenuSettings();
    }

    public void SetMenuVisible(string page, bool visible)
    {
        MenuSettings.SetVisible(page, visible);
        SaveMenuSettings();
    }

    public void ShowAllMenu()
    {
        MenuSettings.ShowAll();
        SaveMenuSettings();
    }

    /// <summary>Nhập đơn hàng loạt: hỏi trước khi bỏ ghim dòng bị sửa tay (trang Cài đặt).</summary>
    public void SetAskBeforeUnpin(bool ask)
    {
        MenuSettings.AskBeforeUnpin = ask;
        SaveMenuSettings();
    }

    /// <summary>Công nợ: mốc tuổi nợ người dùng nhập (đã kiểm tra bằng DebtAging.Parse).</summary>
    public void SetDebtAgingMarks(IReadOnlyList<int> marks)
    {
        MenuSettings.DebtAgingMarks = marks;
        SaveMenuSettings();
    }

    // ---------- Ảnh chụp (Pictures\MeRon): tự dọn ảnh cũ - trang Cài đặt ----------

    public void SetCaptureKeepDays(int days)
    {
        MenuSettings.CaptureKeepDays = days;
        SaveMenuSettings();
    }

    /// <summary>Đưa ảnh MeRon cũ hơn số ngày ở Cài đặt vào Thùng rác (lúc mở app, nút "Dọn ngay"). Trả (đã xoá, lỗi).</summary>
    public (int Deleted, int Failed) CleanUpCaptures()
    {
        try
        {
            var result = Core.CaptureFiles.CleanUp(Core.CaptureFiles.Folder, MenuSettings.CaptureKeepDays, DateTime.Now, Core.CaptureFiles.SendToRecycleBin);
            if (result.Deleted + result.Failed > 0)
            {
                AppLog.For("App").LogInformation("Dọn ảnh chụp cũ hơn {Days} ngày: {Deleted} ảnh vào Thùng rác, {Failed} lỗi",
                    MenuSettings.CaptureKeepDays, result.Deleted, result.Failed);
            }
            return result;
        }
        catch (Exception ex)
        {
            AppLog.For("App").LogError(ex, "Dọn ảnh chụp cũ lỗi");
            return (0, 1);
        }
    }

    // ---------- Lịch sử chỉnh sửa: tự xoá dòng cũ - trang Cài đặt ----------

    /// <summary>Cài đặt: tự sao lưu mỗi ngày / khi thao tác (Nhập, Xoá toàn bộ, Dọn).</summary>
    public void SetBackupOptions(bool daily, bool beforeActions)
    {
        MenuSettings.AutoDailyBackup = daily;
        MenuSettings.BackupBeforeActions = beforeActions;
        AppServices.Database.CopyBeforeActions = beforeActions;
        SaveMenuSettings();
    }

    /// <summary>Số bản sao lưu hằng ngày giữ lại (Cài đặt) - giảm thì xoá ngay các bản thừa cũ nhất.</summary>
    public void SetDailyBackupKeep(int keep)
    {
        MenuSettings.DailyBackupKeep = keep;
        SaveMenuSettings();
        try
        {
            AppServices.Database.PruneCopies(Core.Data.MeRonDatabase.DailyCopyReason, MenuSettings.DailyBackupKeep);
        }
        catch (Exception ex)
        {
            AppLog.For("App").LogError(ex, "Dọn bản sao lưu hằng ngày lỗi");
        }
    }

    public void SetHistoryKeepDays(int days)
    {
        MenuSettings.HistoryKeepDays = days;
        SaveMenuSettings();
    }

    /// <summary>Xoá lịch sử chỉnh sửa cũ hơn số ngày ở Cài đặt (lúc mở app, nút "Dọn ngay"). Trả số dòng đã xoá (-1 = lỗi).</summary>
    public int CleanUpHistory()
    {
        try
        {
            var deleted = AppServices.History.Cleanup(MenuSettings.HistoryKeepDays, DateTime.Now);
            if (deleted > 0)
            {
                AppLog.For("App").LogInformation("Xoá lịch sử chỉnh sửa cũ hơn {Days} ngày: {Count} dòng", MenuSettings.HistoryKeepDays, deleted);
            }
            return deleted;
        }
        catch (Exception ex)
        {
            AppLog.For("App").LogError(ex, "Dọn lịch sử chỉnh sửa lỗi");
            return -1;
        }
    }

    private void SaveMenuSettings()
    {
        try
        {
            MenuSettings.Save(MenuSettingsPath);
        }
        catch (Exception ex)
        {
            AppLog.For("App").LogError(ex, "Không lưu được cài đặt menu");
        }
        ApplyMenuSettings();
    }

    /// <summary>Ẩn mục bị bỏ tích; nhóm ẩn khi không còn mục con nào hiện.</summary>
    private void ApplyMenuSettings()
    {
        foreach (var item in AllItems())
        {
            if (item.Tag is string tag)
            {
                item.Visibility = MenuSettings.IsVisible(tag) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        foreach (var group in Nav.MenuItems.OfType<NavigationViewItem>().Where(i => i.MenuItems.Count > 0))
        {
            group.Visibility = group.MenuItems.OfType<NavigationViewItem>().Any(i => i.Visibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Chưa có dữ liệu (lần đầu mở) → vào thẳng trang Nhập dữ liệu; có rồi → Đặt hàng.</summary>
    private static string StartPage()
    {
        try
        {
            return AppServices.Database.Count(Schema.Users.Name) + AppServices.Database.Count(Schema.Orders.Name) == 0
                ? "ImportPage"
                : "OrdersPage";
        }
        catch (Exception ex)
        {
            AppLog.For("App").LogError(ex, "Không mở được database");
            return "ImportPage";
        }
    }

    /// <summary>Các mục mở trang: mục lẻ và mục con của nhóm (nhóm không có Tag, bấm chỉ mở / đóng).</summary>
    private IEnumerable<NavigationViewItem> AllItems() =>
        Nav.MenuItems.OfType<NavigationViewItem>().SelectMany(i => i.MenuItems.Count > 0 ? i.MenuItems.OfType<NavigationViewItem>() : [i]);

    /// <summary>Tìm cả mục ở chân menu (Cài đặt) - vd chuông Thông báo → Mở Cài đặt sao lưu.</summary>
    private NavigationViewItem? FindItem(string tag) =>
        AllItems().Concat(Nav.FooterMenuItems.OfType<NavigationViewItem>()).FirstOrDefault(i => i.Tag as string == tag);

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            NavigateFromMenu(tag);
        }
    }

    /// <summary>Chọn 1 mục menu từ trong trang (vd cảnh báo ở trang Nhập → mở trang Xuất).</summary>
    public void SelectMenu(string tag)
    {
        if (FindItem(tag) is { } item)
        {
            if (item.Visibility == Visibility.Visible)
            {
                Nav.SelectedItem = item;
            }
            else
            {
                // mục đang bị ẩn (Cài đặt): vẫn mở trang, menu không chọn mục nào
                Nav.SelectedItem = null;
                NavigateFromMenu(tag);
            }
        }
    }

    /// <summary>Mục menu = trang gốc: xoá lịch sử Back (Back chỉ dùng trong 1 luồng, vd Đặt hàng → form đơn → quay lại).</summary>
    private void NavigateFromMenu(string tag)
    {
        if (Type.GetType($"MeRon.Views.{tag}") is { } pageType && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
            ContentFrame.BackStack.Clear();
        }
    }

    private SharedUI.Help.HelpWindow? _help;

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "Notices" } notices)
        {
            Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(notices);
        }
        else if (args.InvokedItemContainer is NavigationViewItem { Tag: "Help" })
        {
            // Mở sau khi cú bấm chuột xong: mở ngay trong ItemInvoked thì lúc thả chuột cửa sổ chính được kéo lại lên trên,
            // Hướng dẫn nằm phía sau (F1 không bị vì không có cú bấm).
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => OpenHelp());
        }
        // Bấm lại mục đang chọn khi đang ở trang con (form đơn, Chi tiết) → về trang gốc của mục đó.
        else if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag } item && ReferenceEquals(item, sender.SelectedItem))
        {
            NavigateFromMenu(tag);
        }
    }

    // ---------- Thông báo (Core\Notices, 2026-11-16) ----------

    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    private IReadOnlyList<Core.Notice> _notices = [];

    /// <summary>Gọi trong ctor: tính lại số trên chuông mỗi lần đổi trang (vừa lưu / sửa đơn rồi quay lại), khi cửa sổ được
    /// kích hoạt và mỗi phút (qua ngày, bản sao lưu ở luồng nền vừa xong).</summary>
    private void StartNotices()
    {
        ContentFrame.Navigated += (_, _) => RefreshNotices();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                RefreshNotices();
            }
        };
        _noticeTimer.Tick += (_, _) => RefreshNotices();
        _noticeTimer.Start();
        RefreshNotices();
    }

    /// <summary>Đọc lại các việc cần để ý; lỗi chỉ ghi log (không làm phiền).</summary>
    public void RefreshNotices()
    {
        try
        {
            var database = AppServices.Database;
            _notices = Core.Notices.Build(AppServices.Orders.NeedsConfirm(), database.GetUnsyncedEdits(),
                database.ListCopies().FirstOrDefault()?.Time, database.Count("Orders") + database.Count("Users") > 0, DateTime.Now);
        }
        catch (Exception ex)
        {
            AppLog.For("App").LogWarning(ex, "Đọc thông báo lỗi");
            return;
        }
        var count = Core.Notices.WarningCount(_notices);
        NoticeBadge.Value = count;
        NoticeBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoticeItem.Content = count > 0 ? $"Thông báo ({count})" : "Thông báo";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NoticeItem, count > 0 ? $"Thông báo - {count} việc đang chờ" : "Thông báo");
    }

    private void NoticeFlyout_Opening(object? sender, object e)
    {
        RefreshNotices();
        ShowNoticePanel();
    }

    /// <summary>Khung Thông báo: mỗi việc 1 khối (biểu tượng, tiêu đề, chi tiết, nút làm ngay).</summary>
    private void ShowNoticePanel(string? status = null)
    {
        NoticePanel.Children.Clear();
        NoticePanel.Children.Add(new TextBlock { Text = "Thông báo", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        if (Core.Notices.WarningCount(_notices) == 0)
        {
            NoticePanel.Children.Add(new TextBlock { Text = "Không có việc gì đang chờ.", Opacity = 0.8 });
        }
        foreach (var notice in _notices)
        {
            NoticePanel.Children.Add(NoticeBlock(notice));
        }
        if (status is not null)
        {
            NoticePanel.Children.Add(new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        }
    }

    private Grid NoticeBlock(Core.Notice notice)
    {
        var warning = notice.Level == Core.NoticeLevel.Warning;
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6) };
        grid.Background = warning ? Views.Ui.LapsedBackground(true) : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = notice.Kind switch { Core.NoticeKind.Confirm => "", Core.NoticeKind.Unsynced => "", _ => "" };
        var icon = new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        if (warning)
        {
            icon.Foreground = Views.Ui.LapsedForeground(true);
        }
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = notice.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (notice.Detail.Length > 0)
        {
            body.Children.Add(new TextBlock { Text = notice.Detail, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        }
        var (label, action) = notice.Kind switch
        {
            Core.NoticeKind.Confirm => ("Xem các đơn", (Action)(() =>
            {
                Views.OrdersPage.ShowConfirm();
                SelectMenu("OrdersPage");
            })),
            Core.NoticeKind.Unsynced => ("Xuất lên Google Drive", () => _ = UploadDriveAsync()),
            _ => ("Mở Cài đặt sao lưu", (Action)(() => SelectMenu("SettingsPage"))),
        };
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
        if (notice.Kind == Core.NoticeKind.Unsynced)
        {
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }
        button.Click += (_, _) =>
        {
            if (notice.Kind != Core.NoticeKind.Unsynced)
            {
                NoticeFlyout.Hide();
            }
            action();
        };
        body.Children.Add(button);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grid, $"{notice.Title}. {notice.Detail}");
        Grid.SetColumn(body, 1);
        grid.Children.Add(icon);
        grid.Children.Add(body);
        return grid;
    }

    /// <summary>Đã đăng nhập Google (trang Xuất) → xuất + tải lên ngay trong khung (bộ đếm chưa xuất về 0); chưa → mở trang Xuất.</summary>
    private async Task UploadDriveAsync()
    {
        var export = new ViewModels.ExportViewModel();
        export.RefreshGoogle();
        if (!export.IsSignedIn)
        {
            NoticeFlyout.Hide();
            SelectMenu("ExportPage");
            return;
        }
        ShowNoticePanel("Đang xuất và tải lên Google Drive...");
        await export.UploadToDriveAsync();
        RefreshNotices();
        var left = AppServices.Database.GetUnsyncedEdits();
        ShowNoticePanel((left.Any ? "Chưa xuất được: " : "Đã xuất: ") + export.DriveStatus
                        + (left.Any ? string.Empty : " Nhập bản này vào điện thoại để điện thoại có các đơn mới."));
    }

    private void F1_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenHelp();
    }

    /// <summary>Mở (hoặc đưa lên trước) cửa sổ Hướng dẫn dùng chung ở đúng mục của trang đang xem.</summary>
    private void OpenHelp()
    {
        if (_help is null)
        {
            _help = new SharedUI.Help.HelpWindow("MeRon", "Quản lý bán hàng - dữ liệu từ app điện thoại me-ron.",
                HelpSections, accent: Windows.UI.Color.FromArgb(0xFF, 0xC2, 0x41, 0x0C),
                searchPlaceholder: "Tìm, vd: nhap, trung ten, xoa...", searchSummary: true);
            App.ApplyTitleBarTheme(_help.AppWindow);
            _help.Closed += (_, _) => _help = null;
        }
        _help.ShowSection(Core.HelpContent.ForPage(ContentFrame.Content?.GetType().Name).Title);
        _help.Activate();
    }

    /// <summary>Nội dung Hướng dẫn (Core\HelpContent - không phụ thuộc WinUI, có unit test) theo kiểu của cửa sổ dùng chung.</summary>
    private static IReadOnlyList<SharedUI.Help.HelpSection> HelpSections { get; } = Core.HelpContent.Sections
        .Select(s => new SharedUI.Help.HelpSection(s.Title, s.Glyph, s.Summary,
            s.Items.Select(i => new SharedUI.Help.HelpItem(i.Name, i.Description, i.Keys)).ToList()))
        .ToList();
}
