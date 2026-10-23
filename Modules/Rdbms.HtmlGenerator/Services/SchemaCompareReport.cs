using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Trang HTML kết quả <see cref="SchemaComparer"/>: 1 file tự chứa (không JS ngoài, mở offline). Tóm tắt số bảng ở đầu,
/// rồi bảng chỉ có ở A, chỉ có ở B, và từng bảng có thay đổi (cột / khoá ngoại / index / ràng buộc). Ô tích "Ẩn thay đổi
/// chỉ khác mô tả" ẩn dòng mô tả và bảng chỉ khác mô tả (comment thường lệch giữa các môi trường, ít quan trọng).
/// </summary>
public static class SchemaCompareReport
{
    public static string Build(SchemaDiff diff, DateTime generatedAt)
    {
        var html = new StringBuilder();
        html.Append(Head(diff));
        html.Append("<header><h1>So sánh schema</h1>");
        html.Append("<div class=\"sides\"><span class=\"side a\">A</span> ").Append(E(diff.NameA)).Append(" <span class=\"muted\">(gốc)</span>");
        html.Append("<span class=\"vs\">→</span><span class=\"side b\">B</span> ").Append(E(diff.NameB));
        html.Append(" <span class=\"muted\">· xuất lúc ").Append(generatedAt.ToString("yyyy-MM-dd HH:mm:ss")).Append("</span></div>");

        html.Append("<div class=\"chips\">");
        Chip(html, "onlyA", diff.TablesOnlyInA.Count, "bảng chỉ có ở A", "#only-a");
        Chip(html, "onlyB", diff.TablesOnlyInB.Count, "bảng chỉ có ở B", "#only-b");
        Chip(html, "changed", diff.ChangedTables.Count, "bảng có thay đổi", "#changed");
        Chip(html, "same", diff.UnchangedTableCount, "bảng giống nhau", null);
        html.Append("</div>");
        html.Append("<label class=\"toggle\"><input type=\"checkbox\" id=\"hideDesc\"> Ẩn thay đổi chỉ khác mô tả (comment)</label>");
        if (!diff.IndexesCompared)
        {
            html.Append("<p class=\"warn\">Không so index / ràng buộc UNIQUE, CHECK: 1 trong 2 file được đọc bằng bản cũ chưa có dữ liệu này - ")
                .Append("bấm lại \"1. Đọc database\" cho file đó rồi so sánh lại.</p>");
        }
        html.Append("</header><main>");

        if (diff.IsIdentical)
        {
            html.Append("<p class=\"identical\">Hai schema giống nhau: cùng bảng, cột, kiểu, khoá ngoại")
                .Append(diff.IndexesCompared ? ", index và ràng buộc" : "").Append(".</p>");
        }

        TableList(html, "only-a", "onlyA", $"Bảng chỉ có ở A ({diff.TablesOnlyInA.Count}) - B không có", diff.TablesOnlyInA);
        TableList(html, "only-b", "onlyB", $"Bảng chỉ có ở B ({diff.TablesOnlyInB.Count}) - A không có", diff.TablesOnlyInB);

        if (diff.ChangedTables.Count > 0)
        {
            html.Append("<section id=\"changed\"><h2 class=\"changed\">Bảng có thay đổi (").Append(diff.ChangedTables.Count).Append(")</h2>");
            html.Append("<div class=\"toc\">");
            foreach (var table in diff.ChangedTables)
            {
                html.Append("<a href=\"#t-").Append(E(table.TableName)).Append('"')
                    .Append(table.OnlyDescriptionChanges ? " class=\"desc-only\"" : "").Append('>')
                    .Append(E(table.TableName)).Append("</a>");
            }
            html.Append("</div>");
            foreach (var table in diff.ChangedTables)
            {
                TableBlock(html, table);
            }
            html.Append("</section>");
        }

        html.Append("</main>");
        html.Append(
            """
            <script>
            (function () {
              var box = document.getElementById('hideDesc');
              box.addEventListener('change', function () { document.body.classList.toggle('hide-desc', box.checked); });
            })();
            </script>
            </body></html>
            """);
        return html.ToString();
    }

    private static void Chip(StringBuilder html, string css, int count, string label, string? href)
    {
        var tag = href is not null && count > 0 ? "a" : "span";
        html.Append('<').Append(tag).Append(" class=\"chip ").Append(css).Append('"');
        if (tag == "a")
        {
            html.Append(" href=\"").Append(href).Append('"');
        }
        html.Append("><b>").Append(count.ToString("N0")).Append("</b> ").Append(label).Append("</").Append(tag).Append('>');
    }

