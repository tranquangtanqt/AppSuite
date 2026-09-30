using FileTools.Core;
using FileTools.Services;
using Microsoft.UI.Xaml;

namespace FileTools;

/// <summary>
/// Điểm vào độc lập của FileTools - chạy giống hệt dù mở trực tiếp hay được MainLauncher khởi động, không bao giờ
/// reference MainLauncher.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // Shift-JIS (code page 932) không có sẵn trong .NET - phải đăng ký trước khi nhận / ghi encoding.
        TextEncodings.Register();
        AppLog.CleanupOldFiles();
        InitializeComponent();
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
