using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Common.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace MainLauncher.ViewModels;

/// <summary>Owns the live list of modules shown on the Module List page (and read by Dashboard).</summary>
public partial class ModuleListViewModel : ObservableObject
{
    private readonly IModuleService _moduleService;
    private readonly IProcessManager _processManager;
    private readonly ILogger<ModuleListViewModel> _logger;
    private bool _initialized;

    public ModuleListViewModel(IModuleService moduleService, IProcessManager processManager, ILogger<ModuleListViewModel> logger)
    {
        _moduleService = moduleService;
        _processManager = processManager;
        _logger = logger;
    }

    public ObservableCollection<ModuleViewModel> Modules { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    /// <summary>
    /// Called once by App.xaml.cs at startup: loads modules.json and starts every module flagged
    /// AutoStart. Safe to call more than once - only the first call actually auto-starts anything.
    /// </summary>
    public Task InitializeAsync()
    {
        if (_initialized)
            return Task.CompletedTask;

        _initialized = true;
        return LoadModulesCoreAsync(autoStart: true);
    }

    /// <summary>
    /// Re-reads modules.json and rebuilds the module list, but never starts or stops anything -
    /// each module's live Running/Stopped state (owned by IProcessManager) is left exactly as it
    /// was. Bound to the "Reload" button on the Module List and Settings pages; use the Start/Stop
    /// buttons on a ModuleCard to actually launch or close a module.
    /// </summary>
    [RelayCommand]
    public Task LoadModulesAsync() => LoadModulesCoreAsync(autoStart: false);

    private async Task LoadModulesCoreAsync(bool autoStart)
    {
        IsLoading = true;
        try
        {
            try
            {
                await _moduleService.LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load module configuration");
                return;
            }

            foreach (var existing in Modules)
                existing.Dispose();
            Modules.Clear();

            foreach (var config in _moduleService.Modules)
                Modules.Add(new ModuleViewModel(config, _processManager));

            if (!autoStart)
                return;

            foreach (var config in _moduleService.Modules.Where(m => m.AutoStart))
            {
                if (!_processManager.CheckRunning(config.Name))
                {
                    _logger.LogInformation("Auto-starting module {Module}", config.Name);
                    _processManager.StartProcess(config);
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
