using System.Text;
using OfficeOpenXml;

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
        SheetGridContext context, int minRow, int maxRow, StringBuilder searchText, Action<string> log,
        string? screenImageHtml, out string html)
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
                    // whose text names an actual diagram ("図") get rasterized from the sheet's own
                    // floating shapes/connectors (see DiagramXmlReader/DiagramRenderer) instead of the
                    // grid fallback, since EPPlus's grid has no representation for those at all - they'd
                    // render as an empty or meaningless table. Any other unrecognized marker, or a
                    // diagram marker with no shapes actually in range, still falls back to the grid so
                    // content is never silently lost.
                    if (!markerText.Contains('図') ||
                        !TryAppendDiagramImage(sb, context, markerText, markerRow, contentStartRow, sectionEndRow, searchText, log))
                    {
                        sb.Append(ExcelSheetHtmlRenderer.RenderGridRange(context, contentStartRow, sectionEndRow, searchText));
                    }

                    break;
            }

            sb.Append("</div>");

            // 画面イメージ (the sheet's own screenshot) is spliced in right after 【説明】 closes - not
            // as its own top-level sheet section further down the page - so a reader sees what the
            // screen looks like immediately after reading what it does, before the more technical
            // sections (処理関連図, オペレーション一覧, ...). Pre-rendered by ScreenDocImporter, since
            // 画面イメージ is a separate sheet that may come before or after 概要 in the workbook.
            if (markerText == "【説明】" && !string.IsNullOrEmpty(screenImageHtml))
            {
                sb.Append("<div class=\"ov-section\"><h3>画面イメージ</h3>").Append(screenImageHtml).Append("</div>");
            }
        }

        sb.Append("</div>");
        html = sb.ToString();
        return true;
    }

    /// <summary>Best-effort: reads every box/connector shape anchored within
    /// <paramref name="contentStartRow"/>..<paramref name="sectionEndRow"/> straight out of the
    /// sheet's raw DrawingML XML (<see cref="DiagramXmlReader"/>), rasterizes them with
    /// <see cref="DiagramRenderer"/>, and appends an &lt;img&gt;. Returns false - appending nothing -
    /// on any failure (including "no shapes found") so the caller falls back to the grid.</summary>
    private static bool TryAppendDiagramImage(
        StringBuilder sb, SheetGridContext context, string markerText, int markerRow, int contentStartRow, int sectionEndRow,
        StringBuilder searchText, Action<string> log)
    {
        var sheet = context.Sheet;
        var drawings = sheet.Drawings;

        // DrawingML rows are 0-based (1 less than EPPlus's usual 1-based cell rows) - see
        // DiagramXmlReader's doc comment.
        var shapes = DiagramXmlReader.ReadShapesInRowRange(drawings.DrawingXml, drawings.NameSpaceManager, contentStartRow - 1, sectionEndRow - 1);

        var imageFileName = $"{ExcelSheetHtmlRenderer.SanitizeFileNamePart(sheet.Name)}_diagram_{markerRow}.png";
        var outputPath = Path.Combine(context.ImagesOutputDir, imageFileName);

        if (!DiagramRenderer.TryRender(shapes, outputPath, out var error, out var texts))
        {
            log($"[{sheet.Name}] Ve so do '{markerText}' (dong {contentStartRow}-{sectionEndRow}) that bai: {error}");
            return false;
        }

        sb.Append("<div class=\"sheet-image\"><img src=\"")
            .Append(ExcelSheetHtmlRenderer.Escape($"{context.ImagesRelativeUrl}/{imageFileName}"))
            .Append("\" alt=\"").Append(ExcelSheetHtmlRenderer.Escape(markerText)).Append("\" loading=\"lazy\"></div>");

        AppendDiagramTextTable(sb, texts, searchText);
        return true;
    }

    /// <summary>Lists every box's text right below its rasterized diagram image, in a plain table -
    /// the PNG itself has no selectable/searchable text, so without this a diagram's content is
    /// invisible to Ctrl+F and to <see cref="ExcelSheetHtmlRenderer"/>'s own search index.</summary>
    private static void AppendDiagramTextTable(StringBuilder sb, IReadOnlyList<string> texts, StringBuilder searchText)
    {
        if (texts.Count == 0)
        {
            return;
        }

        sb.Append("<div class=\"table-scroll\"><table class=\"ov-table diagram-text-table\"><thead><tr><th>図中のテキスト</th></tr></thead><tbody>");
        foreach (var text in texts)
        {
            searchText.Append(text).Append(' ');
            sb.Append("<tr><td>").Append(ExcelSheetHtmlRenderer.Escape(text)).Append("</td></tr>");
        }

        sb.Append("</tbody></table></div>");
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
