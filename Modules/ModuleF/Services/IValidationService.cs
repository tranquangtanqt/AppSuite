using ModuleF.Models;

namespace ModuleF.Services;

public interface IValidationService
{
    /// <summary>Column-level checks that need the fully loaded document (empty-cell ratio, ...).
    /// Row/field-count issues are collected inline while parsing (see <see cref="CsvFileService"/>)
    /// since that data is only available while streaming and would be wasteful to re-scan.</summary>
    List<ValidationIssue> ValidateColumns(CsvDocument document);
}
