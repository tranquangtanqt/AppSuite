using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Common.Interfaces;
using Common.Logging;
using MainLauncher.Services;
using MainLauncher.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MainLauncher;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>DI container for the whole launcher; resolved by pages/view models via <c>App.Services</c>.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Shared in-memory log sink feeding the Logs page, registered as an ILoggerProvider.</summary>
    public static InMemoryLoggerProvider InMemoryLog { get; } = new();

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
        // Lỗi chưa bắt (vd lỗi XAML khi mở 1 trang) làm app tắt ngay không để lại gì ngoài Event Log - ghi vào Logs\launcher-*.log.
        UnhandledException += (_, e) =>
            Services.GetService<ILogger<App>>()?.LogCritical(e.Exception, "Unhandled exception: {Message}", e.Message);
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Chỉ 1 MainLauncher: đã có bản đang chạy thì nhờ bản đó tự đưa cửa sổ lên + báo "AppSuite đang chạy", bản này thoát.
        if (!TryBecomePrimaryInstance())
        {
            Environment.Exit(0);
            return;
        }

        _window = new MainWindow();
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
        _window.Activate();

        // Tạo ngay (trên luồng UI) để trang Logs có cả các dòng từ lúc khởi động, không chỉ từ lần đầu mở trang.
        Services.GetRequiredService<LogsViewModel>();
        var logger = Services.GetRequiredService<ILogger<App>>();
        var moduleList = Services.GetRequiredService<ModuleListViewModel>();
        _ = LoadModulesOnStartupAsync(moduleList, logger);
    }

    // ----- Chỉ chạy 1 bản -----

    private const string InstanceMutexName = @"Local\AppSuite.MainLauncher";
    private const string ActivateEventName = @"Local\AppSuite.MainLauncher.Activate";

    /// <summary>Giữ suốt đời tiến trình (GC thu mutex là bản khác mở được).</summary>
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _activateSignal;

    /// <summary>True = bản đầu tiên (nghe tín hiệu "có người mở lần nữa"); false = đã có bản đang chạy - đã báo cho bản đó.</summary>
    private bool TryBecomePrimaryInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Bản đang chạy cần quyền đưa cửa sổ lên trước - bản này đang ở trước (người dùng vừa mở) nên cho phép được.
            AllowSetForegroundWindow(ASFW_ANY);
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var signal))
            {
                using (signal)
                    signal.Set();
            }
            return false;
        }

        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        new Thread(() =>
        {
            while (_activateSignal.WaitOne())
                dispatcher.TryEnqueue(() => (_window as MainWindow)?.BringToFrontWithNotice());
        })
        { IsBackground = true, Name = "MainLauncher single-instance" }.Start();
        return true;
    }

    private const int ASFW_ANY = -1;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private static async Task LoadModulesOnStartupAsync(ModuleListViewModel moduleList, ILogger logger)
    {
        try
        {
            await moduleList.InitializeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load module configuration on startup");
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var configDirectory = Path.Combine(baseDirectory, "Config");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(configDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        // LogsViewModel nhận provider này qua constructor - AddProvider bên dưới không đăng ký nó vào DI (thiếu dòng này thì
        // mở trang Logs ném InvalidOperationException và app tắt).
        services.AddSingleton(InMemoryLog);

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(InMemoryLog);
            builder.AddProvider(new RollingFileLoggerProvider(Path.Combine(baseDirectory, "Logs"), "launcher"));
        });

        services.AddSingleton(sp => new LauncherSettingsStore(
            Path.Combine(configDirectory, "launcher-settings.json"), sp.GetRequiredService<ILogger<LauncherSettingsStore>>()));
        services.AddSingleton<IModuleService, ModuleManager>();
        services.AddSingleton<IProcessManager, ProcessManager>();

        services.AddSingleton<ModuleListViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
