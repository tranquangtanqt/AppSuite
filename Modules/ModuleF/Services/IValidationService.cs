using ModuleF.Models;

namespace ModuleF.Services;

public interface IValidationService
{
    /// <summary>Column-level checks that need the fully loaded document (empty-cell ratio, ...).</summary>
    List<ValidationIssue> ValidateColumns(CsvDocument document);

    /// <summary>Row-level field-count-vs-header check, re-derivable from the in-memory rows (see
    /// <see cref="ValidationService.ValidateFieldCounts"/>) - separate from the one collected while
    /// parsing in <see cref="CsvFileService"/>, which is cheaper for the initial Open but goes stale.
    /// </summary>
    List<ValidationIssue> ValidateFieldCounts(CsvDocument document);
}
