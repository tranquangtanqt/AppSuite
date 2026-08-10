using System.Collections.Generic;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using ModuleB.Models;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ModuleB.Services;

/// <summary>
/// Reads text out of drawing shapes (xdr:sp/xdr:txBody) anchored on a worksheet - separate XML from
/// &lt;sheetData&gt;, so ExcelCellExtractor's Cell walk never sees it. Mirrors
/// ModuleG.Services.DiagramXmlReader.ReadText's paragraph/run/line-break flattening, but through the
/// strongly-typed OpenXml SDK objects (ModuleB references DocumentFormat.OpenXml, not EPPlus).
/// A shape has no cell of its own, so its text is attributed to the cell nearest its top-left anchor
/// (xdr:from row/col) - close enough to open Excel and land next to the shape.
/// </summary>
public static class ExcelShapeExtractor
{
    public static List<ExcelCellMatch> ExtractShapeTexts(WorksheetPart worksheetPart, string sheetName)
    {
        var results = new List<ExcelCellMatch>();

        var worksheetDrawing = worksheetPart.DrawingsPart?.WorksheetDrawing;
        if (worksheetDrawing is null)
        {
            return results;
        }

        foreach (var shape in worksheetDrawing.Descendants<Xdr.Shape>())
        {
            var text = ReadText(shape.TextBody);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var anchor = FindAnchorRowColumn(shape);
            if (anchor is null)
            {
                continue;
            }

            var (row0, column0) = anchor.Value;
            var row = row0 + 1;
            var column = column0 + 1;

            results.Add(new ExcelCellMatch(sheetName, ToCellReference(row, column), row, column, text));
        }

        return results;
    }

    private static (int Row0, int Column0)? FindAnchorRowColumn(Xdr.Shape shape)
    {
        var fromMarker = shape.Parent switch
        {
            Xdr.TwoCellAnchor two => two.FromMarker,
            Xdr.OneCellAnchor one => one.FromMarker,
            _ => null,
        };

        if (fromMarker?.RowId?.Text is not { } rowText || fromMarker.ColumnId?.Text is not { } columnText)
        {
            return null;
        }

        if (!int.TryParse(rowText, out var row) || !int.TryParse(columnText, out var column))
        {
            return null;
        }

        return (row, column);
    }

    private static string? ReadText(Xdr.TextBody? textBody)
    {
        if (textBody is null)
        {
            return null;
        }

        var lines = new List<string>();
        foreach (var paragraph in textBody.Elements<Drawing.Paragraph>())
        {
            var current = new StringBuilder();
            foreach (var child in paragraph.ChildElements)
            {
                switch (child)
                {
                    case Drawing.Run run:
                        current.Append(run.Text?.Text);
                        break;
                    case Drawing.Break:
                        lines.Add(current.ToString());
                        current.Clear();
                        break;
                }
            }

            lines.Add(current.ToString());
        }

        var joined = string.Join('\n', lines).Trim();
        return joined.Length > 0 ? joined : null;
    }

    private static string ToCellReference(int row, int column)
    {
        var letters = new StringBuilder();
        while (column > 0)
        {
            var remainder = (column - 1) % 26;
            letters.Insert(0, (char)('A' + remainder));
            column = (column - 1) / 26;
        }

        return $"{letters}{row}";
    }
}
