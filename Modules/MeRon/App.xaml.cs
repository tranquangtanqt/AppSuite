using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace MeRon;

/// <summary>
/// Điểm vào độc lập của MeRon - chạy giống hệt dù mở trực tiếp hay được MainLauncher khởi động, không bao giờ
/// reference MainLauncher.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        AppLog.CleanupOldFiles();
        // Đặt ở đây, không đổi lúc app đang chạy: nhiều chỗ lấy màu qua Application.Current.Resources, hộp thoại và cửa sổ Hướng dẫn
        // cũng theo màu của Application; đổi riêng trên cửa sổ thì những chỗ đó lệch màu (chữ tối trên nền tối).
        Theme = MenuSettings.Load(SettingsPath).Theme;
        if (Theme != AppTheme.System)
        {
            RequestedTheme = Theme == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }

        InitializeComponent();
    }

    /// <summary>Giao diện đang dùng trong lần chạy này (đổi trong Cài đặt có tác dụng từ lần mở sau).</summary>
    public static AppTheme Theme { get; private set; }

    private static string SettingsPath =>
        Path.Combine(Path.GetDirectoryName(MeRonDatabase.DefaultPath)!, MenuSettings.FileName);

    /// <summary>Thanh tiêu đề mặc định luôn sáng, không tự theo giao diện của app.</summary>
    public static void ApplyTitleBarTheme(AppWindow window) => window.TitleBar.PreferredTheme = Theme switch
    {
        AppTheme.Light => TitleBarTheme.Light,
        AppTheme.Dark => TitleBarTheme.Dark,
        _ => TitleBarTheme.UseDefaultAppMode,
    };

    /// <summary>Mở lại app để áp dụng giao diện vừa chọn.</summary>
    public static void Restart()
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, arguments));
        Current.Exit();
    }

    /// <summary>Cửa sổ chính - các hộp thoại chọn file cần hwnd của nó (app unpackaged).</summary>
    public static MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        _window = MainWindow;
        _window.Activate();
    }
}
