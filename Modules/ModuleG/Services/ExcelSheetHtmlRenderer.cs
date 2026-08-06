using System.Text;
using OfficeOpenXml;
using OfficeOpenXml.Drawing;
using OfficeOpenXml.Style;

namespace ModuleG.Services;

/// <summary>
/// Worksheet-to-HTML renderer. 3 sheets get a dedicated semantic parser instead of the raw grid:
/// 概要/overview and 画面遷移/screen transition (<see cref="OverviewSheetParser"/>/
/// <see cref="ScreenDiagramSheetParser"/>) recognize the 【...】-labelled sections these sheets are
/// authored with and render proper labelled tables/paragraphs, matching how the reference web app
/// (mcf7-web-develop) presents them; 項目説明/field description (<see cref="ItemExplanationSheetParser"/>)
/// - the densest sheet, up to 358 merged cells - is instead rendered as a compact table (required
/// marker/field name/description + the sheet's own flag columns) rather than a faithful grid rebuild.
/// Every other
/// sheet - and any of these 3 whose layout doesn't match the expected structure - falls back to the
/// original generic grid renderer (<see cref="RenderGridRange"/>): rebuild the Excel grid faithfully
/// (merged cells, background fill, bold, alignment) without parsing meaning - embedded pictures
/// (small legend icons, decorative shapes) are not rendered on this path, since they're anchored by
/// row/col only and don't map cleanly onto arbitrary merged-cell layouts.
/// This fallback is what makes the approach safe across many files with unpredictable layout (see
/// PLAN.md) while still giving the highest-value sheets a much more readable rendering.
/// </summary>
public static class ExcelSheetHtmlRenderer
{
    /// <summary>Renders one worksheet. Every non-empty cell's text is appended to
    /// <paramref name="searchText"/> for the content-search index. Embedded pictures are written as
    /// files under <paramref name="imagesOutputDir"/> and referenced by relative path
    /// <paramref name="imagesRelativeUrl"/> (so the generated HTML can sit anywhere alongside its
    /// Images\{ScreenCode}\ folder).</summary>
    public static string RenderSheet(
        ExcelWorksheet sheet,
        string imagesOutputDir,
        string imagesRelativeUrl,
        StringBuilder searchText,
        Action<string> log,
        string? screenImageHtml = null)
    {
        var dimension = sheet.Dimension;
        if (dimension is null)
        {
            return string.Empty;
        }

        // Prefer the sheet's defined print area over the raw used-range: these workbooks are
        // authored for printing (docProps show a real Print_Area per sheet) and the used range
        // routinely extends far past the meaningful content with empty formatted filler
        // cells/columns, which bloats the HTML and looks like a wall of empty graph-paper cells.
        var printArea = TryGetPrintArea(sheet);
        var minRow = printArea?.Start.Row ?? dimension.Start.Row;
        var maxRow = printArea?.End.Row ?? dimension.End.Row;
        var minCol = printArea?.Start.Column ?? dimension.Start.Column;
        var maxCol = printArea?.End.Column ?? dimension.End.Column;

        // The print area itself is usually still wider/taller than the actual content (authors
        // leave slack for future rows, or the block just doesn't reach the printable page edge) -
        // trim the trailing blank rows/columns so the page isn't mostly empty "graph paper".
        (maxRow, maxCol) = TrimTrailingEmpty(sheet, minRow, maxRow, minCol, maxCol);

        // Every sheet repeats the same 3-row "mcframe 7 / 文書名 / ... / 文書番号 / Version / Rev."
        // metadata block (plus a 4th row just repeating the sheet's own name as a title) - already
        // shown in the page's own header chips, so skip past it here regardless of which sheet this is
        // or whether it ends up on the semantic or generic-grid path below.
        minRow = SkipDocumentHeaderBlock(sheet, minRow, maxRow, minCol, maxCol, sheet.Name);

        var context = new SheetGridContext(sheet, minCol, maxCol, imagesOutputDir, imagesRelativeUrl);

        var semanticHtml = sheet.Name switch
        {
            "概要" => OverviewSheetParser.TryRender(context, minRow, maxRow, searchText, log, screenImageHtml, out var overviewHtml) ? overviewHtml : null,
            "画面遷移" => ScreenDiagramSheetParser.TryRender(context, minRow, maxRow, searchText, out var diagramHtml) ? diagramHtml : null,
            "項目説明" => ItemExplanationSheetParser.TryRender(context, minRow, maxRow, searchText, out var itemHtml) ? itemHtml : null,
            _ => null,
        };

        var html = new StringBuilder(semanticHtml ?? RenderGridRange(context, minRow, maxRow, searchText));

        // OverviewSheetParser only splices screenImageHtml into its own 【説明】 section - if 概要
        // didn't parse semantically at all (fell back to the grid above), that splice never happened
        // and the screenshot would otherwise vanish silently instead of just landing in a less ideal
        // spot.
        if (semanticHtml is null && !string.IsNullOrEmpty(screenImageHtml))
        {
            html.Append("<div class=\"ov-section\"><h3>画面イメージ</h3>").Append(screenImageHtml).Append("</div>");
        }

        return html.ToString();
    }

