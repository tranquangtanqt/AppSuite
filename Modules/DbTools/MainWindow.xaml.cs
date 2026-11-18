using DbTools.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DbTools;

public sealed partial class MainWindow : Window
{
    private SharedUI.Help.HelpWindow? _help;

    public MainWindow()
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        ResizeForDpi(1280, 900);
        AppLog.For("App").LogInformation("Khởi động DbTools");
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();
        Closed += (_, _) => _help?.Close(); // đóng app thì đóng luôn cửa sổ Hướng dẫn
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Kích thước mở cửa sổ tính theo DIP (Resize nhận pixel thật: 1280×900 px ở màn hình 150% chỉ còn ~850×600 DIP),
    /// không vượt quá vùng làm việc của màn hình.</summary>
    private void ResizeForDpi(int widthDip, int heightDip)
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        var width = (int)Math.Min(widthDip * scale, work.Width * 0.95);
        var height = (int)Math.Min(heightDip * scale, work.Height * 0.95);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
    }

    /// <summary>Chuyển sang trang Kết nối (từ nút trên trang Chạy).</summary>
    public void ShowPage(string tag)
    {
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }
            && Type.GetType($"DbTools.Views.{tag}") is { } pageType
            && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "Help" })
        {
            // Mở sau khi cú bấm chuột xong: mở ngay thì cửa sổ chính được kéo lại lên trên, Hướng dẫn nằm phía sau.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => OpenHelp());
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e) => OpenHelp();

    /// <summary>Mở (hoặc đưa lên trước) cửa sổ Hướng dẫn dùng chung ở đúng mục của trang đang xem.</summary>
    private void OpenHelp()
    {
        if (_help is null)
        {
            _help = new SharedUI.Help.HelpWindow("DbTools", "Chạy thư mục script SQL lên PostgreSQL: thứ tự, kiểm tra, chạy thử, chạy trong 1 transaction.",
                HelpSections, accent: Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x76, 0x6E),
                searchPlaceholder: "Tìm, vd: rollback, thu tu, xoa roi tao...", searchSummary: true);
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
