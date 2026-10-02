using CsvEditor.Models;

namespace CsvEditor.Services;

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

    /// <summary>Re-derives the "Dòng N: có X field" warning from the current in-memory
    /// rows (comparing each <see cref="CsvRow.CellCount"/> with the most common one) - used to refresh that
    /// warning after an edit closes the gap (e.g. typing into the row's missing trailing cells), unlike
    /// the one-shot version collected while parsing in <see cref="CsvFileService"/>, which never updates.
    /// So với số field mà nhiều dòng có nhất (hoà thì lấy số cột của bảng), không so với số cột: bảng được nới thêm cột
    /// cho vài dòng dài hơn header (xem CsvFileService) thì mọi dòng bình thường không bị báo lệch.
    /// </summary>
    public List<ValidationIssue> ValidateFieldCounts(CsvDocument document)
    {
        var issues = new List<ValidationIssue>();
        if (document.Rows.Count == 0)
        {
            return issues;
        }

        var reference = document.Rows.GroupBy(r => r.CellCount)
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key == document.Columns.Count)
            .First().Key;
        for (var rowIndex = 0; rowIndex < document.Rows.Count; rowIndex++)
        {
            var cellCount = document.Rows[rowIndex].CellCount;
            if (cellCount != reference)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Dòng {rowIndex + 1}: có {cellCount} field, phần lớn các dòng có {reference} field.",
                    RowIndex = rowIndex,
                    IsRecomputable = true,
                });
            }
        }

        return issues;
    }
}
