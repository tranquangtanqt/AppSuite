using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ModuleD.Models;

namespace ModuleD.Services;

/// <summary>
/// Reads the imported DB-dictionary back out of <see cref="ModuleDDatabase"/> and renders a single
/// self-contained static HTML file (data embedded inline as JSON, vanilla JS, no CDN/network
/// dependency) so it can be opened offline via file:// - left menu with a search box, right pane
/// showing the selected table's columns.
/// </summary>
public sealed class HtmlReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Generate(ModuleDDatabase database)
    {
        var tables = database.GetAllTables();
        var columns = database.GetAllColumns();
        var foreignKeys = database.GetAllForeignKeys();
        var columnsByTable = columns
            .GroupBy(c => c.TableName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var foreignKeysByTable = foreignKeys
            .GroupBy(f => f.TableName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var payload = new
        {
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            tables = tables.Select(t => new
            {
                name = t.TableName,
                alias = t.Alias,
                japaneseName = t.JapaneseName,
                kind = t.Kind,
                note = t.Note,
                description = t.Description,
                managementType = t.ManagementType,
                cautionItems = t.CautionItems,
                revisionHistory = t.RevisionHistory,
                sourceFile = t.SourceFile,
                sourceSheet = t.SourceSheet,
                columns = (columnsByTable.TryGetValue(t.TableName, out var cols) ? cols : [])
                    .Select(c => new
                    {
                        level = c.Level,
                        name = c.ColumnName,
                        dataType = c.DataType,
                        length = c.Length,
                        nullable = c.Nullable,
                        defaultValue = c.DefaultValue,
                        japaneseName = c.JapaneseName,
                        description = c.Description,
                        fullName = c.FullName,
                        valueRestriction = c.ValueRestriction,
                        meta = c.Meta,
                        isCommon = c.IsCommon,
                    }),
                foreignKeys = (foreignKeysByTable.TryGetValue(t.TableName, out var fks) ? fks : [])
                    .Select(f => new
                    {
                        localColumns = f.LocalColumns,
                        referencedTable = f.ReferencedTable,
                        referencedColumns = f.ReferencedColumns,
                    }),
            }),
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions).Replace("</", "<\\/", StringComparison.Ordinal);

        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Database");
        Directory.CreateDirectory(dataDirectory);
        var htmlPath = Path.Combine(dataDirectory, "ModuleD.html");
        File.WriteAllText(htmlPath, BuildHtml(json));

        return htmlPath;
    }

    private static string BuildHtml(string dataJson)
    {
        return HtmlTemplate.Replace("%%DATA_JSON%%", dataJson, StringComparison.Ordinal);
    }

    private const string HtmlTemplate =
        """
        <!doctype html>
        <html lang="vi">
        <head>
        <meta charset="utf-8">
        <title>CSDL</title>
        <style>
          :root { color-scheme: light dark; }
          * { box-sizing: border-box; }
          body { margin: 0; font-family: "Segoe UI", system-ui, sans-serif; display: flex; height: 100vh; }
          #sidebar { width: 340px; min-width: 260px; border-right: 1px solid #8884; display: flex; flex-direction: column; }
          #searchArea { padding: 10px; display: flex; gap: 6px; border-bottom: 1px solid #8884; }
          #searchBox { flex: 1; padding: 6px 8px; font-size: 14px; }
          #searchButton { padding: 6px 12px; cursor: pointer; }
          #summary { padding: 6px 10px; font-size: 12px; opacity: .7; }
          #tableList { list-style: none; margin: 0; padding: 0; overflow-y: auto; flex: 1; }
          #tableList li { padding: 8px 12px; cursor: pointer; border-bottom: 1px solid #8882; }
          #tableList li:hover { background: #8882; }
          #tableList li.active { background: #4a90d966; }
          #tableList .tname { font-weight: 600; font-family: Consolas, monospace; }
          #tableList .jname { font-size: 12px; opacity: .75; }
          #content { flex: 1; overflow-y: auto; padding: 20px; }
          table.cols { border-collapse: collapse; width: 100%; font-size: 13px; }
          table.cols th, table.cols td { border: 1px solid #8886; padding: 4px 8px; text-align: left; vertical-align: top; }
          table.cols th { position: sticky; top: 0; background: #d9f2df; color: #000; }
          h1 { font-size: 18px; margin: 0 0 4px; font-family: Consolas, monospace; }
          .meta { font-size: 13px; opacity: .8; margin-bottom: 12px; white-space: pre-wrap; }
          .metaLabel { font-weight: 600; opacity: .9; margin-bottom: 2px; }
          mark { background: #ffe066; color: #000; }
          tr.pk { background: rgba(255, 193, 7, .22); }
          tr.common { background: rgba(66, 133, 244, .16); }
          tr.pk.common { background: linear-gradient(90deg, rgba(255, 193, 7, .22) 0 50%, rgba(66, 133, 244, .16) 50% 100%); }
          .legend { display: flex; gap: 16px; font-size: 12px; opacity: .8; margin-bottom: 8px; align-items: center; }
          .legend .swatch { display: inline-block; width: 12px; height: 12px; border-radius: 2px; margin-right: 4px; vertical-align: -1px; }
          .legend .swatch.pk { background: rgba(255, 193, 7, .5); }
          .legend .swatch.common { background: rgba(66, 133, 244, .4); }
          .fk-link { color: #4a90d9; text-decoration: none; font-family: Consolas, monospace; }
          .fk-link:hover { text-decoration: underline; }
        </style>
        </head>
        <body>
        <div id="sidebar">
          <div id="searchArea">
            <input id="searchBox" type="text" placeholder="Tim ten bang / ten cot..." />
            <button id="searchButton">Tim kiem</button>
          </div>
          <div id="summary"></div>
          <ul id="tableList"></ul>
        </div>
        <div id="content">
          <p>Chon 1 bang o menu ben trai, hoac go tu khoa tim kiem (ten bang, ten tieng Nhat, hoac ten cot).</p>
        </div>
        <script id="app-data" type="application/json">%%DATA_JSON%%</script>
        <script>
        (function () {
          var data = JSON.parse(document.getElementById('app-data').textContent);
          var tables = data.tables;
          var listEl = document.getElementById('tableList');
          var contentEl = document.getElementById('content');
          var searchBox = document.getElementById('searchBox');
          var searchButton = document.getElementById('searchButton');
          var summaryEl = document.getElementById('summary');

          var totalColumns = tables.reduce(function (sum, t) { return sum + t.columns.length; }, 0);
          summaryEl.textContent = 'Tong: ' + tables.length + ' bang, ' + totalColumns + ' cot. (Xuat luc ' + data.generatedAt + ')';

          var tablesByName = {};
          tables.forEach(function (t) { tablesByName[t.name] = t; });

          function matches(table, query) {
            if (!query) return true;
            if (table.name.toLowerCase().indexOf(query) >= 0) return true;
            if (table.japaneseName.toLowerCase().indexOf(query) >= 0) return true;
            for (var i = 0; i < table.columns.length; i++) {
              var c = table.columns[i];
              if (c.name.toLowerCase().indexOf(query) >= 0) return true;
              if (c.japaneseName.toLowerCase().indexOf(query) >= 0) return true;
            }
            return false;
          }

          function renderList() {
            var query = searchBox.value.trim().toLowerCase();
            listEl.innerHTML = '';
            tables.filter(function (t) { return matches(t, query); }).forEach(function (t) {
              var li = document.createElement('li');
              li.innerHTML = '<div class="tname"></div><div class="jname"></div>';
              li.querySelector('.tname').textContent = t.name;
              li.querySelector('.jname').textContent = t.japaneseName || t.alias || '';
              li.addEventListener('click', function () { selectTable(t, li); });
              listEl.appendChild(li);
            });
          }

          function renderSection(label, text) {
            if (!text) return '';
            return '<div class="meta"><div class="metaLabel">' + escapeHtml(label) + '</div>' + escapeHtml(text) + '</div>';
          }

          function renderForeignKeys(fks) {
            if (!fks || !fks.length) return '';
            var rows = fks.map(function (f) {
              // Real link (not a JS click-handler) pointing at this same file with a #table=... hash,
              // opened via target="_blank" so it lands in a new tab already showing that table.
              var refCell = tablesByName[f.referencedTable]
                ? '<a href="#table=' + encodeURIComponent(f.referencedTable) + '" target="_blank" class="fk-link">' +
                  escapeHtml(f.referencedTable) + '</a>'
                : escapeHtml(f.referencedTable);
              return '<tr>' +
                '<td>' + escapeHtml(f.localColumns) + '</td>' +
                '<td>' + refCell + '</td>' +
                '<td>' + escapeHtml(f.referencedColumns || f.localColumns) + '</td>' +
                '</tr>';
            }).join('');
            return '<div class="metaLabel" style="margin-top:12px">Khoa ngoai (FOREIGN)</div>' +
              '<table class="cols"><thead><tr><th>Cot cua bang nay</th><th>Bang tham chieu</th><th>Cot tham chieu</th></tr></thead>' +
              '<tbody>' + rows + '</tbody></table>';
          }

          function selectTableByName(name) {
            var target = tablesByName[name];
            if (!target) return;
            searchBox.value = name;
            renderList();
            var li = Array.prototype.find.call(listEl.children, function (item) {
              return item.querySelector('.tname').textContent === name;
            });
            selectTable(target, li);
          }

          function applyHash() {
            var match = /^#table=(.+)$/.exec(location.hash);
            if (match) selectTableByName(decodeURIComponent(match[1]));
          }

          function renderColumnsTable(t) {
            var html = '<h1>' + escapeHtml(t.name) + (t.alias ? ' (' + escapeHtml(t.alias) + ')' : '') + '</h1>';
            html += '<div class="meta">' + escapeHtml(t.japaneseName) + (t.kind ? ' - ' + escapeHtml(t.kind) : '') + '\n';
            html += 'Nguon: ' + escapeHtml(t.sourceFile) + ' / ' + escapeHtml(t.sourceSheet) + '</div>';
            html += renderSection('Mo ta (説明)', t.description);
            html += renderSection('Loai quan ly (管理タイプ)', t.managementType);
            html += renderSection('Muc can luu y khi thay doi (運用後の変更に注意が必要な項目)', t.cautionItems);
            html += renderSection('Cai cach / bai bo (改廃)', t.revisionHistory);
            html += renderForeignKeys(t.foreignKeys);
            html += '<div class="legend" style="margin-top:12px">' +
              '<span><span class="swatch pk"></span>Khoa chinh (level 0)</span>' +
              '<span><span class="swatch common"></span>Cot dung chung ($...$ group)</span>' +
              '</div>';
            html += '<table class="cols"><thead><tr>' +
              '<th>Level</th><th>Ten cot</th><th>Kieu</th><th>Xac dinh</th><th>Null</th>' +
              '<th>Ten tieng Nhat</th><th>Mo ta</th></tr></thead><tbody>';
            t.columns.forEach(function (c) {
              var rowClasses = [];
              if (c.level === 0) rowClasses.push('pk');
              if (c.isCommon) rowClasses.push('common');
              html += '<tr class="' + rowClasses.join(' ') + '">' +
                '<td>' + (c.level === null || c.level === undefined ? '' : c.level) + '</td>' +
                '<td>' + escapeHtml(c.name) + '</td>' +
                '<td>' + escapeHtml(c.dataType) + '</td>' +
                '<td>' + escapeHtml(c.length) + '</td>' +
                '<td>' + escapeHtml(c.nullable) + '</td>' +
                '<td>' + escapeHtml(c.japaneseName) + '</td>' +
                '<td>' + escapeHtml(c.description) + '</td>' +
                '</tr>';
            });
            html += '</tbody></table>';
            contentEl.innerHTML = html;
          }

          function selectTable(t, li) {
            var current = listEl.querySelector('li.active');
            if (current) current.classList.remove('active');
            if (li) li.classList.add('active');
            renderColumnsTable(t);
            document.title = 'CSDL - ' + t.name;
          }

          function escapeHtml(s) {
            return (s || '').replace(/[&<>"]/g, function (ch) {
              return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[ch];
            });
          }

          window.addEventListener('hashchange', applyHash);

          searchButton.addEventListener('click', renderList);
          searchBox.addEventListener('keyup', function (e) { if (e.key === 'Enter') renderList(); });
          searchBox.addEventListener('input', renderList);

          renderList();
          applyHash();
        })();
        </script>
        </body>
        </html>
        """;
}
