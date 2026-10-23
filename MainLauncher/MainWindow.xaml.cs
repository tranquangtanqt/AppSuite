using System;
using System.Linq;
using MainLauncher.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MainLauncher;

/// <summary>Application shell: a NavigationView hosting Dashboard / Module List / Logs / Settings.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        Closed += (_, _) => _helpWindow?.Close(); // đóng launcher thì đóng luôn cửa sổ Hướng dẫn
        // Trang dựng lỗi (vd lỗi XAML) → ghi lại lỗi thay vì chỉ thấy app tắt.
        ContentFrame.NavigationFailed += (_, e) =>
            App.Services.GetService<ILogger<MainWindow>>()?.LogError(e.Exception, "Không mở được trang {Page}", e.SourcePageType?.Name);
        AppWindow.Closing += AppWindow_Closing;
        SetUpTray();
    }

    // ----- Khay hệ thống ("Khi bấm X: thu xuống khay hệ thống") -----

    private const int TrayOpen = 1, TrayExit = 2, TrayInfo = 3;

    private Services.TrayIcon? _tray;
    private bool _trayBalloonShown;

    private static Services.LauncherSettingsStore Settings => App.Services.GetRequiredService<Services.LauncherSettingsStore>();

    private void SetUpTray()
    {
        _tray = new Services.TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), DispatcherQueue, "AppSuite - Main Launcher");
        _tray.OpenRequested += (_, _) => BringToFront();
        _tray.MenuRequested += (_, _) => ShowTrayMenu();
        _tray.IsVisible = Settings.Current.CloseToTray;
        // Đổi ở trang Cài đặt → bật / tắt icon ngay; tắt mà cửa sổ đang ẩn (không thể - đang ở trang Cài đặt) thì vẫn hiện lại cho chắc.
        Settings.Saved += () => DispatcherQueue.TryEnqueue(() =>
        {
            if (_tray is null)
                return;
            _tray.IsVisible = Settings.Current.CloseToTray;
            if (!_tray.IsVisible && !AppWindow.IsVisible)
                BringToFront();
        });
        Closed += (_, _) => { _tray?.Dispose(); _tray = null; };
    }

    /// <summary>Ẩn cửa sổ, chỉ còn icon ở khay. <paramref name="balloon"/>: lần đầu mỗi phiên báo "vẫn đang chạy" cạnh icon.</summary>
    public void HideToTray(bool balloon = true)
    {
        if (_tray is null)
            return;
        _tray.IsVisible = true;
        AppWindow.Hide();
        if (balloon && !_trayBalloonShown)
        {
            _trayBalloonShown = true;
            _tray.ShowBalloon("AppSuite vẫn đang chạy", "Bấm biểu tượng này để mở lại. Chuột phải → Thoát AppSuite để tắt hẳn.");
        }
    }

    private void ShowTrayMenu()
    {
        var running = RunningModules();
        var info = running.Count == 0 ? "Không có module nào đang chạy" : $"{running.Count} module đang chạy";
        var selected = _tray!.ShowMenu(
        [
            (TrayOpen, "Mở AppSuite", true, true),
            (TrayInfo, info, false, false),
            (0, null, false, false),
            (TrayExit, "Thoát AppSuite", true, false),
        ]);
        if (selected == TrayOpen)
            BringToFront();
        else if (selected == TrayExit)
            _ = ExitAsync();
    }

    private static System.Collections.Generic.List<string> RunningModules()
    {
        var moduleService = App.Services.GetRequiredService<Common.Interfaces.IModuleService>();
        var processManager = App.Services.GetRequiredService<Common.Interfaces.IProcessManager>();
        return moduleService.Modules.Where(m => processManager.CheckRunning(m.Name)).Select(m => m.Name).ToList();
    }

    // ----- Mở MainLauncher lần nữa khi đang chạy -----

    private DispatcherTimer? _noticeTimer;

    /// <summary>App gọi khi có người mở MainLauncher lần nữa (bản mới thoát ngay): hiện lại cửa sổ này (bỏ thu nhỏ), đưa lên
    /// trước và báo nhỏ "AppSuite đang chạy" (tự ẩn sau 6 giây).</summary>
    public void BringToFrontWithNotice()
    {
        BringToFront();
        ShowNotice(InfoBarSeverity.Informational, "AppSuite đang chạy", "Đây là cửa sổ MainLauncher đã mở trước đó.", TimeSpan.FromSeconds(6));
    }

    /// <summary>Hiện lại cửa sổ (đang ẩn ở khay / thu nhỏ) và đưa lên trước.</summary>
    private void BringToFront()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        AppWindow.Show();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private void ShowNotice(InfoBarSeverity severity, string title, string message, TimeSpan? autoHide)
    {
        NoticeBar.Severity = severity;
        NoticeBar.Title = title;
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
        NoticeHost.Visibility = Visibility.Visible;
        _noticeTimer?.Stop();
        if (autoHide is { } delay)
        {
            _noticeTimer = new DispatcherTimer { Interval = delay };
            _noticeTimer.Tick += (_, _) => { _noticeTimer?.Stop(); NoticeBar.IsOpen = false; };
            _noticeTimer.Start();
        }
    }

    /// <summary>Bấm X hoặc tự ẩn: ẩn luôn khung nền.</summary>
    private void NoticeBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) => NoticeHost.Visibility = Visibility.Collapsed;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // ----- Đóng MainLauncher mà còn module đang chạy -----

    private bool _closeConfirmed;

    /// <summary>Bấm X: "thu xuống khay" bật → ẩn cửa sổ (module chạy tiếp). Tắt → thoát như <see cref="ExitAsync"/>.</summary>
    private async void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed)
            return;

        args.Cancel = true; // phải đặt trước await đầu tiên
        if (Settings.Current.CloseToTray)
        {
            HideToTray();
            return;
        }
        await ExitAsync();
    }

    /// <summary>Thoát hẳn (bấm X khi không dùng khay, hoặc menu khay "Thoát AppSuite"). Còn module đang chạy: theo Settings -
    /// Hỏi (Đóng hết / Để chạy tiếp / Huỷ, ô "Nhớ lựa chọn"), Luôn đóng hết, hoặc Luôn để chạy tiếp. Đóng hết = Stop từng
    /// module song song (như nút Stop) rồi mới đóng cửa sổ.</summary>
    private async System.Threading.Tasks.Task ExitAsync()
    {
        var processManager = App.Services.GetRequiredService<Common.Interfaces.IProcessManager>();
        var settingsStore = Settings;
        var running = RunningModules();
        var mode = settingsStore.Current.CloseModulesOnExit;

        var closeAll = mode == Services.CloseModulesOnExit.CloseAll;
        if (running.Count > 0 && mode == Services.CloseModulesOnExit.Ask)
        {
            BringToFront(); // thoát từ khay: cửa sổ đang ẩn, hộp thoại cần cửa sổ hiện

            var remember = new CheckBox { Content = "Nhớ lựa chọn (đổi lại ở Settings)" };
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock
            {
                Text = $"Còn {running.Count} module đang chạy: {string.Join(", ", running)}.",
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(new TextBlock
            {
                Text = "Đóng hết: đóng từng module như bấm Dừng - module chưa tắt sau 5 giây (vd đang hỏi lưu) sẽ bị buộc tắt, nên lưu việc đang làm trước.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                FontSize = 12,
            });
            content.Children.Add(remember);
            var dialog = new ContentDialog
            {
                Title = "Đóng cả các module đang chạy?",
                Content = SharedUI.Helpers.DialogContent.Scrollable(content),
                PrimaryButtonText = "Đóng hết",
                SecondaryButtonText = "Để chạy tiếp",
                CloseButtonText = "Huỷ",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = Content.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None)
                return; // Huỷ: không đóng MainLauncher
            closeAll = result == ContentDialogResult.Primary;
            if (remember.IsChecked == true)
            {
                settingsStore.Current.CloseModulesOnExit = closeAll ? Services.CloseModulesOnExit.CloseAll : Services.CloseModulesOnExit.KeepRunning;
                settingsStore.Save();
            }
        }

        if (closeAll && running.Count > 0)
        {
            ShowNotice(InfoBarSeverity.Informational, "Đang đóng các module...", string.Join(", ", running), null);
            await System.Threading.Tasks.Task.WhenAll(running.Select(name =>
                System.Threading.Tasks.Task.Run(() => processManager.StopProcess(name))));
        }

        _closeConfirmed = true;
        Close();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    // ----- Hướng dẫn (F1) -----

    private SharedUI.Help.HelpWindow? _helpWindow;

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if ((args.InvokedItemContainer as NavigationViewItem)?.Tag as string == "Help")
            ShowHelp();
    }

    private void HelpAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowHelp();
    }

    /// <summary>Mở cửa sổ Hướng dẫn (1 cửa sổ duy nhất - đang mở thì đưa lên trước). Nội dung: Views/HelpContent.</summary>
    private void ShowHelp()
    {
        if (_helpWindow is null)
        {
            _helpWindow = new SharedUI.Help.HelpWindow("AppSuite - Main Launcher", "Mở / đóng các công cụ của AppSuite, ẩn / hiện module, xem nhật ký.",
                HelpContent.Sections, searchPlaceholder: "Tìm tính năng, vd: dừng module, ẩn module, modules.json...");
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        _helpWindow.Activate();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        var pageType = tag switch
        {
            "Dashboard" => typeof(DashboardPage),
            "Modules" => typeof(ModuleListPage),
            "Logs" => typeof(LogsPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            try
            {
                ContentFrame.Navigate(pageType);
            }
            catch (Exception ex)
            {
                App.Services.GetService<ILogger<MainWindow>>()?.LogError(ex, "Không mở được trang {Page}", pageType.Name);
            }
        }
    }
}
