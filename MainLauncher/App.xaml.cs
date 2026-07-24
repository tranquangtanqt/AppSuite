using System;
using System.IO;
using System.Threading.Tasks;
using Common.Interfaces;
using Common.Logging;
using MainLauncher.Services;
using MainLauncher.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        var logger = Services.GetRequiredService<ILogger<App>>();
        var moduleList = Services.GetRequiredService<ModuleListViewModel>();
        _ = LoadModulesOnStartupAsync(moduleList, logger);
    }

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

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(InMemoryLog);
            builder.AddProvider(new RollingFileLoggerProvider(Path.Combine(baseDirectory, "Logs"), "launcher"));
        });

        services.AddSingleton<IModuleService, ModuleManager>();
        services.AddSingleton<IProcessManager, ProcessManager>();

        services.AddSingleton<ModuleListViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
