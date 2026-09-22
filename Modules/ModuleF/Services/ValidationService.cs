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
                    IsRecomputable = true,
                });
            }
        }

        return issues;
    }

    /// <summary>Re-derives the "Dòng N: có X field, header có Y cột" warning from the current in-memory
    /// rows (comparing <see cref="CsvRow.CellCount"/> against the column count) - used to refresh that
    /// warning after an edit closes the gap (e.g. typing into the row's missing trailing cells), unlike
    /// the one-shot version collected while parsing in <see cref="CsvFileService"/>, which never updates.
    /// </summary>
    public List<ValidationIssue> ValidateFieldCounts(CsvDocument document)
    {
        var issues = new List<ValidationIssue>();
        for (var rowIndex = 0; rowIndex < document.Rows.Count; rowIndex++)
        {
            var cellCount = document.Rows[rowIndex].CellCount;
            if (cellCount != document.Columns.Count)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Dòng {rowIndex + 1}: có {cellCount} field, header có {document.Columns.Count} cột.",
                    RowIndex = rowIndex,
                    IsRecomputable = true,
                });
            }
        }

        return issues;
    }
}
