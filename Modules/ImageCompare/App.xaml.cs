using ImageCompare.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace ImageCompare;

/// <summary>
/// Standalone entry point for ImageCompare (So sánh ảnh). Runs identically whether launched directly by a
/// developer or started by MainLauncher's ProcessManager - it never references MainLauncher.
/// Dòng lệnh: <c>ImageCompare.exe [ảnh A] [ảnh B]</c> mở sẵn 2 ảnh (xem MainWindow.LoadFromCommandLine).
/// </summary>
public partial class App : Application
{
    private static readonly ILogger Log = AppLog.For(nameof(App));
    private Window? _window;

    public App()
    {
        InitializeComponent();
        // Không bắt thì 1 exception chưa xử lý làm app tắt hẳn không rõ lý do - ghi vào log để còn xem lại.
        UnhandledException += (_, e) => Log.LogCritical(e.Exception, "Lỗi không xử lý được - app sẽ tắt");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.CleanupOldFiles();
        Log.LogInformation("Khởi động ImageCompare {Version}", typeof(App).Assembly.GetName().Version);
        _window = new MainWindow();
        _window.Activate();
    }
}
