using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ModuleB.Models;

namespace ModuleB.Services;

/// <summary>
/// Reads every non-empty cell of a .xlsx/.xlsm workbook, resolving shared strings and keeping the
/// sheet name + cell reference (e.g. "Sheet1"/"B7") so a match can later be jumped to in Excel.
/// </summary>
public static class ExcelCellExtractor
{
    public static List<ExcelCellMatch> ExtractCells(string filePath)
    {
        var cells = new List<ExcelCellMatch>();

        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart;
        var sheets = workbookPart?.Workbook.Sheets;
        if (workbookPart is null || sheets is null)
        {
            return cells;
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

        foreach (var sheet in sheets.Elements<Sheet>())
        {
            var relationshipId = sheet.Id?.Value;
            var sheetName = sheet.Name?.Value;
            if (relationshipId is null || sheetName is null)
            {
                continue;
            }

            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            foreach (var cell in worksheetPart.Worksheet.Descendants<Cell>())
            {
                var reference = cell.CellReference?.Value;
                var text = GetCellText(cell, sharedStrings);
                if (string.IsNullOrEmpty(reference) || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var (row, column) = ParseCellReference(reference);
                cells.Add(new ExcelCellMatch(sheetName, reference, row, column, text));
            }

            cells.AddRange(ExcelShapeExtractor.ExtractShapeTexts(worksheetPart, sheetName));
        }

        return cells;
    }

    private static string? GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (sharedStrings is not null && int.TryParse(cell.CellValue?.InnerText, out var index))
            {
                return sharedStrings.ElementAtOrDefault(index)?.InnerText;
            }

            return null;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.Text?.Text;
        }

        return cell.CellValue?.InnerText;
    }

    private static (int Row, int Column) ParseCellReference(string reference)
    {
        var i = 0;
        while (i < reference.Length && char.IsLetter(reference[i]))
        {
            i++;
        }

        var column = 0;
        for (var j = 0; j < i; j++)
        {
            column = column * 26 + (char.ToUpperInvariant(reference[j]) - 'A' + 1);
        }

        int.TryParse(reference[i..], out var row);
        return (row, column);
    }
}
