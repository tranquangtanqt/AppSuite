using System.Text;
using OfficeOpenXml;

namespace Mcf.CrudDiagram.HtmlGenerator.Services;

/// <summary>Renders a parsed <see cref="CrudSheetModel"/> into the page body HTML for 1 logic sheet -
/// 1 continuous `&lt;table&gt;`, 1 `&lt;tr&gt;` per source row (blank spacer rows between blocks are
/// skipped), matching the row layout of the original Excel sheet rather than splitting each block
/// into its own separate table/card.</summary>
internal static class CrudHtmlRenderer
{
    /// <summary><paramref name="screenCodeIndex"/> maps every known sheet's own code (e.g.
    /// "MSBBL6020") to its .html file name - used to turn a 使用オブジェクト cell shaped like
    /// "MSBBL6020.Slo_Chk03" (a reference to another logic's specific ID block, not a table) into a
    /// working link to that block, instead of just plain text. A cell only becomes a link when the
    /// part before the first '.' matches a *known* sheet - anything else (a plain table name with no
    /// dot, or a dotted name whose prefix isn't one of our sheets, e.g. a call into code outside this
    /// document set) is left as plain text rather than guessing.</summary>
    public static string Render(CrudSheetModel model, ExcelWorksheet sheet, IReadOnlyDictionary<string, string> screenCodeIndex)
    {
        if (!model.DataHeaderFound)
        {
            // Unexpected layout (the "使用オブジェクト" header couldn't be found anywhere) - dump the
            // whole sheet as a plain grid instead of silently showing an empty page.
            return RenderRawGridFallback(sheet);
        }

        if (model.Rows.Count == 0)
        {
            return "<p class=\"crud-empty\">Khong tim thay dong du lieu nao trong sheet nay.</p>";
        }

        var sb = new StringBuilder();
        sb.Append("<div class=\"table-scroll\"><table class=\"crud-table\"><thead><tr>")
            .Append("<th>ID</th><th>名称</th><th>使用オブジェクト</th><th>C</th><th>R</th><th>U</th><th>D</th><th>種</th><th>備考</th>")
            .Append("</tr></thead><tbody>");

        foreach (var row in model.Rows)
        {
            if (IsEmptyRow(row))
            {
                continue;
            }

            var isBlockHeader = !string.IsNullOrWhiteSpace(row.Id);
            var anchor = isBlockHeader ? $" id=\"blk-{HtmlTemplates.Escape(row.Id)}\"" : string.Empty;
            sb.Append(isBlockHeader ? $"<tr class=\"crud-block-row\"{anchor}>" : "<tr>")
                .Append("<td>").Append(HtmlTemplates.Escape(row.Id)).Append("</td>")
                .Append("<td>").Append(HtmlTemplates.Escape(row.Name)).Append("</td>")
                .Append("<td>").Append(RenderUsedObjectCell(row.UsedObject, screenCodeIndex)).Append("</td>")
                .Append("<td class=\"flag\">").Append(HtmlTemplates.Escape(row.Create)).Append("</td>")
                .Append("<td class=\"flag\">").Append(HtmlTemplates.Escape(row.Read)).Append("</td>")
                .Append("<td class=\"flag\">").Append(HtmlTemplates.Escape(row.Update)).Append("</td>")
                .Append("<td class=\"flag\">").Append(HtmlTemplates.Escape(row.Delete)).Append("</td>")
                .Append("<td>").Append(HtmlTemplates.Escape(row.Kind)).Append("</td>")
                .Append("<td>").Append(HtmlTemplates.Escape(row.Remark)).Append("</td>")
                .Append("</tr>");
        }

        sb.Append("</tbody></table></div>");
        return sb.ToString();
    }

    /// <summary>A row with every cell blank is a spacer row from the source sheet's layout - skip it
    /// so the rendered table doesn't carry empty gaps.</summary>
    private static bool IsEmptyRow(CrudTableRow row) =>
        string.IsNullOrWhiteSpace(row.Id) &&
        string.IsNullOrWhiteSpace(row.Name) &&
        string.IsNullOrWhiteSpace(row.UsedObject) &&
        string.IsNullOrWhiteSpace(row.Create) &&
        string.IsNullOrWhiteSpace(row.Read) &&
        string.IsNullOrWhiteSpace(row.Update) &&
        string.IsNullOrWhiteSpace(row.Delete) &&
        string.IsNullOrWhiteSpace(row.Kind) &&
        string.IsNullOrWhiteSpace(row.Remark);

    /// <summary>"MSBBL6020.Slo_Chk03" -&gt; link to blk-Slo_Chk03 inside MSBBL6020's page, only when
    /// "MSBBL6020" is a sheet we actually imported; otherwise (no dot, or an unknown prefix - e.g. a
    /// call into code outside this document set) renders as plain escaped text.</summary>
    private static string RenderUsedObjectCell(string usedObject, IReadOnlyDictionary<string, string> screenCodeIndex)
    {
        var dotIndex = usedObject.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == usedObject.Length - 1)
        {
            return HtmlTemplates.Escape(usedObject);
        }

        var targetCode = usedObject[..dotIndex];
        var targetId = usedObject[(dotIndex + 1)..];
        if (!screenCodeIndex.TryGetValue(targetCode, out var targetHtmlFile))
        {
            return HtmlTemplates.Escape(usedObject);
        }

        var href = $"{targetHtmlFile}#blk-{Uri.EscapeDataString(targetId)}";
        return $"<a href=\"{HtmlTemplates.Escape(href)}\">{HtmlTemplates.Escape(usedObject)}</a>";
    }

    /// <summary>Best-effort plain grid dump (no merge/style awareness, unlike Mcf.Screen.HtmlGenerator's fuller
    /// generic renderer) - only reached when a sheet doesn't match the expected CRUD図 layout at all,
    /// which hasn't happened in any sample seen; keeps content visible instead of losing it.</summary>
    private static string RenderRawGridFallback(ExcelWorksheet sheet)
    {
        var dim = sheet.Dimension;
        if (dim is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append("<div class=\"table-scroll\"><table class=\"sheet-grid\">");
        for (var row = dim.Start.Row; row <= dim.End.Row; row++)
        {
            sb.Append("<tr>");
            for (var col = dim.Start.Column; col <= dim.End.Column; col++)
            {
                sb.Append("<td>").Append(HtmlTemplates.Escape(sheet.Cells[row, col].Text)).Append("</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</table></div>");
        return sb.ToString();
    }
}
