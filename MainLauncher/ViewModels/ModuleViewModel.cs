using System;
using Common.Interfaces;
using Common.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace MainLauncher.ViewModels;

/// <summary>Wraps one <see cref="ModuleConfig"/> with live status and Start/Stop/Restart commands.</summary>
public partial class ModuleViewModel : ObservableObject, IDisposable
{
    private readonly IProcessManager _processManager;
    private readonly DispatcherQueue? _dispatcherQueue;

    public ModuleViewModel(ModuleConfig config, IProcessManager processManager)
    {
        Config = config;
        _processManager = processManager;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _processManager.StatusChanged += HandleProcessStatusChanged;

        var info = _processManager.GetProcessStatus(config.Name);
        status = info.Status;
        processId = info.ProcessId;
    }

    public ModuleConfig Config { get; }

    public string Name => Config.Name;

    public string Description => Config.Description;

    [ObservableProperty]
    private ModuleStatus status;

    [ObservableProperty]
    private int? processId;

    public bool CanStart => Status is ModuleStatus.Stopped or ModuleStatus.Error or ModuleStatus.Unknown;

    public bool CanStop => Status is ModuleStatus.Running or ModuleStatus.Starting;

    partial void OnStatusChanged(ModuleStatus value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
    }

    [RelayCommand]
    private void Start() => _processManager.StartProcess(Config);

    [RelayCommand]
    private void Stop() => _processManager.StopProcess(Config.Name);

    [RelayCommand]
    private void Restart() => _processManager.RestartProcess(Config);

    private void HandleProcessStatusChanged(object? sender, ModuleProcessStatusChangedEventArgs e)
    {
        if (!string.Equals(e.ProcessInfo.ModuleName, Config.Name, StringComparison.OrdinalIgnoreCase))
            return;

        void Update()
        {
            Status = e.ProcessInfo.Status;
            ProcessId = e.ProcessInfo.ProcessId;
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            Update();
        else
            _dispatcherQueue.TryEnqueue(Update);
    }

    public void Dispose() => _processManager.StatusChanged -= HandleProcessStatusChanged;
}
