using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using ScreenCapture.Views;

namespace ScreenCapture;

/// <summary>
/// Standalone entry point for the ScreenCapture module. Runs identically whether launched directly by
/// a developer or started by MainLauncher's ProcessManager - it never references MainLauncher.
/// </summary>
public partial class App : Application
{
    private static readonly ILogger Log = Services.AppLog.For(nameof(App));

    private Window? _window;

    public App()
    {
        InitializeComponent();

        // Nếu không bắt ở đây, 1 exception chưa xử lý sẽ làm app tắt hoàn toàn không rõ lý do (đúng triệu
        // chứng đã gặp lúc mở Editor sau khi chọn vùng). Ghi vào log (Logs cạnh exe) để còn xem lại được
        // sau khi app đã đóng.
        this.UnhandledException += (_, e) => Log.LogCritical(e.Exception, "Lỗi không xử lý được - app sẽ tắt");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // "Khởi động cùng Windows" chạy exe với --tray → chỉ hiện icon ở khay, không bật cửa sổ chính.
        bool startInTray = Environment.GetCommandLineArgs().Contains(Services.StartupRegistration.TrayArgument);
        Services.AppLog.CleanupOldFiles();
        Log.LogInformation("Khởi động ScreenCapture {Version} (tray={Tray})", typeof(App).Assembly.GetName().Version, startInTray);
        var launcher = new CaptureLauncherWindow(startInTray);
        _window = launcher;
        if (!launcher.StartHidden)
        {
            launcher.Activate();
        }
    }
}
