using System;
using System.Linq;
using System.Text;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Trang HTML kết quả <see cref="SchemaQualityChecker"/>: 1 file tự chứa (mở offline). Đầu trang: số mục Nên sửa / Cân nhắc /
/// Thông tin + bảng tóm tắt từng quy tắc (0 = ✓); bên dưới mỗi quy tắc có vi phạm 1 khối: vì sao đáng để ý, bảng các chỗ
/// vi phạm, câu SQL gợi ý (chỉ tham khảo). Tên bảng link sang trang từ điển dữ liệu (nếu đã xuất) ở #table=.
/// </summary>
public static class SchemaQualityReport
{
    /// <param name="dictionaryFileName">Tên file trang từ điển dữ liệu cùng thư mục (vd "orders.html") để link tên bảng;
    /// null = chưa xuất, tên bảng không link.</param>
    public static string Build(QualityReport report, DateTime readAt, DateTime generatedAt, string? dictionaryFileName)
    {
        var html = new StringBuilder();
        html.Append(Head(report));
        html.Append("<header><h1>Kiểm tra chất lượng schema</h1><div class=\"sub\"><span class=\"code\">").Append(E(report.Name))
            .Append("</span> · ").Append(report.TableCount.ToString("N0")).Append(" bảng, ").Append(report.ViewCount.ToString("N0"))
            .Append(" view · đọc lúc ").Append(readAt.ToString("yyyy-MM-dd HH:mm")).Append(" · xuất lúc ")
            .Append(generatedAt.ToString("yyyy-MM-dd HH:mm:ss")).Append("</div>");
        html.Append("<div class=\"chips\">");
        Chip(html, QualitySeverity.Error, report.Count(QualitySeverity.Error));
        Chip(html, QualitySeverity.Warning, report.Count(QualitySeverity.Warning));
        Chip(html, QualitySeverity.Info, report.Count(QualitySeverity.Info));
        html.Append("</div>");
        html.Append("<label class=\"toggle\"><input type=\"checkbox\" id=\"hideInfo\"> Ẩn mục Thông tin</label>");
        if (!report.IndexesChecked)
        {
            html.Append("<p class=\"warn\">File đọc bằng bản cũ chưa có index / thống kê: bỏ qua \"Khoá ngoại không có index\", ")
                .Append("\"Index trùng\" và \"Chưa có thống kê\" - bấm lại \"1. Đọc database\" rồi kiểm tra lại để có đủ.</p>");
        }
        html.Append("</header><main>");

        html.Append("<table class=\"summary\"><thead><tr><th>Mức</th><th>Quy tắc</th><th class=\"num\">Số chỗ</th></tr></thead><tbody>");
        foreach (var rule in SchemaQualityChecker.AllRules)
        {
            var count = report.Findings.Count(f => f.Rule.Id == rule.Id);
            var skipped = !report.IndexesChecked && rule.Id is "fk-index" or "dup-index" or "no-stats";
            html.Append("<tr class=\"").Append(Css(rule.Severity)).Append("\"><td>").Append(Badge(rule.Severity)).Append("</td><td>");
            if (count > 0)
            {
                html.Append("<a href=\"#r-").Append(rule.Id).Append("\">").Append(E(rule.Title)).Append("</a>");
            }
            else
            {
                html.Append(E(rule.Title));
            }
            html.Append("</td><td class=\"num\">")
                .Append(skipped ? "<span class=\"muted\">bỏ qua</span>" : count > 0 ? count.ToString("N0") : "<span class=\"ok\">✓ không có</span>")
                .Append("</td></tr>");
        }
        html.Append("</tbody></table>");

        if (report.Findings.Count == 0)
        {
            html.Append("<p class=\"allok\">Không phát hiện vấn đề nào.</p>");
        }

        foreach (var rule in SchemaQualityChecker.AllRules)
        {
            var findings = report.Findings.Where(f => f.Rule.Id == rule.Id).ToList();
            if (findings.Count == 0)
            {
                continue;
            }
            var hasSql = findings.Any(f => f.Sql is not null);
            html.Append("<section id=\"r-").Append(rule.Id).Append("\" class=\"").Append(Css(rule.Severity)).Append("\"><h2>")
                .Append(Badge(rule.Severity)).Append(' ').Append(E(rule.Title)).Append(" (").Append(findings.Count.ToString("N0")).Append(")</h2>");
            html.Append("<p class=\"why\">").Append(E(rule.Why)).Append("</p>");
            html.Append("<table><thead><tr><th>Bảng</th><th>Chi tiết</th>").Append(hasSql ? "<th>SQL gợi ý (tham khảo)</th>" : "")
                .Append("</tr></thead><tbody>");
            foreach (var finding in findings)
            {
                html.Append("<tr><td class=\"code\">").Append(TableLink(finding.TableName, dictionaryFileName)).Append("</td><td>")
                    .Append(E(finding.Message)).Append("</td>");
                if (hasSql)
                {
                    html.Append("<td class=\"code sql\">").Append(E(finding.Sql)).Append("</td>");
                }
                html.Append("</tr>");
            }
            html.Append("</tbody></table>");
            if (hasSql)
            {
                html.Append("<p class=\"muted note\">SQL chỉ để tham khảo - cú pháp có thể cần sửa theo loại database; kiểm tra kỹ trước khi chạy.</p>");
            }
            html.Append("</section>");
        }

        html.Append("</main>");
        html.Append(
            """
            <script>
            (function () {
              var box = document.getElementById('hideInfo');
              box.addEventListener('change', function () { document.body.classList.toggle('hide-info', box.checked); });
            })();
            </script>
            </body></html>
            """);
        return html.ToString();
    }

