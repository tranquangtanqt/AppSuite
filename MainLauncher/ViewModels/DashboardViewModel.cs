using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Common.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MainLauncher.Services;
using Microsoft.UI.Dispatching;

namespace MainLauncher.ViewModels;

/// <summary>Aggregate module counts and the live module list shown on the Dashboard (Home) page.</summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly IModuleService _moduleService;
    private readonly IProcessManager _processManager;
    private readonly ModuleListViewModel _moduleList;
    private readonly LauncherSettingsStore _settingsStore;
    private readonly LauncherSettings _settings;
    private readonly HashSet<string> _hidden;
    private readonly DispatcherQueue? _dispatcherQueue;

    public DashboardViewModel(IModuleService moduleService, IProcessManager processManager, ModuleListViewModel moduleList,
        LauncherSettingsStore settingsStore)
    {
        _moduleService = moduleService;
        _processManager = processManager;
        _moduleList = moduleList;
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        _hidden = new HashSet<string>(_settings.HiddenModules, StringComparer.OrdinalIgnoreCase);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _processManager.StatusChanged += (_, _) => RefreshStatus();
        _moduleList.Modules.CollectionChanged += (_, _) =>
        {
            RebuildVisibilityItems();
            RefreshVisibleModules();
        };
        RebuildVisibilityItems();
        RefreshVisibleModules();
        RefreshStatus();
    }

    /// <summary>Same live collection shown on the Module List page - Dashboard just renders it as a preview grid.</summary>
    public ObservableCollection<ModuleViewModel> Modules => _moduleList.Modules;

    /// <summary>Module hiện ở Dashboard và Module List: các module không bị ẩn, cùng module bị ẩn nhưng đang chạy (để còn bấm Stop).</summary>
    public ObservableCollection<ModuleViewModel> VisibleModules { get; } = new();

    /// <summary>Danh sách ô tích Ẩn / hiện (flyout ở Dashboard / Module List và trang Settings dùng chung).</summary>
    public ObservableCollection<ModuleVisibilityItem> VisibilityItems { get; } = new();

    /// <summary>"Đang hiện 9 / 12" (đếm theo lựa chọn, không tính module ẩn đang chạy tạm hiện) hoặc "Hiện tất cả 12".</summary>
    public string VisibleSummary
    {
        get
        {
            var shown = Modules.Count(m => !_hidden.Contains(m.Name));
            return shown == Modules.Count ? $"Hiện tất cả {Modules.Count}" : $"Đang hiện {shown} / {Modules.Count}";
        }
    }

    public bool IsAllHidden => Modules.Count > 0 && VisibleModules.Count == 0;

    [ObservableProperty]
    private int totalModules;

    [ObservableProperty]
    private int runningModules;

    [ObservableProperty]
    private int stoppedModules;

    public string HeaderSubtitle => TotalModules == 0
        ? "Chua co module nao duoc cau hinh"
        : $"{RunningModules}/{TotalModules} module dang chay";

    [RelayCommand]
    private void RefreshStatus()
    {
        void Update()
        {
            var modules = _moduleService.Modules;
            TotalModules = modules.Count;
            RunningModules = modules.Count(m => _processManager.CheckRunning(m.Name));
            StoppedModules = TotalModules - RunningModules;
            OnPropertyChanged(nameof(HeaderSubtitle));
            RefreshVisibleModules(); // module ẩn vừa chạy / vừa tắt thì hiện / ẩn theo
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            Update();
        else
            _dispatcherQueue.TryEnqueue(Update);
    }

    [RelayCommand]
    private void ShowAllModules()
    {
        foreach (var item in VisibilityItems)
            item.IsVisible = true; // mỗi lần đổi gọi OnVisibilityChanged
    }

    private void RebuildVisibilityItems()
    {
        VisibilityItems.Clear();
        foreach (var module in Modules)
            VisibilityItems.Add(new ModuleVisibilityItem(module.Name, module.Description, !_hidden.Contains(module.Name), OnVisibilityChanged));
    }

    private void OnVisibilityChanged(ModuleVisibilityItem item)
    {
        var changed = item.IsVisible ? _hidden.Remove(item.Name) : _hidden.Add(item.Name);
        if (!changed)
            return;
        _settings.HiddenModules = _hidden.Order(StringComparer.OrdinalIgnoreCase).ToList();
        _settingsStore.Save(_settings);
        RefreshVisibleModules();
    }

    /// <summary>Dựng lại VisibleModules khi khác thứ tự / thành phần (giữ nguyên nếu không đổi để thẻ không nháy).</summary>
    private void RefreshVisibleModules()
    {
        var desired = Modules.Where(m => !_hidden.Contains(m.Name) || _processManager.CheckRunning(m.Name)).ToList();
        if (!desired.SequenceEqual(VisibleModules))
        {
            VisibleModules.Clear();
            foreach (var module in desired)
                VisibleModules.Add(module);
        }
        OnPropertyChanged(nameof(VisibleSummary));
        OnPropertyChanged(nameof(IsAllHidden));
    }
}
