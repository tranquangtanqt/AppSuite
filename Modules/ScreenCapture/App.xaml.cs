using Microsoft.UI.Xaml;
using ScreenCapture.Views;

namespace ScreenCapture;

/// <summary>
/// Standalone entry point for the ScreenCapture module. Runs identically whether launched directly by
/// a developer or started by MainLauncher's ProcessManager - it never references MainLauncher.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // Không có logging trong module này (PLAN.md), nên nếu không bắt ở đây, 1 exception chưa
        // xử lý sẽ làm app tắt hoàn toàn không rõ lý do (đúng triệu chứng đã gặp lúc mở Editor sau
        // khi chọn vùng). Ghi ra file cạnh exe để còn xem lại được sau khi app đã đóng.
        this.UnhandledException += (_, e) =>
        {
            try
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log");
                System.IO.File.AppendAllText(path,
                    $"{DateTime.Now:O}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Không để việc ghi log lại làm crash tiếp.
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new CaptureLauncherWindow();
        _window.Activate();
    }
}