    /// <summary>Renders a row range of a sheet as a plain HTML grid (merge cells/background/bold/
    /// alignment/rotation/embedded images), with no attempt to interpret meaning. Used both as the
    /// whole-sheet fallback (sheet not recognized / doesn't have a semantic parser) and, from
    /// <see cref="OverviewSheetParser"/>/<see cref="ScreenDiagramSheetParser"/>, to render the leading
    /// header block and any 【...】 section whose content doesn't match the expected sub-layout - so a
    /// surprising file degrades gracefully to "grid, but still correct" instead of losing content.</summary>
    internal static string RenderGridRange(SheetGridContext context, int minRow, int maxRow, StringBuilder? searchText = null)
    {
        var sheet = context.Sheet;
        var minCol = context.MinCol;
        var maxCol = context.MaxCol;

        var html = new StringBuilder();
        html.Append("<div class=\"table-scroll\"><table class=\"sheet-grid\">");
        AppendColGroup(html, sheet, minCol, maxCol);

        for (var row = minRow; row <= maxRow; row++)
        {
            html.Append("<tr>");
            for (var col = minCol; col <= maxCol; col++)
            {
                if (context.Covered.Contains((row, col)))
                {
                    continue;
                }

                var cell = sheet.Cells[row, col];
                var text = cell.Text ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    searchText?.Append(text).Append(' ');
                }

                context.SpanByTopLeft.TryGetValue((row, col), out var span);
                var rowSpan = span.RowSpan > 1 ? $" rowspan=\"{span.RowSpan}\"" : string.Empty;
                var colSpan = span.ColSpan > 1 ? $" colspan=\"{span.ColSpan}\"" : string.Empty;
                var style = BuildCellStyle(cell, text);

                html.Append("<td").Append(rowSpan).Append(colSpan).Append(style).Append('>');
                html.Append(RenderCellContent(cell, text));
                html.Append("</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</table></div>");

        return html.ToString();
    }

    /// <summary>Scans backward from the print-area edges to find the last row/column that actually
    /// has text or a background fill, and clamps down to it. Cheap heuristic (ignores whether a
    /// merge's origin is further up/left) - worst case a large merged block's tail gets visually
    /// clipped by a row or two, which degrades gracefully rather than breaking layout.</summary>
    private static (int MaxRow, int MaxCol) TrimTrailingEmpty(ExcelWorksheet sheet, int minRow, int maxRow, int minCol, int maxCol)
    {
        var lastNonEmptyRow = minRow - 1;
        var lastNonEmptyCol = minCol - 1;

        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                if (!IsCellVisuallyEmpty(sheet.Cells[row, col]))
                {
                    lastNonEmptyRow = Math.Max(lastNonEmptyRow, row);
                    lastNonEmptyCol = Math.Max(lastNonEmptyCol, col);
                }
            }
        }

