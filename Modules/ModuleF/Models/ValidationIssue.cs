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

    /// <summary>True for issues <see cref="IValidationService"/> can re-derive from the current
    /// in-memory document (empty-column ratio, field-count mismatch) - the ViewModel discards and
    /// regenerates only these after an edit/Save, rather than the encoding/delimiter warnings and
    /// unclosed-quote errors from CsvParser/CsvFileService, which describe
    /// the original file text and can't be recomputed from parsed rows (the raw line is already gone).
    /// </summary>
    public bool IsRecomputable { get; init; }
}
