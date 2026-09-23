using System.Text;
using OfficeOpenXml;

namespace Mcf.Screen.HtmlGenerator.Services;

/// <summary>
/// Compact renderer for the "項目説明" (field description) sheet - by far the densest sheet (up to
/// 358 merged cells / 397 rows per PLAN.md), where the full generic grid renders a slow, sprawling
/// wall of mostly-empty cells. Unlike <see cref="OverviewSheetParser"/>/<see cref="ScreenDiagramSheetParser"/>
/// this makes no attempt at a faithful structural rebuild - it keeps to the required marker/field name/
/// description columns plus every one of the short flag columns the sheet defines (説明 header row
/// carries 型/参/コ/入/非/O next to it, in that order - reference legend in the screenshot the user
/// sent: リ/型/参/コ/入/非/O), dropping only colour coding and merge geometry. The sheet repeats a
/// group header row (操作種別/検索/登録/...) that doubles as the column header for its group (always
/// carries a "説明" cell) - that's what both announces a new group and locates each flag column for
/// it, so a slightly different column layout per group is still handled correctly (a group missing a
/// given flag just renders that cell blank). Groups render as full-width banded rows inside 1
/// continuous table (rather than 1 separate table per group), matching how the source Excel bands
/// each group's header across the full row width. Falls back to the generic grid if no "説明" header
/// is found at all (sheet doesn't match the expected convention).
/// </summary>
internal static class ItemExplanationSheetParser
{
    /// <summary>Flag columns as labelled in the sheet's own header row, in their natural left-to-right
    /// order (説明 itself is handled separately since it's never blank-worthy the way these are).</summary>
    private static readonly string[] FlagLabels = { "リ", "型", "参", "コ", "入", "非", "O" };

    public static bool TryRender(SheetGridContext context, int minRow, int maxRow, StringBuilder searchText, out string html)
    {
        var sheet = context.Sheet;
        var minCol = context.MinCol;
        var maxCol = context.MaxCol;
        var markerCol = minCol;
        var nameCol = minCol + 1;

        var headerRows = new List<int>();
        for (var row = minRow; row <= maxRow; row++)
        {
            if (SemanticSheetHelpers.NonEmptyCells(sheet, row, minCol, maxCol).Any(c => c.Text == "説明"))
            {
                headerRows.Add(row);
            }
        }

        if (headerRows.Count == 0)
        {
            html = string.Empty;
            return false;
        }

        // Column set must be identical for every group so a group-band row's colspan lines up and the
        // table has 1 consistent header - a flag column is included sheet-wide if ANY group has it
        // (some groups, e.g. plain search filter fields, carry fewer flags than others).
        var groups = new List<(int HeaderRow, int SectionEnd, string Name, int DescCol, int[] FlagCols)>();
        var hasFlag = new bool[FlagLabels.Length];
        for (var i = 0; i < headerRows.Count; i++)
        {
            var headerRow = headerRows[i];
            var sectionEnd = i + 1 < headerRows.Count ? headerRows[i + 1] - 1 : maxRow;
            var headerCells = SemanticSheetHelpers.NonEmptyCells(sheet, headerRow, minCol, maxCol).ToList();
            var groupName = headerCells.Count > 0 ? headerCells[0].Text : string.Empty;
            var descCol = headerCells.First(c => c.Text == "説明").Col;

            var flagCols = new int[FlagLabels.Length];
            for (var f = 0; f < FlagLabels.Length; f++)
            {
                var match = headerCells.FirstOrDefault(c => c.Text == FlagLabels[f]);
                flagCols[f] = match.Col;
                if (match.Col > 0)
                {
                    hasFlag[f] = true;
                }
            }

            groups.Add((headerRow, sectionEnd, groupName, descCol, flagCols));
        }

        var columnCount = 3 + hasFlag.Count(x => x);

        // Rows before the first group header are just the repeated mcframe/module/document-number
        // header block, already shown in the page's own header chips - skip rendering it here entirely.
        var sb = new StringBuilder();
        sb.Append("<div class=\"semantic-doc\">");

        sb.Append("<div class=\"table-scroll\"><table class=\"ov-table item-table\"><thead><tr><th>必須</th><th>項目名</th><th>説明</th>");
        for (var f = 0; f < FlagLabels.Length; f++)
        {
            if (hasFlag[f])
            {
                sb.Append("<th>").Append(FlagLabels[f]).Append("</th>");
            }
        }

        sb.Append("</tr></thead><tbody>");

        foreach (var group in groups)
        {
            sb.Append("<tr class=\"item-group-row\"><td colspan=\"").Append(columnCount).Append("\">")
              .Append(ExcelSheetHtmlRenderer.Escape(group.Name)).Append("</td></tr>");

            for (var row = group.HeaderRow + 1; row <= group.SectionEnd; row++)
            {
                var marker = sheet.Cells[row, markerCol].Text ?? string.Empty;
                var name = sheet.Cells[row, nameCol].Text ?? string.Empty;
                var description = ReadNearby(sheet, row, group.DescCol, 2);

                var flagValues = new string[FlagLabels.Length];
                var anyFlagValue = false;
                for (var f = 0; f < FlagLabels.Length; f++)
                {
                    var value = group.FlagCols[f] > 0 ? sheet.Cells[row, group.FlagCols[f]].Text ?? string.Empty : string.Empty;
                    flagValues[f] = value;
                    anyFlagValue |= !string.IsNullOrWhiteSpace(value);
                }

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(description) && !anyFlagValue)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    searchText.Append(name).Append(' ');
                }

                if (!string.IsNullOrWhiteSpace(description))
                {
                    searchText.Append(description).Append(' ');
                }

                sb.Append("<tr><td>").Append(ExcelSheetHtmlRenderer.Escape(marker)).Append("</td><td>")
                  .Append(ExcelSheetHtmlRenderer.EscapeMultiline(name)).Append("</td><td>")
                  .Append(ExcelSheetHtmlRenderer.EscapeMultiline(description)).Append("</td>");

                for (var f = 0; f < FlagLabels.Length; f++)
                {
                    if (hasFlag[f])
                    {
                        sb.Append("<td>").Append(ExcelSheetHtmlRenderer.Escape(flagValues[f])).Append("</td>");
                    }
                }

                sb.Append("</tr>");
            }
        }

        sb.Append("</tbody></table></div>");
        SemanticSheetHelpers.AppendImagesInRange(sb, context, headerRows[0], maxRow);
        sb.Append("</div>");

        html = sb.ToString();
        return true;
    }

    /// <summary>The "説明" header's own column doesn't always line up with the description value's
    /// column in the data rows below it (an off-by-one seen in real files, likely a header cell
    /// indented for readability) - check a couple of columns to its left for the first non-empty cell
    /// instead of assuming an exact match.</summary>
    private static string ReadNearby(ExcelWorksheet sheet, int row, int col, int lookback)
    {
        for (var c = col; c >= col - lookback; c--)
        {
            var text = sheet.Cells[row, c].Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }
}
