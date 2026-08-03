using ModuleF.Models;

namespace ModuleF.Services;

public sealed class ValidationService : IValidationService
{
    private const double EmptyRatioWarningThreshold = 0.5;

    public List<ValidationIssue> ValidateColumns(CsvDocument document)
    {
        var issues = new List<ValidationIssue>();
        if (document.Rows.Count == 0)
        {
            return issues;
        }

        for (var columnIndex = 0; columnIndex < document.Columns.Count; columnIndex++)
        {
            var emptyCount = document.Rows.Count(r => string.IsNullOrEmpty(r.GetCell(columnIndex)));
            var ratio = emptyCount / (double)document.Rows.Count;
            if (ratio > EmptyRatioWarningThreshold)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Cột '{document.Columns[columnIndex].Name}' có {ratio:P0} ô rỗng ({emptyCount}/{document.Rows.Count} dòng).",
                    ColumnIndex = columnIndex,
                });
            }
        }

        return issues;
    }
}
