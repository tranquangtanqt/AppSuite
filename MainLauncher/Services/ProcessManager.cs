using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Common.Interfaces;
using Common.Models;
using Microsoft.Extensions.Logging;

namespace MainLauncher.Services;

/// <summary>
/// Starts, stops, restarts and tracks module processes with <see cref="Process"/>. Every module's
/// running state lives here; view models only ever read it through <see cref="IProcessManager"/>.
/// </summary>
public sealed class ProcessManager : IProcessManager
{
    private readonly ILogger<ProcessManager> _logger;
    private readonly ConcurrentDictionary<string, Process> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ModuleProcessInfo> _statuses = new(StringComparer.OrdinalIgnoreCase);

    public ProcessManager(ILogger<ProcessManager> logger)
    {
        _logger = logger;
    }

    public event EventHandler<ModuleProcessStatusChangedEventArgs>? StatusChanged;

    public bool StartProcess(ModuleConfig module)
    {
        if (CheckRunning(module.Name))
        {
            _logger.LogWarning("Module {Module} is already running", module.Name);
            return false;
        }

        var exePath = module.ResolveExecutablePath(AppContext.BaseDirectory);
        if (!File.Exists(exePath))
        {
            _logger.LogError("Executable not found for module {Module}: {Path}", module.Name, exePath);
            SetStatus(module.Name, ModuleStatus.Error, null, $"Executable not found: {exePath}");
            return false;
        }

        try
        {
            SetStatus(module.Name, ModuleStatus.Starting, null);

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = module.Arguments,
                WorkingDirectory = Path.GetDirectoryName(exePath),
                UseShellExecute = false
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += (_, _) => OnProcessExited(module.Name);

            if (!process.Start())
            {
                SetStatus(module.Name, ModuleStatus.Error, null, "Process failed to start");
                return false;
            }

            _processes[module.Name] = process;
            SetStatus(module.Name, ModuleStatus.Running, process.Id);
            _logger.LogInformation("Started module {Module} (PID {Pid}) with arguments '{Args}'", module.Name, process.Id, module.Arguments);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start module {Module}", module.Name);
            SetStatus(module.Name, ModuleStatus.Error, null, ex.Message);
            return false;
        }
    }

    public bool StopProcess(string moduleName, bool force = false)
    {
        if (!_processes.TryGetValue(moduleName, out var process))
            return false;

        try
        {
            SetStatus(moduleName, ModuleStatus.Stopping, process.Id);

            if (!process.HasExited)
            {
                if (force)
                {
                    process.Kill(entireProcessTree: true);
                }
                else
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(5000))
                        process.Kill(entireProcessTree: true);
                }
            }

            _logger.LogInformation("Stopped module {Module}", moduleName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop module {Module}", moduleName);
            SetStatus(moduleName, ModuleStatus.Error, null, ex.Message);
            return false;
        }
        finally
        {
            _processes.TryRemove(moduleName, out _);
        }
    }

    public bool RestartProcess(ModuleConfig module)
    {
        StopProcess(module.Name, force: true);
        return StartProcess(module);
    }

    public bool CheckRunning(string moduleName)
    {
        if (!_processes.TryGetValue(moduleName, out var process))
            return false;

        try
        {
            process.Refresh();
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public ModuleProcessInfo GetProcessStatus(string moduleName)
        => _statuses.TryGetValue(moduleName, out var info)
            ? info
            : new ModuleProcessInfo { ModuleName = moduleName, Status = ModuleStatus.Stopped };

    public IReadOnlyCollection<ModuleProcessInfo> GetAllStatuses() => _statuses.Values.ToArray();

    private void OnProcessExited(string moduleName)
    {
        _processes.TryRemove(moduleName, out _);
        SetStatus(moduleName, ModuleStatus.Stopped, null);
        _logger.LogInformation("Module {Module} exited", moduleName);
    }

    private void SetStatus(string moduleName, ModuleStatus status, int? processId, string? error = null)
    {
        var previousStartTime = _statuses.TryGetValue(moduleName, out var previous) ? previous.StartTime : null;

        var info = new ModuleProcessInfo
        {
            ModuleName = moduleName,
            Status = status,
            ProcessId = processId,
            StartTime = status == ModuleStatus.Running ? DateTimeOffset.Now : previousStartTime,
            LastError = error
        };

        _statuses[moduleName] = info;
        StatusChanged?.Invoke(this, new ModuleProcessStatusChangedEventArgs(info));
    }
}
