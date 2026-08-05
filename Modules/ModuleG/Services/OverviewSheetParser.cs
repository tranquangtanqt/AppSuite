using System.Text;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace ModuleG.Services;

/// <summary>
/// Semantic renderer for the "概要" (overview) sheet, matching the section layout the reference web
/// app (mcf7-web-develop/front-end .../document/screen/screen-overview) understands: a fixed set of
/// 【...】-labelled sections (説明/目的/オペレーション一覧/SQLID一覧/ソート順/データアクセスコントロール/
/// 自由記述), each either a short list-style table or a free-text block. Unlike the reference app -
/// which reads a fixed tab-column offset per field, baked in for its own Excel→text export format -
/// this looks up each table's columns by matching the actual header cell text (e.g. "処理名") in the
/// row below the section marker, since we read the workbook directly via EPPlus and column positions
/// aren't guaranteed identical across screens. Any section whose header row can't be found, and any
/// content sitting outside a recognized 【...】 marker, falls back to <see cref="ExcelSheetHtmlRenderer.RenderGridRange"/>
/// for just that row range - so an overview sheet with a slightly different layout still renders
/// correctly (just less prettily) instead of silently dropping content.
/// </summary>
internal static class OverviewSheetParser
{
    private static readonly HashSet<string> KnownMarkers = new()
    {
        "【説明】", "【目的】", "【オペレーション一覧】", "【SQLID一覧】", "【ソート順】", "【データアクセスコントロール】", "【自由記述】",
    };

    public static bool TryRender(
        SheetGridContext context, int minRow, int maxRow, StringBuilder searchText,
        ExcelDiagramCapture? diagramCapture, string sourceFilePath, Action<string> log, out string html)
    {
        var sheet = context.Sheet;
        var minCol = context.MinCol;
        var maxCol = context.MaxCol;

        // Section markers are only ever authored near the left edge of the sheet (column B in every
        // sample seen). Inline sub-labels that use the same 【...】 bracket convention deep inside a
        // detail table (e.g. 【利用シーン】/【補足】 inside a 目的 row's far-right "usage" column) must
        // NOT be picked up here, or they'd be misread as new top-level sections and chop a real table
        // in half - restrict the scan to a narrow zone near minCol instead of the full row width.
        var markerColMax = Math.Min(maxCol, minCol + 3);
        var markers = new List<(int Row, string Text)>();
        for (var row = minRow; row <= maxRow; row++)
        {
            var first = SemanticSheetHelpers.FirstNonEmptyCell(sheet, row, minCol, markerColMax);
            if (first is { } cell && cell.Text.StartsWith('【') && cell.Text.EndsWith('】'))
            {
                markers.Add((row, cell.Text));
            }
        }

        // No recognizable section at all - this isn't laid out like a standard 概要 sheet (or EPPlus
        // couldn't find one for some other reason); let the caller fall back to the generic grid for
        // the whole sheet rather than rendering a mostly-empty "semantic" page.
        if (markers.Count == 0 || !markers.Exists(m => KnownMarkers.Contains(m.Text)))
        {
            html = string.Empty;
            return false;
        }

        // Rows before the first marker are just the repeated mcframe/module/document-number header
        // block, already shown in the page's own header chips - skip rendering it here entirely.
        var sb = new StringBuilder();
        sb.Append("<div class=\"semantic-doc\">");

        for (var i = 0; i < markers.Count; i++)
        {
            var (markerRow, markerText) = markers[i];
            var sectionEndRow = i + 1 < markers.Count ? markers[i + 1].Row - 1 : maxRow;
            var contentStartRow = markerRow + 1;

            sb.Append("<div class=\"ov-section\"><h3>").Append(ExcelSheetHtmlRenderer.Escape(markerText)).Append("</h3>");

            if (contentStartRow > sectionEndRow)
            {
                sb.Append("</div>");
                continue;
            }

            switch (markerText)
            {
                case "【説明】":
                case "【自由記述】":
                    SemanticSheetHelpers.AppendFreeTextParagraphs(sb, sheet, contentStartRow, sectionEndRow, minCol, maxCol, searchText);
                    SemanticSheetHelpers.AppendImagesInRange(sb, context, contentStartRow, sectionEndRow);
                    break;

                case "【目的】":
                    AppendTableOrFallback(sb, context, contentStartRow, sectionEndRow, searchText,
                        new[] { "目的（レベル１）", "目的（レベル２）", "利用シーン/補足", "操作種別/検索モード" });
                    break;

                case "【オペレーション一覧】":
                    AppendTableOrFallback(sb, context, contentStartRow, sectionEndRow, searchText,
                        new[] { "ID", "処理名", "処理概要" });
                    break;

                case "【SQLID一覧】":
                    AppendTableOrFallback(sb, context, contentStartRow, sectionEndRow, searchText,
                        new[] { "SQLID", "使用オペレーション", "目的" });
                    break;

                case "【ソート順】":
                    AppendTableOrFallback(sb, context, contentStartRow, sectionEndRow, searchText,
                        new[] { "オペレーションID", "一覧名", "ソート順", "条件" });
                    break;

                case "【データアクセスコントロール】":
                    AppendTableOrFallback(sb, context, contentStartRow, sectionEndRow, searchText,
                        new[] { "ボタン名", "データアクセスコントロール対象項目", "条件" });
                    break;

                default:
                    // Unrecognized marker (e.g. 【処理関連図/サービス関連図】, which the reference app
                    // doesn't handle either - it's diagram/image content, not a data list). Markers
                    // whose text names an actual diagram ("図") get rasterized via Excel COM Interop
                    // (see ExcelDiagramCapture) instead of the grid fallback, since the box/arrow
                    // flowchart there is drawn with floating shapes/connectors that EPPlus can't read
                    // and the cell grid alone would show as an empty or meaningless table. Any other
                    // unrecognized marker, or a diagram marker when Excel Interop capture fails/isn't
                    // available on this machine, still falls back to the grid so content is never lost.
                    if (!markerText.Contains('図') ||
                        !TryAppendDiagramImage(sb, context, diagramCapture, sourceFilePath, markerText, markerRow, contentStartRow, sectionEndRow, log))
                    {
                        sb.Append(ExcelSheetHtmlRenderer.RenderGridRange(context, contentStartRow, sectionEndRow, searchText));
                    }

                    break;
            }

            sb.Append("</div>");
        }

        sb.Append("</div>");
        html = sb.ToString();
        return true;
    }

