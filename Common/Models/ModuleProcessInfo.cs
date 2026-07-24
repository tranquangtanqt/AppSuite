namespace Common.Models;

/// <summary>Point-in-time runtime status of a module's process.</summary>
public sealed class ModuleProcessInfo
{
    public required string ModuleName { get; init; }

    public ModuleStatus Status { get; set; } = ModuleStatus.Unknown;

    public int? ProcessId { get; set; }

    public DateTimeOffset? StartTime { get; set; }

    public string? LastError { get; set; }
}
