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
        ApplyMenuSettings();
        _ = Task.Run(CleanUpCaptures); // ảnh Chụp ảnh cũ → Thùng rác, không chặn lúc mở app
        var start = StartPage();
        Nav.SelectedItem = FindItem(MenuSettings.IsVisible(start) ? start : "OrdersPage");
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

    private NavigationViewItem? FindItem(string tag) => AllItems().FirstOrDefault(i => i.Tag as string == tag);

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
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "Help" })
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