    private static void TableList(StringBuilder html, string id, string css, string title, List<Models.DbTableRecord> tables)
    {
        if (tables.Count == 0)
        {
            return;
        }
        html.Append("<section id=\"").Append(id).Append("\"><h2 class=\"").Append(css).Append("\">").Append(E(title)).Append("</h2>");
        html.Append("<table><thead><tr><th>Tên bảng</th><th>Loại</th><th>Mô tả</th></tr></thead><tbody>");
        foreach (var table in tables)
        {
            html.Append("<tr><td class=\"code\">").Append(E(table.TableName)).Append("</td><td>").Append(E(table.Kind))
                .Append("</td><td>").Append(E(table.Description)).Append("</td></tr>");
        }
        html.Append("</tbody></table></section>");
    }

    private static void TableBlock(StringBuilder html, TableDiff table)
    {
        html.Append("<div class=\"block").Append(table.OnlyDescriptionChanges ? " desc-only" : "").Append("\" id=\"t-")
            .Append(E(table.TableName)).Append("\"><h3>").Append(E(table.TableName)).Append("</h3>");

        if (table.TableChanges.Count > 0)
        {
            html.Append("<table><thead><tr><th>Thuộc tính bảng</th><th>A</th><th>B</th></tr></thead><tbody>");
            foreach (var change in table.TableChanges)
            {
                html.Append("<tr").Append(IsDesc(change) ? " class=\"desc\"" : "").Append("><td>").Append(E(change.Field))
                    .Append("</td><td class=\"code before\">").Append(E(change.Before)).Append("</td><td class=\"code after\">")
                    .Append(E(change.After)).Append("</td></tr>");
            }
            html.Append("</tbody></table>");
        }

        if (table.ColumnsOnlyInA.Count + table.ColumnsOnlyInB.Count + table.ColumnChanges.Count > 0)
        {
            html.Append("<table><thead><tr><th>Cột</th><th>Thay đổi</th><th>A</th><th>B</th></tr></thead><tbody>");
            foreach (var column in table.ColumnsOnlyInA)
            {
                html.Append("<tr class=\"rowA\"><td class=\"code\">").Append(E(column.ColumnName))
                    .Append("</td><td>Chỉ có ở A</td><td class=\"code\">").Append(E(ColumnSummary(column)))
                    .Append("</td><td class=\"muted\">(không có)</td></tr>");
            }
            foreach (var column in table.ColumnsOnlyInB)
            {
                html.Append("<tr class=\"rowB\"><td class=\"code\">").Append(E(column.ColumnName))
                    .Append("</td><td>Chỉ có ở B</td><td class=\"muted\">(không có)</td><td class=\"code\">")
                    .Append(E(ColumnSummary(column))).Append("</td></tr>");
            }
            foreach (var column in table.ColumnChanges)
            {
                foreach (var change in column.Changes)
                {
                    html.Append("<tr").Append(IsDesc(change) ? " class=\"desc\"" : "").Append("><td class=\"code\">")
                        .Append(E(column.ColumnName)).Append("</td><td>").Append(E(change.Field))
                        .Append("</td><td class=\"code before\">").Append(Value(change.Before))
                        .Append("</td><td class=\"code after\">").Append(Value(change.After)).Append("</td></tr>");
                }
            }
            html.Append("</tbody></table>");
        }

        ItemTable(html, "Khoá ngoại", table.ForeignKeysOnlyInA, table.ForeignKeysOnlyInB);
        ItemTable(html, "Index", table.IndexesOnlyInA, table.IndexesOnlyInB);
        ItemTable(html, "Ràng buộc UNIQUE / CHECK", table.ConstraintsOnlyInA, table.ConstraintsOnlyInB);
        html.Append("</div>");
    }

    private static void ItemTable(StringBuilder html, string title, List<string> onlyInA, List<string> onlyInB)
    {
        if (onlyInA.Count + onlyInB.Count == 0)
        {
            return;
        }
        html.Append("<table><thead><tr><th>").Append(E(title)).Append("</th><th>Định nghĩa</th></tr></thead><tbody>");
        foreach (var item in onlyInA)
        {
            html.Append("<tr class=\"rowA\"><td>Chỉ có ở A</td><td class=\"code\">").Append(E(item)).Append("</td></tr>");
        }
        foreach (var item in onlyInB)
        {
            html.Append("<tr class=\"rowB\"><td>Chỉ có ở B</td><td class=\"code\">").Append(E(item)).Append("</td></tr>");
        }
        html.Append("</tbody></table>");
    }

