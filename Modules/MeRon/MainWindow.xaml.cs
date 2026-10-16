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
        AppWindow.Resize(new SizeInt32(1280, 860));
        AppLog.For("App").LogInformation("Khởi động MeRon");
        Closed += (_, _) => _help?.Close(); // đóng app thì đóng luôn cửa sổ Hướng dẫn
        MenuSettings = Core.MenuSettings.Load(MenuSettingsPath);
        ApplyMenuSettings();
        var start = StartPage();
        Nav.SelectedItem = FindItem(MenuSettings.IsVisible(start) ? start : "OrdersPage");
    }

    // ---------- Ẩn / hiện mục menu (trang Cài đặt) ----------

    public Core.MenuSettings MenuSettings { get; }

    private static string MenuSettingsPath => System.IO.Path.Combine(AppServices.Database.Folder, Core.MenuSettings.FileName);

    /// <summary>1 mục menu ẩn được: nhóm (tiêu đề phía trên nó), tên, tag trang.</summary>
    public sealed record MenuEntry(string Group, string Name, string Page);

    public IReadOnlyList<MenuEntry> MenuEntries()
    {
        var entries = new List<MenuEntry>();
        var group = string.Empty;
        foreach (var item in Nav.MenuItems)
        {
            if (item is NavigationViewItemHeader header)
            {
                group = header.Content as string ?? string.Empty;
            }
            else if (item is NavigationViewItem { Tag: string tag, Content: string name })
            {
                entries.Add(new MenuEntry(group, name, tag));
            }
        }
        return entries;
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

    /// <summary>Ẩn mục bị bỏ tích; tiêu đề nhóm ẩn khi cả nhóm không còn mục nào hiện.</summary>
    private void ApplyMenuSettings()
    {
        NavigationViewItemHeader? header = null;
        var groupVisible = false;
        void CloseGroup()
        {
            if (header is not null)
            {
                header.Visibility = groupVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        foreach (var item in Nav.MenuItems)
        {
            if (item is NavigationViewItemHeader next)
            {
                CloseGroup();
                header = next;
                groupVisible = false;
            }
            else if (item is NavigationViewItem { Tag: string tag } menuItem)
            {
                var visible = MenuSettings.IsVisible(tag);
                menuItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                groupVisible |= visible;
            }
        }
        CloseGroup();
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

    private NavigationViewItem? FindItem(string tag) =>
        Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag);

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
            OpenHelp();
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
