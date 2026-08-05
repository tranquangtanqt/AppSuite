using System.Text;
using OfficeOpenXml;

namespace ModuleG.Services;

/// <summary>Small row/label-scanning helpers shared by <see cref="OverviewSheetParser"/> and
/// <see cref="ScreenDiagramSheetParser"/>. Both sheets are authored with a "label cell, value cell(s)
/// to the right, next row" convention rather than a fixed column layout, so parsing works by matching
/// cell *text* (section markers like 【目的】, column headers like "処理名") instead of hardcoded
/// column indices - this is what lets the same code handle a file whose columns shifted slightly from
/// the sample used to write it, and to safely say "doesn't match" (see <see cref="ExcelSheetHtmlRenderer.RenderGridRange"/>
/// fallback) rather than silently misreading a differently-shaped sheet.</summary>
internal static class SemanticSheetHelpers
{
    public static (int Col, string Text)? FirstNonEmptyCell(ExcelWorksheet sheet, int row, int minCol, int maxCol)
    {
        foreach (var cell in NonEmptyCells(sheet, row, minCol, maxCol))
        {
            return cell;
        }

        return null;
    }

    public static IEnumerable<(int Col, string Text)> NonEmptyCells(ExcelWorksheet sheet, int row, int minCol, int maxCol)
    {
        for (var c = minCol; c <= maxCol; c++)
        {
            var text = sheet.Cells[row, c].Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return (c, text.Trim());
            }
        }
    }

    /// <summary>Scans <paramref name="searchStartRow"/>..<paramref name="searchEndRow"/> for the first
    /// row that contains every one of <paramref name="headers"/> as an exact (trimmed) cell match, and
    /// returns that row plus each header's column. Returns null if no such row exists in range - the
    /// caller then falls back to the generic grid for that section instead of guessing.</summary>
    public static (int HeaderRow, int[] Columns)? TryFindHeaderRow(
        ExcelWorksheet sheet, int searchStartRow, int searchEndRow, int minCol, int maxCol, string[] headers)
    {
        for (var row = searchStartRow; row <= searchEndRow; row++)
        {
            var columns = new int[headers.Length];
            Array.Fill(columns, -1);

            foreach (var (col, text) in NonEmptyCells(sheet, row, minCol, maxCol))
            {
                var idx = Array.IndexOf(headers, text);
                if (idx >= 0)
                {
                    columns[idx] = col;
                }
            }

            if (Array.TrueForAll(columns, c => c >= 0))
            {
                return (row, columns);
            }
        }

        return null;
    }

    /// <summary>Renders a `<table>` with the given header labels/columns, one `<tr>` per data row that
    /// has at least one non-empty tracked column (skips fully-blank padding rows). A "continuation"
    /// row where only 1 column has text (common in 目的/概要 detail lists) still renders as its own row
    /// with the other cells blank - matching the source layout instead of trying to merge it upward.</summary>
    public static void AppendLabeledTable(
        StringBuilder sb, ExcelWorksheet sheet, string[] headers, int[] columns,
        int dataStartRow, int dataEndRow, StringBuilder searchText, string tableClass = "ov-table")
    {
        sb.Append("<div class=\"table-scroll\"><table class=\"").Append(tableClass).Append("\"><thead><tr>");
        foreach (var header in headers)
        {
            sb.Append("<th>").Append(ExcelSheetHtmlRenderer.Escape(header)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");

        for (var row = dataStartRow; row <= dataEndRow; row++)
        {
            var rowHasContent = false;
            var rowHtml = new StringBuilder();
            foreach (var col in columns)
            {
                var text = sheet.Cells[row, col].Text ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    rowHasContent = true;
                    searchText.Append(text).Append(' ');
                }

                rowHtml.Append("<td>").Append(ExcelSheetHtmlRenderer.EscapeMultiline(text)).Append("</td>");
            }

            if (!rowHasContent)
            {
                continue;
            }

            sb.Append("<tr>").Append(rowHtml).Append("</tr>");
        }

        sb.Append("</tbody></table></div>");
    }

    /// <summary>Renders each non-blank row in range as its own paragraph, indented by how far its
    /// first non-empty column sits from <paramref name="minCol"/> - a cheap approximation of the
    /// bullet-point nesting these free-text sections use (deeper column = nested bullet), without
    /// needing to understand the actual bullet semantics.</summary>
    public static void AppendFreeTextParagraphs(
        StringBuilder sb, ExcelWorksheet sheet, int startRow, int endRow, int minCol, int maxCol, StringBuilder searchText)
    {
        sb.Append("<div class=\"ov-freetext\">");
        for (var row = startRow; row <= endRow; row++)
        {
            var cells = NonEmptyCells(sheet, row, minCol, maxCol).ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            var indentLevel = Math.Min(8, cells[0].Col - minCol);
            var text = string.Join(" ", cells.Select(c => c.Text));
            searchText.Append(text).Append(' ');

            sb.Append("<p class=\"ov-line\" style=\"padding-left:").Append(indentLevel * 10).Append("px\">")
              .Append(ExcelSheetHtmlRenderer.EscapeMultiline(text)).Append("</p>");
        }

        sb.Append("</div>");
    }

    /// <summary>Appends any embedded image anchored within [startRow, endRow] - a section rendered as
    /// a semantic table/paragraph block (instead of going through <see cref="ExcelSheetHtmlRenderer.RenderGridRange"/>,
    /// which interleaves images itself) would otherwise silently drop an image that happens to sit in
    /// its row range.</summary>
    public static void AppendImagesInRange(StringBuilder sb, SheetGridContext context, int startRow, int endRow)
    {
        foreach (var (anchorRow, images) in context.ImagesByAnchorRow)
        {
            if (anchorRow < startRow || anchorRow > endRow)
            {
                continue;
            }

            foreach (var imageUrl in images)
            {
                sb.Append("<div class=\"sheet-image\"><img src=\"").Append(ExcelSheetHtmlRenderer.Escape(imageUrl)).Append("\" loading=\"lazy\"></div>");
            }
        }
    }
}
