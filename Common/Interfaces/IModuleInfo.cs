namespace Common.Interfaces;

/// <summary>
/// Read-only contract describing a module the launcher can discover and run.
/// Implemented by <see cref="Common.Models.ModuleConfig"/>, which is what modules.json deserializes into.
/// </summary>
public interface IModuleInfo
{
    string Name { get; }

    string Description { get; }

    /// <summary>Path to the module executable, relative to the launcher's base directory or absolute.</summary>
    string Path { get; }

    /// <summary>Command-line arguments passed to the module process on start.</summary>
    string Arguments { get; }

    /// <summary>Whether the launcher should start this module automatically on startup.</summary>
    bool AutoStart { get; }
}