    private static string TableLink(string tableName, string? dictionaryFileName) =>
        dictionaryFileName is null
            ? E(tableName)
            : $"<a href=\"{E(Uri.EscapeDataString(dictionaryFileName))}#table={E(Uri.EscapeDataString(tableName))}\" target=\"_blank\">{E(tableName)}</a>";

    private static void Chip(StringBuilder html, QualitySeverity severity, int count) =>
        html.Append("<span class=\"chip ").Append(Css(severity)).Append("\"><b>").Append(count.ToString("N0")).Append("</b> ")
            .Append(Label(severity).ToLowerInvariant()).Append("</span>");

    private static string Badge(QualitySeverity severity) =>
        $"<span class=\"badge {Css(severity)}\">{Label(severity)}</span>";

    public static string Label(QualitySeverity severity) => severity switch
    {
        QualitySeverity.Error => "Nên sửa",
        QualitySeverity.Warning => "Cân nhắc",
        _ => "Thông tin",
    };

    private static string Css(QualitySeverity severity) => severity switch
    {
        QualitySeverity.Error => "error",
        QualitySeverity.Warning => "warning",
        _ => "info",
    };

    /// <summary>Chỉ thoát 5 ký tự đặc biệt của HTML (giữ chữ có dấu / tiếng Nhật đọc được trong file).</summary>
    private static string E(string? text) => (text ?? string.Empty)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);

    private static string Head(QualityReport report) =>
        $$"""
        <!doctype html>
        <html lang="vi">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Kiểm tra chất lượng: {{E(report.Name)}}</title>
        <style>
          :root { color-scheme: light dark; --error: #d1435b; --warning: #c98a00; --info: #808890; --ok: #2e9e5b; }
          * { box-sizing: border-box; }
          body { margin: 0; font-family: "Segoe UI", system-ui, sans-serif; font-size: 14px; }
          header { padding: 18px 24px 12px; border-bottom: 1px solid #8884; position: sticky; top: 0; background: Canvas; z-index: 1; }
          h1 { font-size: 20px; margin: 0 0 6px; }
          .sub { opacity: .8; }
          .code { font-family: Consolas, monospace; }
          .muted { opacity: .6; }
          .chips { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0 8px; }
          .chip { padding: 4px 10px; border-radius: 14px; border: 1px solid #8886; }
          .chip.error { border-color: var(--error); } .chip.warning { border-color: var(--warning); } .chip.info { border-color: var(--info); }
          .toggle { font-size: 13px; user-select: none; }
          .warn { color: var(--warning); margin: 8px 0 0; font-size: 13px; }
          main { padding: 8px 24px 40px; }
          .badge { display: inline-block; padding: 1px 8px; border-radius: 4px; color: #fff; font-size: 12px; font-weight: 600; white-space: nowrap; }
          .badge.error { background: var(--error); } .badge.warning { background: var(--warning); } .badge.info { background: var(--info); }
          .ok { color: var(--ok); } .allok { font-size: 16px; color: var(--ok); font-weight: 600; }
          table { border-collapse: collapse; width: 100%; font-size: 13px; margin-bottom: 8px; }
          table.summary { width: auto; min-width: 520px; margin: 8px 0 12px; }
          th, td { border: 1px solid #8886; padding: 4px 8px; text-align: left; vertical-align: top; }
          th { background: #8882; }
          td.num, th.num { text-align: right; }
          td.code { white-space: pre-wrap; word-break: break-word; }
          td.sql { font-size: 12px; }
          a { color: #4a90d9; text-decoration: none; } a:hover { text-decoration: underline; }
          section { margin-top: 22px; scroll-margin-top: 140px; }
          h2 { font-size: 16px; margin: 0 0 6px; padding-left: 8px; border-left: 4px solid #8886; }
          section.error h2 { border-color: var(--error); } section.warning h2 { border-color: var(--warning); } section.info h2 { border-color: var(--info); }
          .why { margin: 0 0 8px; opacity: .85; }
          .note { font-size: 12px; margin: 0; }
          body.hide-info section.info, body.hide-info table.summary tr.info, body.hide-info .chip.info { display: none; }
        </style>
        </head>
        <body>

        """;
}
