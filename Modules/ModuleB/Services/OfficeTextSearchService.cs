using System;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ModuleB.Services;

/// <summary>
/// Best-effort text extraction for keyword search. Only the OpenXML formats (.xlsx/.docx) can be
/// read; legacy binary formats (.xls/.doc) and anything else return null so callers can fall back
/// to matching the file name instead of silently excluding the file.
/// </summary>
public sealed class OfficeTextSearchService
{
    public string? ExtractText(string filePath)
    {
        try
        {
            return Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".xlsx" or ".xlsm" => ExtractFromWorkbook(filePath),
                ".docx" => ExtractFromWordDocument(filePath),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string ExtractFromWorkbook(string filePath)
    {
        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        if (sharedStrings is not null)
        {
            foreach (var item in sharedStrings.Elements<SharedStringItem>())
            {
                sb.AppendLine(item.InnerText);
            }
        }

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            foreach (var cell in worksheetPart.Worksheet.Descendants<Cell>())
            {
                if (cell.DataType?.Value == CellValues.InlineString)
                {
                    sb.AppendLine(cell.InlineString?.Text?.Text);
                }
                else if (cell.DataType is null && cell.CellValue is not null)
                {
                    sb.AppendLine(cell.CellValue.InnerText);
                }
            }
        }

        return sb.ToString();
    }

    private static string ExtractFromWordDocument(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false);
        return document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    }
}
