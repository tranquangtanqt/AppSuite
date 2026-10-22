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
    private readonly Services.LauncherSettingsStore _settingsStore;
    private bool _initialized;

    public ModuleListViewModel(IModuleService moduleService, IProcessManager processManager, ILogger<ModuleListViewModel> logger,
        Services.LauncherSettingsStore settingsStore)
    {
        _moduleService = moduleService;
        _processManager = processManager;
        _logger = logger;
        _settingsStore = settingsStore;
    }

    public ObservableCollection<ModuleViewModel> Modules { get; } = new();

    // ----- Thứ tự module (kéo thả thẻ ở Tổng quan / Danh sách module) -----

    /// <summary>Cấu hình theo thứ tự đã lưu (LauncherSettings.ModuleOrder); module chưa có trong đó (vd vừa thêm vào
    /// modules.json) đứng sau, giữ thứ tự của modules.json.</summary>
    private System.Collections.Generic.List<Common.Models.ModuleConfig> OrderedConfigs()
    {
        var order = _settingsStore.Current.ModuleOrder;
        var configs = _moduleService.Modules.ToList();
        return configs
            .Select((config, index) => (config, index, saved: order.FindIndex(n => string.Equals(n, config.Name, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(x => x.saved < 0 ? 1 : 0).ThenBy(x => x.saved < 0 ? x.index : x.saved)
            .Select(x => x.config)
            .ToList();
    }

    /// <summary>Sau khi kéo thả: <paramref name="visibleInNewOrder"/> là các thẻ đang hiện theo thứ tự mới. Module bị ẩn giữ
    /// nguyên chỗ, các chỗ của module đang hiện được điền lại theo thứ tự mới; lưu vào launcher-settings.json.</summary>
    public void ApplyVisibleOrder(System.Collections.Generic.IReadOnlyList<ModuleViewModel> visibleInNewOrder)
    {
        var visible = visibleInNewOrder.ToHashSet();
        var queue = new System.Collections.Generic.Queue<ModuleViewModel>(visibleInNewOrder);
        var newOrder = Modules.Select(m => visible.Contains(m) && queue.Count > 0 ? queue.Dequeue() : m).ToList();
        MoveTo(newOrder);
        _settingsStore.Current.ModuleOrder = newOrder.Select(m => m.Name).ToList();
        _settingsStore.Save();
        _logger.LogInformation("Module order changed: {Order}", string.Join(", ", _settingsStore.Current.ModuleOrder));
    }

    /// <summary>"Sắp xếp lại theo modules.json" (trang Cài đặt): bỏ thứ tự đã lưu.</summary>
    [RelayCommand]
    private void ResetOrder()
    {
        _settingsStore.Current.ModuleOrder.Clear();
        _settingsStore.Save();
        var byConfig = _moduleService.Modules.Select(c => Modules.FirstOrDefault(m => m.Config == c)).Where(m => m is not null).Select(m => m!).ToList();
        MoveTo(byConfig);
    }

    /// <summary>Đổi chỗ bằng Move (không Clear / Add) để thẻ không dựng lại, trạng thái / nút giữ nguyên.</summary>
    private void MoveTo(System.Collections.Generic.List<ModuleViewModel> newOrder)
    {
        for (var i = 0; i < newOrder.Count && i < Modules.Count; i++)
        {
            var current = Modules.IndexOf(newOrder[i]);
            if (current >= 0 && current != i)
                Modules.Move(current, i);
        }
    }

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

            // Module mở từ lần chạy MainLauncher trước (hoặc mở tay) vẫn đang chạy → hiện Running, Stop được, không Start bản thứ 2.
            var configs = _moduleService.Modules.ToList();
            await Task.Run(() => _processManager.AttachRunningProcesses(configs));

            foreach (var existing in Modules)
                existing.Dispose();
            Modules.Clear();

            foreach (var config in OrderedConfigs())
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
