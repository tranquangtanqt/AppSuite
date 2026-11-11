using System.Text;
using CsvEditor.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CsvEditor;

/// <summary>
/// Standalone entry point for Module F. Runs identically whether launched directly by a developer
/// or started by MainLauncher's ProcessManager - it never references MainLauncher.
/// </summary>
public partial class App : Application
{
    private static readonly ILogger Log = AppLog.For("App");
    private MainWindow? _window;
    private int _fatalHandled;

    public App()
    {
        // .NET has dropped legacy code pages (Shift-JIS/932 included) from its default Encoding
        // table - GetEncoding(932) throws NotSupportedException without this. Must run before
        // EncodingPickerDialog ever resolves Shift-JIS.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        AppLog.CleanupOldFiles();
        // Đặt trước InitializeComponent, không đổi lúc app đang chạy: hộp thoại, chữ đỏ báo lỗi (Application.Current.Resources) và
        // cửa sổ Hướng dẫn theo màu của Application - đổi riêng trên cửa sổ thì những chỗ đó lệch màu (như MeRon).
        Theme = new AppSettingsStore().LoadTheme();
        if (Theme != AppTheme.System)
        {
            RequestedTheme = Theme == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }

        InitializeComponent();

        // Lỗi chưa ai bắt làm app tắt hẳn: ghi log kèm stack để còn tìm nguyên nhân, và chép bảng đang sửa dở ra bản khôi phục
        // (lần mở sau hỏi mở lại). Lỗi trong tác vụ nền bị bỏ quên không làm tắt app - chỉ ghi log.
        UnhandledException += (_, e) => OnFatalError(e.Exception, "giao diện");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnFatalError(e.ExceptionObject as Exception, "luồng nền");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.LogError(e.Exception, "Lỗi trong tác vụ nền (app vẫn chạy)");
    }

    /// <summary>Giao diện đang dùng trong lần chạy này (đổi ở nút Giao diện có tác dụng từ lần mở sau).</summary>
    public static AppTheme Theme { get; private set; }

    /// <summary>Thanh tiêu đề mặc định luôn sáng, không tự theo giao diện của app.</summary>
    public static void ApplyTitleBarTheme(Microsoft.UI.Windowing.AppWindow window) => window.TitleBar.PreferredTheme = Theme switch
    {
        AppTheme.Light => Microsoft.UI.Windowing.TitleBarTheme.Light,
        AppTheme.Dark => Microsoft.UI.Windowing.TitleBarTheme.Dark,
        _ => Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode,
    };

    /// <summary>Mở lại app (áp dụng giao diện vừa chọn); <paramref name="filePath"/> khác null thì lần mở sau mở lại file đó.</summary>
    public static void Restart(string? filePath)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!);
        if (filePath is not null)
        {
            start.ArgumentList.Add(filePath);
        }
        System.Diagnostics.Process.Start(start);
        Current.Exit();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.LogInformation("Khởi động CsvEditor {Version}", typeof(App).Assembly.GetName().Version);
        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>Chỉ chạy 1 lần dù lỗi báo qua cả 2 đường (giao diện rồi AppDomain).</summary>
    private void OnFatalError(Exception? exception, string where)
    {
        if (Interlocked.Exchange(ref _fatalHandled, 1) == 1)
        {
            return;
        }

        Log.LogCritical(exception, "Lỗi không xử lý được ({Where}) - app sẽ tắt: {Message}", where, exception?.Message);
        if (_window?.SaveRecoveryIfDirty() is { } path)
        {
            Log.LogWarning("Đã lưu bản khôi phục các thay đổi chưa lưu: {Path}", path);
        }
    }
}
