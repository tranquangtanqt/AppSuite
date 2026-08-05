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

    public static bool TryRender(SheetGridContext context, int minRow, int maxRow, StringBuilder searchText, out string html)
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
                    // doesn't handle either - it's diagram/image content, not a data list) - grid
                    // fallback keeps whatever is there (including any embedded diagram image) visible.
                    sb.Append(ExcelSheetHtmlRenderer.RenderGridRange(context, contentStartRow, sectionEndRow, searchText));
                    break;
            }

            sb.Append("</div>");
        }

        sb.Append("</div>");
        html = sb.ToString();
        return true;
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