    /// <summary>Best-effort: exports <paramref name="contentStartRow"/>..<paramref name="sectionEndRow"/>
    /// (using <see cref="SheetGridContext.UntrimmedMinCol"/>/<see cref="SheetGridContext.UntrimmedMaxCol"/>,
    /// since the diagram's shapes routinely sit over columns with no cell text/fill of their own and
    /// the trimmed grid bounds would clip it) as a PNG via Excel Interop and appends an &lt;img&gt;.
    /// Returns false - appending nothing - on any failure so the caller falls back to the grid.</summary>
    private static bool TryAppendDiagramImage(
        StringBuilder sb, SheetGridContext context, ExcelDiagramCapture? diagramCapture, string sourceFilePath,
        string markerText, int markerRow, int contentStartRow, int sectionEndRow, Action<string> log)
    {
        if (diagramCapture is null)
        {
            return false;
        }

        var sheet = context.Sheet;
        var maxCol = FindLocalMaxCol(sheet, contentStartRow, sectionEndRow, context.UntrimmedMinCol, context.UntrimmedMaxCol);
        var rangeAddress = sheet.Cells[contentStartRow, context.UntrimmedMinCol, sectionEndRow, maxCol].Address;
        var imageFileName = $"{ExcelSheetHtmlRenderer.SanitizeFileNamePart(sheet.Name)}_diagram_{markerRow}.png";
        var outputPath = Path.Combine(context.ImagesOutputDir, imageFileName);

        if (!diagramCapture.TryCaptureRange(sourceFilePath, sheet.Name, rangeAddress, outputPath, out var error))
        {
            log($"[{Path.GetFileName(sourceFilePath)}] Chup anh so do '{markerText}' ({sheet.Name}!{rangeAddress}) that bai: {error}");
            return false;
        }

        sb.Append("<div class=\"sheet-image\"><img src=\"")
            .Append(ExcelSheetHtmlRenderer.Escape($"{context.ImagesRelativeUrl}/{imageFileName}"))
            .Append("\" alt=\"").Append(ExcelSheetHtmlRenderer.Escape(markerText)).Append("\" loading=\"lazy\"></div>");
        return true;
    }

    /// <summary>Scans just this section's own rows (not the whole sheet, unlike
    /// <see cref="SheetGridContext.UntrimmedMaxCol"/>) for the rightmost column carrying a box border,
    /// fill or text, +2 columns of slack for a connector-arrow shape poking slightly past the last box.
    /// Without this, a diagram spanning ~15 columns can end up captured 5x too wide because some
    /// unrelated section elsewhere on the same sheet happens to use far-right columns.</summary>
    private static int FindLocalMaxCol(ExcelWorksheet sheet, int minRow, int maxRow, int minCol, int maxColLimit)
    {
        var lastNonEmptyCol = minCol - 1;
        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxColLimit; col++)
            {
                var cell = sheet.Cells[row, col];
                var hasContent = !string.IsNullOrWhiteSpace(cell.Text)
                    || cell.Style.Fill.PatternType != ExcelFillStyle.None
                    || cell.Style.Border.Top.Style != ExcelBorderStyle.None
                    || cell.Style.Border.Bottom.Style != ExcelBorderStyle.None
                    || cell.Style.Border.Left.Style != ExcelBorderStyle.None
                    || cell.Style.Border.Right.Style != ExcelBorderStyle.None;

                if (hasContent)
                {
                    lastNonEmptyCol = Math.Max(lastNonEmptyCol, col);
                }
            }
        }

        return lastNonEmptyCol >= minCol ? Math.Min(lastNonEmptyCol + 2, maxColLimit) : maxColLimit;
    }

    private static void AppendTableOrFallback(
        StringBuilder sb, SheetGridContext context, int contentStartRow, int sectionEndRow, StringBuilder searchText, string[] headers)
    {
        var header = SemanticSheetHelpers.TryFindHeaderRow(context.Sheet, contentStartRow, sectionEndRow, context.MinCol, context.MaxCol, headers);
        if (header is { } found)
        {
            SemanticSheetHelpers.AppendLabeledTable(sb, context.Sheet, headers, found.Columns, found.HeaderRow + 1, sectionEndRow, searchText);
            SemanticSheetHelpers.AppendImagesInRange(sb, context, contentStartRow, sectionEndRow);
        }
        else
        {
            sb.Append(ExcelSheetHtmlRenderer.RenderGridRange(context, contentStartRow, sectionEndRow, searchText));
        }
    }
}
