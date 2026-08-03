namespace ModuleF.Models;

public enum ValidationSeverity
{
    Warning,
    Error,
}

/// <summary>One validation finding surfaced after opening a file (mismatched field count, unparsable
/// quoting, suspiciously empty column, low-confidence delimiter/encoding detection, ...). Best-effort:
/// issues never block opening the file, they are just reported.</summary>
public sealed class ValidationIssue
{
    public required ValidationSeverity Severity { get; init; }
    public required string Message { get; init; }
    public int? RowIndex { get; init; }
    public int? ColumnIndex { get; init; }
}
