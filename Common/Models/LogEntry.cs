namespace Common.Models;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string Level { get; init; } = "Information";

    public string Category { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string DisplayText => ToString();

    public override string ToString() => $"[{Timestamp:HH:mm:ss}] [{Level}] [{Category}] {Message}";
}
