using System.Collections.ObjectModel;
using System.Linq;
using Common.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace MainLauncher.ViewModels;

/// <summary>Aggregate module counts and the live module list shown on the Dashboard (Home) page.</summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly IModuleService _moduleService;
    private readonly IProcessManager _processManager;
    private readonly ModuleListViewModel _moduleList;
    private readonly DispatcherQueue? _dispatcherQueue;

    public DashboardViewModel(IModuleService moduleService, IProcessManager processManager, ModuleListViewModel moduleList)
    {
        _moduleService = moduleService;
        _processManager = processManager;
        _moduleList = moduleList;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _processManager.StatusChanged += (_, _) => RefreshStatus();
        RefreshStatus();
    }

    /// <summary>Same live collection shown on the Module List page - Dashboard just renders it as a preview grid.</summary>
    public ObservableCollection<ModuleViewModel> Modules => _moduleList.Modules;

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
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            Update();
        else
            _dispatcherQueue.TryEnqueue(Update);
    }
}
