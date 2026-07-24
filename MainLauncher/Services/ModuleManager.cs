using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Interfaces;
using Common.Models;
using Common.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MainLauncher.Services;

/// <summary>
/// Loads modules.json into <see cref="ModuleConfig"/> instances. The launcher never hard-codes
/// which modules exist - this is the only place that reads the config file.
/// </summary>
public sealed class ModuleManager : IModuleService
{
    private readonly string _configPath;
    private readonly ILogger<ModuleManager> _logger;
    private List<ModuleConfig> _modules = new();

    public ModuleManager(IConfiguration configuration, ILogger<ModuleManager> logger)
    {
        _logger = logger;
        var relativePath = configuration["ModulesConfigPath"] ?? "Config/modules.json";
        _configPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
    }

    public IReadOnlyList<ModuleConfig> Modules => _modules;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading module configuration from {Path}", _configPath);
        _modules = await JsonConfigLoader.LoadModulesAsync(_configPath, cancellationToken);
        _logger.LogInformation("Loaded {Count} module(s)", _modules.Count);
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);

    public ModuleConfig? GetModule(string name)
        => _modules.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