    private static string ColumnSummary(Models.DbColumnRecord column)
    {
        var parts = new List<string> { column.DataType };
        if (!string.IsNullOrEmpty(column.Length) && !column.DataType.Contains('(', StringComparison.Ordinal))
        {
            parts[0] += $"({column.Length})";
        }
        if (column.Nullable == "N")
        {
            parts.Add("NOT NULL");
        }
        if (column.Level == 0)
        {
            parts.Add("khoá chính");
        }
        if (!string.IsNullOrEmpty(column.DefaultValue))
        {
            parts.Add($"mặc định {column.DisplayDefaultValue}");
        }
        return string.Join(", ", parts.Where(p => p.Length > 0));
    }

    private static bool IsDesc(FieldChange change) => change.Field == SchemaComparer.DescriptionField;

    private static string Value(string value) => value.Length == 0 ? "<span class=\"muted\">(trống)</span>" : E(value);

    /// <summary>Chỉ thoát 5 ký tự đặc biệt của HTML - WebUtility.HtmlEncode còn đổi chữ Latin-1 có dấu (ó, à...) thành
    /// &amp;#243;, trang vẫn hiện đúng nhưng file khó đọc / khó tìm.</summary>
    private static string E(string? text) => (text ?? string.Empty)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);

    private static string Head(SchemaDiff diff) =>
        $$"""
        <!doctype html>
        <html lang="vi">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>So sánh schema: {{E(diff.NameA)}} → {{E(diff.NameB)}}</title>
        <style>
          :root { color-scheme: light dark; --a: #d1435b; --b: #2e9e5b; --c: #c98a00; }
          * { box-sizing: border-box; }
          body { margin: 0; font-family: "Segoe UI", system-ui, sans-serif; font-size: 14px; }
          header { padding: 18px 24px 12px; border-bottom: 1px solid #8884; position: sticky; top: 0; background: Canvas; z-index: 1; }
          h1 { font-size: 20px; margin: 0 0 6px; }
          .sides { font-family: Consolas, monospace; }
          .side { display: inline-block; min-width: 20px; padding: 0 6px; border-radius: 4px; color: #fff; font-weight: 700; text-align: center; font-family: "Segoe UI", sans-serif; }
          .side.a { background: var(--a); } .side.b { background: var(--b); }
          .vs { margin: 0 10px; opacity: .6; }
          .muted { opacity: .6; font-family: "Segoe UI", sans-serif; }
          .chips { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0 8px; }
          .chip { padding: 4px 10px; border-radius: 14px; border: 1px solid #8886; text-decoration: none; color: inherit; }
          .chip.onlyA { border-color: var(--a); } .chip.onlyB { border-color: var(--b); } .chip.changed { border-color: var(--c); }
          a.chip:hover { background: #8882; }
          .toggle { font-size: 13px; user-select: none; }
          .warn { color: var(--c); margin: 8px 0 0; font-size: 13px; }
          main { padding: 8px 24px 40px; }
          .identical { font-size: 16px; color: var(--b); font-weight: 600; }
          h2 { font-size: 16px; margin: 22px 0 8px; padding-left: 8px; border-left: 4px solid #8886; }
          h2.onlyA { border-color: var(--a); } h2.onlyB { border-color: var(--b); } h2.changed { border-color: var(--c); }
          h3 { font-size: 15px; margin: 0 0 8px; font-family: Consolas, monospace; }
          .toc { display: flex; flex-wrap: wrap; gap: 4px 12px; margin-bottom: 12px; font-family: Consolas, monospace; font-size: 13px; }
          .toc a { color: #4a90d9; text-decoration: none; } .toc a:hover { text-decoration: underline; }
          .block { border: 1px solid #8886; border-radius: 6px; padding: 12px; margin-bottom: 14px; scroll-margin-top: 150px; }
          table { border-collapse: collapse; width: 100%; font-size: 13px; margin-bottom: 10px; }
          th, td { border: 1px solid #8886; padding: 4px 8px; text-align: left; vertical-align: top; }
          th { background: #8882; }
          td.code { font-family: Consolas, monospace; white-space: pre-wrap; word-break: break-word; }
          td.before { background: rgba(209, 67, 91, .10); } td.after { background: rgba(46, 158, 91, .12); }
          tr.rowA td:first-child { border-left: 4px solid var(--a); } tr.rowB td:first-child { border-left: 4px solid var(--b); }
          body.hide-desc tr.desc, body.hide-desc .block.desc-only, body.hide-desc .toc a.desc-only { display: none; }
        </style>
        </head>
        <body>

        """;
}
