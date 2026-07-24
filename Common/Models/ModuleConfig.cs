using Common.Interfaces;

namespace Common.Models;

/// <summary>Deserialized shape of one entry in modules.json.</summary>
public sealed class ModuleConfig : IModuleInfo
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public bool AutoStart { get; set; }

    /// <summary>Resolves <see cref="Path"/> (which may be relative) against a base directory.</summary>
    public string ResolveExecutablePath(string baseDirectory)
        => System.IO.Path.IsPathRooted(Path) ? Path : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, Path));
}
