using System.Text;
using OfficeOpenXml;

namespace ModuleG.Services;

/// <summary>
/// Semantic renderer for the "画面遷移" (screen transition) sheet, matching the structure the
/// reference web app (mcf7-web-develop .../document/screen/screen-diagram) understands: repeated
/// blocks, each starting with an "ID / 処理名 / ダブルクリック対象 / ダブルクリック処理条件" header row
/// and its data row, followed by one or more 遷移処理/復帰処理 detail sections (destination screen,
/// condition, a small 遷移元画面項目→遷移先画面項目 mapping table, and the resulting action). Like
/// <see cref="OverviewSheetParser"/>, this locates columns by matching label cell text rather than
/// hardcoded offsets, and falls back to <see cref="ExcelSheetHtmlRenderer.RenderGridRange"/> for any
/// block/detail it can't recognize so a differently-shaped sheet still renders its content.
/// </summary>
internal static class ScreenDiagramSheetParser
{
    private static readonly string[] BlockHeaders = { "ID", "処理名", "ダブルクリック対象", "ダブルクリック処理条件" };

    private static readonly HashSet<string> DetailLabels = new()
    {
        "遷移処理", "復帰処理", "遷移先画面ＩＤ", "遷移先画面名", "遷移条件", "遷移後のアクション", "引継項目・種別", "引継項目",
    };

    private enum DiagramMode { None, Transition, Return }

    private sealed class DiagramDetail
    {
        public string ScreenId = string.Empty;
        public string ScreenName = string.Empty;
        public string Condition = string.Empty;
        public string Action = string.Empty;
        public readonly List<(string Source, string Destination)> TransitionItems = new();
        public readonly List<(string Source, string Destination)> ReturnItems = new();
    }

    public static bool TryRender(SheetGridContext context, int minRow, int maxRow, StringBuilder searchText, out string html)
    {
        var sheet = context.Sheet;
        var minCol = context.MinCol;
        var maxCol = context.MaxCol;

        // Block headers ("ID / 処理名 / ...") are only ever authored near the left edge (column A in
        // every sample seen) - restrict the scan so an incidental "ID" value elsewhere in a data row
        // is never misread as the start of a new block (same reasoning as OverviewSheetParser's
        // marker-column restriction).
        var blockMarkerColMax = Math.Min(maxCol, minCol + 1);
        var blockStarts = new List<int>();
        for (var row = minRow; row <= maxRow; row++)
        {
            var first = SemanticSheetHelpers.FirstNonEmptyCell(sheet, row, minCol, blockMarkerColMax);
            if (first is { Text: "ID" })
            {
                blockStarts.Add(row);
            }
        }

        if (blockStarts.Count == 0)
        {
            html = string.Empty;
            return false;
        }

        // Rows before the first ID block are just the repeated mcframe/module/document-number header
        // block, already shown in the page's own header chips - skip rendering it here entirely.
        var sb = new StringBuilder();
        sb.Append("<div class=\"semantic-doc\">");

        for (var i = 0; i < blockStarts.Count; i++)
        {
            var blockRow = blockStarts[i];
            var blockEnd = i + 1 < blockStarts.Count ? blockStarts[i + 1] - 1 : maxRow;
            AppendBlock(sb, context, blockRow, blockEnd, searchText);
        }

        sb.Append("</div>");
        html = sb.ToString();
        return true;
    }