        var trimmedMaxRow = lastNonEmptyRow >= minRow ? lastNonEmptyRow : maxRow;
        var trimmedMaxCol = lastNonEmptyCol >= minCol ? lastNonEmptyCol : maxCol;
        return (trimmedMaxRow, trimmedMaxCol);
    }

    /// <summary>Detects and skips the repeated doc-metadata header block every sheet starts with: a
    /// 3-row table anchored by a cell reading exactly "mcframe 7", followed by a blank separator row
    /// and then a 1-line title row repeating the sheet's own name (e.g. "画面イメージ"). Only skips when
    /// that exact anchor is found - a sheet that doesn't start this way (or a layout that shifts
    /// unexpectedly) is left untouched rather than risking cutting off real content.</summary>
    private static int SkipDocumentHeaderBlock(ExcelWorksheet sheet, int minRow, int maxRow, int minCol, int maxCol, string sheetName)
    {
        var anchor = SemanticSheetHelpers.FirstNonEmptyCell(sheet, minRow, minCol, Math.Min(maxCol, minCol + 1));
        if (anchor is not { Text: "mcframe 7" })
        {
            return minRow;
        }

        var row = Math.Min(maxRow + 1, minRow + 3);
        while (row <= maxRow && !SemanticSheetHelpers.NonEmptyCells(sheet, row, minCol, maxCol).Any())
        {
            row++;
        }

        if (row <= maxRow)
        {
            var titleRowCells = SemanticSheetHelpers.NonEmptyCells(sheet, row, minCol, maxCol).ToList();
            if (titleRowCells.Count == 1 && titleRowCells[0].Text == sheetName)
            {
                row++;
                while (row <= maxRow && !SemanticSheetHelpers.NonEmptyCells(sheet, row, minCol, maxCol).Any())
                {
                    row++;
                }
            }
        }

        return Math.Min(row, maxRow + 1);
    }

    private static bool IsCellVisuallyEmpty(ExcelRange cell) =>
        string.IsNullOrWhiteSpace(cell.Text) && cell.Style.Fill.PatternType == ExcelFillStyle.None;

    private static ExcelAddress? TryGetPrintArea(ExcelWorksheet sheet)
    {
        try
        {
            var printArea = sheet.PrinterSettings.PrintArea;
            return printArea is null ? null : new ExcelAddress(printArea.Start.Row, printArea.Start.Column, printArea.End.Row, printArea.End.Column);
        }
        catch
        {
            // Best-effort only - a handful of workbooks may have a malformed/missing print area.
            return null;
        }
    }

    private static void AppendColGroup(StringBuilder html, ExcelWorksheet sheet, int minCol, int maxCol)
    {
        html.Append("<colgroup>");
        for (var col = minCol; col <= maxCol; col++)
        {
            var width = sheet.Column(col).Width;
            var px = width > 0 ? (int)Math.Round(width * 7) : 60;
            html.Append("<col style=\"width:").Append(px).Append("px\">");
        }

        html.Append("</colgroup>");
    }

    /// <summary>Excel's "rotate this cell's text" setting doesn't map to a <td> style directly
    /// (rotating the cell itself wrecks table layout) - the rotation instead wraps just the text in
    /// an inline-block span. <c>TextRotation == 255</c> is Excel's special "stacked vertical text"
    /// value distinct from an actual angle.</summary>
    private static string RenderCellContent(ExcelRange cell, string text)
    {
        var escaped = EscapeMultiline(text);
        var rotation = cell.Style.TextRotation;
        if (rotation == 0)
        {
            return escaped;
        }

        if (rotation == 255)
        {
            return $"<span style=\"writing-mode:vertical-rl;text-orientation:upright;\">{escaped}</span>";
        }

        // Excel encodes 1-90 as counter-clockwise degrees and 91-180 as (value-90) clockwise degrees.
        var degrees = rotation is >= 1 and <= 90 ? -rotation : rotation - 90;
        return $"<span style=\"display:inline-block;white-space:nowrap;transform:rotate({degrees}deg);transform-origin:left center;\">{escaped}</span>";
    }

    private static string BuildCellStyle(ExcelRange cell, string text)
    {
        var declarations = new List<string>();

        var hasFill = cell.Style.Fill.PatternType != ExcelFillStyle.None;
        var backgroundColor = TryGetRgbHex(cell.Style.Fill.BackgroundColor);
        if (hasFill && backgroundColor is not null)
        {
            declarations.Add($"background-color:#{backgroundColor}");
        }

        // A blank, unfilled cell is very likely just print-area padding, not meaningful table
        // structure - hiding its border is what turns a "wall of empty graph paper" back into a
        // readable document with borders only around cells that actually carry content.
        if (!hasFill && string.IsNullOrWhiteSpace(text))
        {
            declarations.Add("border-color:transparent");
        }

        if (cell.Style.Font.Bold)
        {
            declarations.Add("font-weight:600");
        }

        var fontColor = TryGetRgbHex(cell.Style.Font.Color);
        if (fontColor is not null)
        {
            declarations.Add($"color:#{fontColor}");
        }

        declarations.Add(cell.Style.HorizontalAlignment switch
        {
            ExcelHorizontalAlignment.Center or ExcelHorizontalAlignment.CenterContinuous => "text-align:center",
            ExcelHorizontalAlignment.Right => "text-align:right",
            _ => "text-align:left",
        });

        return declarations.Count > 0 ? $" style=\"{string.Join(';', declarations)}\"" : string.Empty;
    }

    private static string? TryGetRgbHex(ExcelColor color)
    {
        var rgb = color.Rgb;
        if (string.IsNullOrEmpty(rgb) || rgb.Length < 6)
        {
            return null;
        }

        return rgb[^6..];
    }

    internal static (Dictionary<(int Row, int Col), (int RowSpan, int ColSpan)> SpanByTopLeft, HashSet<(int Row, int Col)> Covered, Dictionary<(int Row, int Col), (int Row, int Col)> CoveredToTopLeft) IndexMergedCells(ExcelWorksheet sheet)
    {
        var spanByTopLeft = new Dictionary<(int, int), (int, int)>();
        var covered = new HashSet<(int, int)>();
        var coveredToTopLeft = new Dictionary<(int, int), (int, int)>();

        foreach (var address in sheet.MergedCells)
        {
            var range = new ExcelAddress(address);
            var rowSpan = range.End.Row - range.Start.Row + 1;
            var colSpan = range.End.Column - range.Start.Column + 1;
            if (rowSpan <= 1 && colSpan <= 1)
            {
                continue;
            }

            var topLeft = (range.Start.Row, range.Start.Column);
            spanByTopLeft[topLeft] = (rowSpan, colSpan);
            for (var r = range.Start.Row; r <= range.End.Row; r++)
            {
                for (var c = range.Start.Column; c <= range.End.Column; c++)
                {
                    if (r == range.Start.Row && c == range.Start.Column)
                    {
                        continue;
                    }

                    covered.Add((r, c));
                    coveredToTopLeft[(r, c)] = topLeft;
                }
            }
        }

        return (spanByTopLeft, covered, coveredToTopLeft);
    }

    /// <summary>Writes every embedded picture to disk and groups the resulting relative URLs by their
    /// anchor cell (1-based row/col, matching cell coordinates) so the caller can place each icon in
    /// the actual &lt;td&gt; it was anchored to in Excel - not a pixel-accurate overlay (see type doc
    /// comment).</summary>
    internal static Dictionary<(int Row, int Col), List<string>> ExtractImages(ExcelWorksheet sheet, string imagesOutputDir, string imagesRelativeUrl)
    {
        var result = new Dictionary<(int, int), List<string>>();
        var index = 0;

        foreach (var drawing in sheet.Drawings)
        {
            if (drawing is not ExcelPicture { Image: not null } picture)
            {
                continue;
            }

            index++;
            var format = picture.ImageFormat;
            var extension = format.Equals(System.Drawing.Imaging.ImageFormat.Jpeg) ? "jpg"
                : format.Equals(System.Drawing.Imaging.ImageFormat.Gif) ? "gif"
                : format.Equals(System.Drawing.Imaging.ImageFormat.Bmp) ? "bmp"
                : "png";
            var fileName = $"{SanitizeFileNamePart(sheet.Name)}_{index}.{extension}";

            Directory.CreateDirectory(imagesOutputDir);
            using (var stream = new FileStream(Path.Combine(imagesOutputDir, fileName), FileMode.Create))
            {
                picture.Image.Save(stream, format);
            }

            var anchor = (Row: picture.From.Row + 1, Col: picture.From.Column + 1);
            if (!result.TryGetValue(anchor, out var list))
            {
                list = new List<string>();
                result[anchor] = list;
            }

            list.Add($"{imagesRelativeUrl}/{fileName}");
        }

        return result;
    }

    internal static string SanitizeFileNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    internal static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    internal static string EscapeMultiline(string value) =>
        Escape(value).Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");
}
