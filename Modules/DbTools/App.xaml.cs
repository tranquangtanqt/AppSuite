using DbTools.Services;
using Microsoft.UI.Xaml;

namespace DbTools;

/// <summary>
/// Điểm vào độc lập của DbTools - chạy giống hệt dù mở trực tiếp hay được MainLauncher khởi động, không bao giờ
/// reference MainLauncher.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // DbTools tự tách câu SQL → Npgsql gửi nguyên văn từng câu (xem Services.Pg). Đặt trước mọi lần dùng Npgsql.
        AppContext.SetSwitch("Npgsql.EnableSqlRewriting", false);
        AppLog.CleanupOldFiles();
        InitializeComponent();
    }

    /// <summary>Cửa sổ chính - hộp thoại chọn thư mục cần hwnd của nó (app unpackaged).</summary>
    public static MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        _window = MainWindow;
        _window.Activate();
    }
}
