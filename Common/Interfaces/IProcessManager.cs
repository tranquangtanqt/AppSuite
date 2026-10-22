using Common.Models;

namespace Common.Interfaces;

/// <summary>
/// Starts, stops and monitors module processes. MainLauncher's ProcessManager implements this
/// directly against <see cref="System.Diagnostics.Process"/>; the interface exists so the launcher's
/// view models and services never depend on the concrete implementation.
/// </summary>
public interface IProcessManager
{
    event EventHandler<ModuleProcessStatusChangedEventArgs>? StatusChanged;

    bool StartProcess(ModuleConfig module);

    bool StopProcess(string moduleName, bool force = false);

    bool RestartProcess(ModuleConfig module);

    bool CheckRunning(string moduleName);

    /// <summary>Nhận lại module đang chạy từ trước (vd mở bằng MainLauncher lần trước, rồi đóng MainLauncher) - tìm tiến
    /// trình theo đúng đường dẫn .exe của module, theo dõi như module tự mở (Running, Stop được). Trả số module nhận lại.</summary>
    int AttachRunningProcesses(IEnumerable<ModuleConfig> modules);

    ModuleProcessInfo GetProcessStatus(string moduleName);

    IReadOnlyCollection<ModuleProcessInfo> GetAllStatuses();
}