    private static void AppendBlock(StringBuilder sb, SheetGridContext context, int blockRow, int blockEnd, StringBuilder searchText)
    {
        var sheet = context.Sheet;
        var header = SemanticSheetHelpers.TryFindHeaderRow(sheet, blockRow, blockRow, context.MinCol, context.MaxCol, BlockHeaders);
        if (header is not { } found)
        {
            sb.Append(ExcelSheetHtmlRenderer.RenderGridRange(context, blockRow, blockEnd, searchText));
            return;
        }

        var dataRow = found.HeaderRow + 1;
        var id = dataRow <= blockEnd ? sheet.Cells[dataRow, found.Columns[0]].Text ?? string.Empty : string.Empty;
        var name = dataRow <= blockEnd ? sheet.Cells[dataRow, found.Columns[1]].Text ?? string.Empty : string.Empty;
        var target = dataRow <= blockEnd ? sheet.Cells[dataRow, found.Columns[2]].Text ?? string.Empty : string.Empty;
        var condition = dataRow <= blockEnd ? sheet.Cells[dataRow, found.Columns[3]].Text ?? string.Empty : string.Empty;
        foreach (var value in new[] { id, name, target, condition })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                searchText.Append(value).Append(' ');
            }
        }

        sb.Append("<div class=\"diag-block\"><div class=\"diag-head\">")
          .Append("<span class=\"chip\">ID: ").Append(ExcelSheetHtmlRenderer.Escape(id)).Append("</span>")
          .Append("<span class=\"chip\">処理名: ").Append(ExcelSheetHtmlRenderer.Escape(name)).Append("</span>");

        if (!string.IsNullOrWhiteSpace(target))
        {
            sb.Append("<span class=\"chip\">ダブルクリック対象: ").Append(ExcelSheetHtmlRenderer.Escape(target)).Append("</span>");
        }

        if (!string.IsNullOrWhiteSpace(condition))
        {
            sb.Append("<span class=\"chip\">条件: ").Append(ExcelSheetHtmlRenderer.Escape(condition)).Append("</span>");
        }

        sb.Append("</div>");

        AppendDetails(sb, context, dataRow + 1, blockEnd, searchText);

        sb.Append("</div>");
    }

    private static void AppendDetails(StringBuilder sb, SheetGridContext context, int startRow, int endRow, StringBuilder searchText)
    {
        var sheet = context.Sheet;
        var minCol = context.MinCol;
        var maxCol = context.MaxCol;

        var details = new List<DiagramDetail>();
        DiagramDetail? current = null;
        var mode = DiagramMode.None;

        var row = startRow;
        while (row <= endRow)
        {
            var first = SemanticSheetHelpers.FirstNonEmptyCell(sheet, row, minCol, maxCol);
            if (first is null)
            {
                row++;
                continue;
            }

            var (labelCol, label) = first.Value;

            if (label == "遷移処理")
            {
                current = new DiagramDetail();
                details.Add(current);
                mode = DiagramMode.Transition;
                row++;
                continue;
            }

            if (label == "復帰処理")
            {
                current ??= NewDetail(details);
                mode = DiagramMode.Return;
                row++;
                continue;
            }

            if (label is "遷移先画面ＩＤ" or "遷移先画面名" or "遷移条件" or "遷移後のアクション")
            {
                current ??= NewDetail(details);
                var value = ValueAfter(sheet, row, labelCol, maxCol);
                switch (label)
                {
                    case "遷移先画面ＩＤ": current.ScreenId = value; break;
                    case "遷移先画面名": current.ScreenName = value; break;
                    case "遷移条件": current.Condition = value; break;
                    case "遷移後のアクション": current.Action = value; break;
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    searchText.Append(value).Append(' ');
                }

                row++;
                continue;
            }

            if (label is "引継項目・種別" or "引継項目")
            {
                current ??= NewDetail(details);
                var headerCells = SemanticSheetHelpers.NonEmptyCells(sheet, row, labelCol + 1, maxCol).Take(2).ToList();
                row++;
                if (headerCells.Count < 2)
                {
                    continue;
                }

                var col1 = headerCells[0].Col;
                var col2 = headerCells[1].Col;
                while (row <= endRow)
                {
                    var itemFirst = SemanticSheetHelpers.FirstNonEmptyCell(sheet, row, minCol, maxCol);
                    if (itemFirst is { } itemCell && DetailLabels.Contains(itemCell.Text))
                    {
                        break;
                    }

                    var source = sheet.Cells[row, col1].Text ?? string.Empty;
                    var destination = sheet.Cells[row, col2].Text ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(source) || !string.IsNullOrWhiteSpace(destination))
                    {
                        searchText.Append(source).Append(' ').Append(destination).Append(' ');
                        var items = mode == DiagramMode.Return ? current.ReturnItems : current.TransitionItems;
                        items.Add((source, destination));
                    }

                    row++;
                }

                continue;
            }

            row++;
        }

        if (details.Count == 0)
        {
            // No detail markers recognized in this block - fall back to the grid so content isn't lost.
            sb.Append(ExcelSheetHtmlRenderer.RenderGridRange(context, startRow, endRow, searchText));
            return;
        }

        foreach (var detail in details)
        {
            AppendDetail(sb, detail);
        }
    }

    private static DiagramDetail NewDetail(List<DiagramDetail> details)
    {
        var detail = new DiagramDetail();
        details.Add(detail);
        return detail;
    }

    private static string ValueAfter(ExcelWorksheet sheet, int row, int labelCol, int maxCol)
    {
        var cell = SemanticSheetHelpers.NonEmptyCells(sheet, row, labelCol + 1, maxCol).FirstOrDefault();
        return cell.Text ?? string.Empty;
    }

    private static void AppendDetail(StringBuilder sb, DiagramDetail detail)
    {
        sb.Append("<div class=\"diag-detail\"><strong>遷移処理</strong>");
        sb.Append("<table class=\"ov-kv\"><tbody>");
        AppendKvRow(sb, "遷移先画面ＩＤ", detail.ScreenId);
        AppendKvRow(sb, "遷移先画面名", detail.ScreenName);
        AppendKvRow(sb, "遷移条件", detail.Condition);
        sb.Append("</tbody></table>");

        AppendItemsTable(sb, detail.TransitionItems);

        sb.Append("<table class=\"ov-kv\"><tbody>");
        AppendKvRow(sb, "遷移後のアクション", detail.Action);
        sb.Append("</tbody></table>");

        if (detail.ReturnItems.Count > 0)
        {
            sb.Append("<strong>復帰処理</strong>");
            AppendItemsTable(sb, detail.ReturnItems);
        }

        sb.Append("</div>");
    }

    private static void AppendKvRow(StringBuilder sb, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sb.Append("<tr><th>").Append(ExcelSheetHtmlRenderer.Escape(label)).Append("</th><td>")
          .Append(ExcelSheetHtmlRenderer.EscapeMultiline(value)).Append("</td></tr>");
    }

    private static void AppendItemsTable(StringBuilder sb, List<(string Source, string Destination)> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.Append("<div class=\"table-scroll\"><table class=\"ov-table\"><thead><tr><th>遷移元画面項目</th><th>遷移先画面項目</th></tr></thead><tbody>");
        foreach (var (source, destination) in items)
        {
            sb.Append("<tr><td>").Append(ExcelSheetHtmlRenderer.EscapeMultiline(source)).Append("</td><td>")
              .Append(ExcelSheetHtmlRenderer.EscapeMultiline(destination)).Append("</td></tr>");
        }

        sb.Append("</tbody></table></div>");
    }
}
