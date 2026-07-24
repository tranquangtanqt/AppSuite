using Common.Models;

namespace Common.Interfaces;

/// <summary>
/// Loads and exposes the set of modules known to the launcher, backed by modules.json (or any
/// other source that resolves to <see cref="ModuleConfig"/> instances).
/// </summary>
public interface IModuleService
{
    IReadOnlyList<ModuleConfig> Modules { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task ReloadAsync(CancellationToken cancellationToken = default);

    ModuleConfig? GetModule(string name);
}
