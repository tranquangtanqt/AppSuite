using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Reads the imported DB-dictionary back out of <see cref="RdbmsHtmlGeneratorDatabase"/> and renders a single
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string? N(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static T[]? NullIfEmpty<T>(IEnumerable<T> items)
    {
        var array = items.ToArray();
        return array.Length == 0 ? null : array;
    }

    public string Generate(RdbmsHtmlGeneratorDatabase database, string databaseName)
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
        var indexesByTable = database.GetAllIndexes()
            .GroupBy(i => i.TableName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var constraintsByTable = database.GetAllConstraints()
            .GroupBy(c => c.TableName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Empty strings / empty lists are left out of the JSON (WhenWritingNull) - with 200k+ columns the always-empty
        // Excel-only fields (japaneseName, fullName, meta...) were most of the file. The page's script fills them back
        // in as '' / [] right after JSON.parse (see "normalize" in HtmlTemplate).
        var payload = new
        {
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            extendedInfo = database.HasExtendedInfo(),
            tables = tables.Select(t => new
            {
                name = t.TableName,
                alias = N(t.Alias),
                japaneseName = N(t.JapaneseName),
                kind = N(t.Kind),
                note = N(t.Note),
                description = N(t.Description),
                managementType = N(t.ManagementType),
                cautionItems = N(t.CautionItems),
                revisionHistory = N(t.RevisionHistory),
                sourceFile = N(t.SourceFile),
                sourceSheet = N(t.SourceSheet),
                estimatedRows = t.EstimatedRows,
                columns = (columnsByTable.TryGetValue(t.TableName, out var cols) ? cols : [])
                    .Select(c => new
                    {
                        level = c.Level,
                        name = c.ColumnName,
                        dataType = N(c.DataType),
                        length = N(c.Length),
                        nullable = N(c.Nullable),
                        defaultValue = N(c.DisplayDefaultValue),
                        japaneseName = N(c.JapaneseName),
                        description = N(c.Description),
                        fullName = N(c.FullName),
                        valueRestriction = N(c.ValueRestriction),
                        meta = N(c.Meta),
                    }),
                foreignKeys = NullIfEmpty((foreignKeysByTable.TryGetValue(t.TableName, out var fks) ? fks : [])
                    .Select(f => new
                    {
                        localColumns = f.LocalColumns,
                        referencedTable = f.ReferencedTable,
                        referencedColumns = N(f.ReferencedColumns),
                    })),
                indexes = NullIfEmpty((indexesByTable.TryGetValue(t.TableName, out var idx) ? idx : [])
                    .Select(i => new
                    {
                        name = i.IndexName,
                        columns = N(i.Columns),
                        isUnique = i.IsUnique,
                        isPrimaryKey = i.IsPrimaryKey,
                        indexType = N(i.IndexType),
                        note = N(i.Note),
                    })),
                constraints = NullIfEmpty((constraintsByTable.TryGetValue(t.TableName, out var cons) ? cons : [])
                    .Select(c => new
                    {
                        name = c.ConstraintName,
                        type = c.ConstraintType,
                        definition = N(DbColumnRecord.StripOuterParentheses(c.Definition)),
                    })),
            }),
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions).Replace("</", "<\\/", StringComparison.Ordinal);

        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Database");
        Directory.CreateDirectory(dataDirectory);
        var htmlPath = Path.Combine(dataDirectory, $"{SanitizeFileName(databaseName)}.html");
        File.WriteAllText(htmlPath, BuildHtml(json));

        return htmlPath;
    }

    /// <summary>Replaces characters that PostgreSQL/Oracle allow in a database name but Windows
    /// doesn't allow in a file name (rare in practice, but database names are user-entered).</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return sanitized.Length == 0 ? "Rdbms.HtmlGenerator" : sanitized;
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
          #searchArea { padding: 10px; display: flex; flex-direction: column; gap: 6px; border-bottom: 1px solid #8884; }
          #searchArea input { width: 100%; padding: 6px 8px; font-size: 14px; }
          #searchButton { padding: 6px 12px; cursor: pointer; }
          #kindFilter { display: flex; border: 1px solid #8886; border-radius: 4px; overflow: hidden; }
          #kindFilter button { flex: 1; padding: 4px 6px; font-size: 12px; border: 0; border-right: 1px solid #8886;
            background: transparent; color: inherit; cursor: pointer; }
          #kindFilter button:last-child { border-right: 0; }
          #kindFilter button.on { background: #4a90d9; color: #fff; }
          #searchArea label { font-size: 12px; display: flex; align-items: center; gap: 6px; cursor: pointer; }
          #searchArea label input { width: auto; margin: 0; }
          .badge { display: inline-block; font-size: 10px; font-weight: 600; padding: 0 4px; margin-left: 6px; border-radius: 3px;
            border: 1px solid #8888; opacity: .8; vertical-align: 1px; font-family: "Segoe UI", system-ui, sans-serif; }
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
          .legend { display: flex; gap: 16px; font-size: 12px; opacity: .8; margin-bottom: 8px; align-items: center; }
          .legend .swatch { display: inline-block; width: 12px; height: 12px; border-radius: 2px; margin-right: 4px; vertical-align: -1px; }
          .legend .swatch.pk { background: rgba(255, 193, 7, .5); }
          .fk-link { color: #4a90d9; text-decoration: none; font-family: Consolas, monospace; }
          .fk-link:hover { text-decoration: underline; }
          .groupBlock { border: 1px solid #8886; border-radius: 6px; padding: 12px; margin-bottom: 16px; }
          .groupBlock h2 { font-size: 15px; font-family: Consolas, monospace; margin: 0 0 8px; }
          td.code { font-family: Consolas, monospace; white-space: pre-wrap; }
          /* Sơ đồ quan hệ (SVG tự vẽ, renderRelationDiagram) */
          details.er-block { border: 1px solid #8886; border-radius: 6px; padding: 8px 12px; margin: 12px 0 4px; }
          details.er-block summary { cursor: pointer; font-weight: 600; font-size: 13px; }
          .er-hint { font-size: 12px; opacity: .7; margin: 4px 0 0; }
          .er-wrap { overflow-x: auto; margin-top: 8px; }
          svg.er { font-family: Consolas, monospace; font-size: 12px; display: block; }
          svg.er text { fill: currentColor; }
          .er-frame { fill: Canvas; stroke: #8889; stroke-width: 1; }
          .er-head { fill: #d9f2df; }
          svg.er text.er-title { font-weight: 700; fill: #000; }
          .er-box.center .er-frame { stroke: #4a90d9; stroke-width: 2; }
          .er-box.center .er-head { fill: #4a90d9; }
          svg.er .er-box.center text.er-title { fill: #fff; }
          .er-box.missing .er-frame { stroke-dasharray: 4 3; }
          .er-box.missing .er-head { fill: #8883; }
          svg.er .er-box.missing text.er-title { fill: currentColor; opacity: .8; }
          .er-box.link { cursor: pointer; }
          .er-box.link:hover .er-frame { stroke: #4a90d9; stroke-width: 2; }
          svg.er text.er-tag { font-size: 9px; font-weight: 700; fill: #b8860b; }
          svg.er text.er-tag.fk { fill: #4a90d9; }
          .er-link { fill: none; stroke: #4a90d9; stroke-width: 1.4; opacity: .75; }
          .er-link:hover { stroke-width: 3; opacity: 1; }
          .er-dot { fill: #4a90d9; }
          svg.er text.er-note { font-family: "Segoe UI", system-ui, sans-serif; opacity: .65; }
        </style>
        </head>
        <body>
        <div id="sidebar">
          <div id="searchArea">
            <input id="searchBox" type="text" placeholder="Tên bảng..." />
            <input id="columnBox" type="text" placeholder="Tên cột (tuỳ chọn)..." />
            <div id="kindFilter" role="group" aria-label="Loại">
              <button type="button" data-kind="all">Tất cả</button>
              <button type="button" data-kind="table">Bảng</button>
              <button type="button" data-kind="view">View</button>
            </div>
            <label title="Tên bảng / Tên cột khớp cả với mô tả (comment) của bảng / cột"><input id="searchDescription" type="checkbox" />Tìm cả trong mô tả</label>
            <button id="searchButton">Tìm kiếm</button>
          </div>
          <div id="summary"></div>
          <ul id="tableList"></ul>
        </div>
        <div id="content">
          <p>Chọn 1 bảng ở menu bên trái, hoặc nhập Tên bảng và / hoặc Tên cột rồi bấm Tìm kiếm.<br>
          Chỉ Tên bảng: hiện toàn bộ cấu trúc bảng. Cả hai: hiện dòng cột đó trong bảng. Chỉ Tên cột:
          tìm tất cả các bảng có cột đó, kết quả nhóm theo từng bảng.</p>
        </div>
        <script id="app-data" type="application/json">%%DATA_JSON%%</script>
        <script>
        (function () {
          var data = JSON.parse(document.getElementById('app-data').textContent);
          var tables = data.tables;

          // normalize: the generator leaves empty strings / lists out of the JSON to keep big schemas small -
          // put them back so the code below can call .toLowerCase() / .length on every field.
          var tableTextFields = ['alias', 'japaneseName', 'kind', 'note', 'description', 'managementType',
            'cautionItems', 'revisionHistory', 'sourceFile', 'sourceSheet'];
          var columnTextFields = ['dataType', 'length', 'nullable', 'defaultValue', 'japaneseName', 'description',
            'fullName', 'valueRestriction', 'meta'];
          tables.forEach(function (t) {
            tableTextFields.forEach(function (k) { if (t[k] == null) t[k] = ''; });
            t.columns = t.columns || [];
            t.columns.forEach(function (c) {
              columnTextFields.forEach(function (k) { if (c[k] == null) c[k] = ''; });
            });
            t.foreignKeys = t.foreignKeys || [];
            t.indexes = (t.indexes || []).map(function (i) {
              i.columns = i.columns || ''; i.indexType = i.indexType || ''; i.note = i.note || ''; return i;
            });
            t.constraints = (t.constraints || []).map(function (c) { c.definition = c.definition || ''; return c; });
          });
          var listEl = document.getElementById('tableList');
          var contentEl = document.getElementById('content');
          var searchBox = document.getElementById('searchBox');
          var columnBox = document.getElementById('columnBox');
          var searchButton = document.getElementById('searchButton');
          var summaryEl = document.getElementById('summary');

          var kindButtons = Array.prototype.slice.call(document.querySelectorAll('#kindFilter button'));
          var searchDescriptionBox = document.getElementById('searchDescription');

          // VIEW / MATERIALIZED VIEW / SYSTEM VIEW = view; BASE TABLE / FOREIGN TABLE = bảng.
          function isView(t) { return /VIEW/i.test(t.kind); }
          var viewCount = tables.filter(isView).length;
          var totalColumns = tables.reduce(function (sum, t) { return sum + t.columns.length; }, 0);
          summaryEl.textContent = 'Tổng: ' + (tables.length - viewCount) + ' bảng, ' + viewCount + ' view, ' + totalColumns +
            ' cột. (Xuất lúc ' + data.generatedAt + ')';
          kindButtons.forEach(function (b) {
            var n = b.dataset.kind === 'view' ? viewCount : b.dataset.kind === 'table' ? tables.length - viewCount : tables.length;
            b.textContent += ' (' + n + ')';
          });

          // Bộ lọc nhớ theo trình duyệt (localStorage có thể bị chặn với file:// - khi đó chỉ không nhớ).
          var kindFilter = 'all';
          try {
            kindFilter = localStorage.getItem('rdbms.kindFilter') || 'all';
            searchDescriptionBox.checked = localStorage.getItem('rdbms.searchDescription') === '1';
          } catch (e) { }
          function saveFilters() {
            try {
              localStorage.setItem('rdbms.kindFilter', kindFilter);
              localStorage.setItem('rdbms.searchDescription', searchDescriptionBox.checked ? '1' : '0');
            } catch (e) { }
          }
          function setKindFilter(kind) {
            kindFilter = kind;
            kindButtons.forEach(function (b) { b.classList.toggle('on', b.dataset.kind === kind); });
          }
          setKindFilter(kindFilter);

          function kindMatches(t) {
            return kindFilter === 'all' || (kindFilter === 'view') === isView(t);
          }

          var tablesByName = {};
          tables.forEach(function (t) { tablesByName[t.name] = t; });

          function tableNameMatches(t, query) {
            if (!kindMatches(t)) return false;
            if (!query) return true;
            return t.name.toLowerCase().indexOf(query) >= 0 || t.japaneseName.toLowerCase().indexOf(query) >= 0
              || (searchDescriptionBox.checked && t.description.toLowerCase().indexOf(query) >= 0);
          }

          function columnMatches(c, query) {
            return c.name.toLowerCase().indexOf(query) >= 0 || c.japaneseName.toLowerCase().indexOf(query) >= 0
              || (searchDescriptionBox.checked && c.description.toLowerCase().indexOf(query) >= 0);
          }

          // Tô chỗ khớp (không phân biệt hoa thường) - escape từng đoạn, chỉ <mark> là HTML.
          function highlight(text, query) {
            text = text || '';
            if (!query) return escapeHtml(text);
            var lower = text.toLowerCase(), html = '', pos = 0, i;
            while ((i = lower.indexOf(query, pos)) >= 0) {
              html += escapeHtml(text.slice(pos, i)) + '<mark>' + escapeHtml(text.slice(i, i + query.length)) + '</mark>';
              pos = i + query.length;
            }
            return html + escapeHtml(text.slice(pos));
          }

          function findListItem(name) {
            return Array.prototype.find.call(listEl.children, function (item) {
              return item.querySelector('.tname').textContent === name;
            });
          }

          function renderList() {
            var query = searchBox.value.trim().toLowerCase();
            listEl.innerHTML = '';
            tables.filter(function (t) { return tableNameMatches(t, query); }).forEach(function (t) {
              var li = document.createElement('li');
              // .tname chỉ chứa tên bảng (findListItem so textContent) - nhãn VIEW để ngoài.
              li.innerHTML = '<div><span class="tname"></span>' + (isView(t) ? '<span class="badge">' + escapeHtml(t.kind) + '</span>' : '') +
                '</div><div class="jname"></div>';
              li.querySelector('.tname').textContent = t.name;
              var byDescription = query && t.name.toLowerCase().indexOf(query) < 0 && t.japaneseName.toLowerCase().indexOf(query) < 0;
              if (byDescription) {
                li.querySelector('.jname').innerHTML = highlight(t.description, query); // khớp nhờ mô tả - cho thấy chỗ khớp
              } else {
                li.querySelector('.jname').textContent = t.japaneseName || t.alias || rowsText(t);
              }
              li.addEventListener('click', function () {
                var columnQuery = columnBox.value.trim().toLowerCase();
                if (columnQuery) {
                  showTableColumnMatches(t, li, columnQuery);
                } else {
                  showFullTable(t, li);
                }
              });
              listEl.appendChild(li);
            });
          }

          // Row count comes from the DB's statistics (no COUNT(*)), so it's labelled as an estimate.
          function rowsText(t) {
            return t.estimatedRows == null ? '' : '≈ ' + t.estimatedRows.toLocaleString('vi-VN') + ' dòng';
          }

          function renderIndexes(indexes) {
            if (!indexes || !indexes.length) return '';
            var rows = indexes.map(function (i) {
              var kind = i.isPrimaryKey ? 'Khoá chính' : (i.isUnique ? 'UNIQUE' : 'Thường');
              return '<tr' + (i.isPrimaryKey ? ' class="pk"' : '') + '>' +
                '<td>' + escapeHtml(i.name) + '</td>' +
                '<td>' + escapeHtml(i.columns) + '</td>' +
                '<td>' + kind + (i.indexType ? ' · ' + escapeHtml(i.indexType) : '') + '</td>' +
                '<td>' + escapeHtml(i.note) + '</td>' +
                '</tr>';
            }).join('');
            return '<div class="metaLabel" style="margin-top:16px">Index (' + indexes.length + ')</div>' +
              '<table class="cols"><thead><tr><th>Tên index</th><th>Cột</th><th>Loại</th><th>Ghi chú</th></tr></thead>' +
              '<tbody>' + rows + '</tbody></table>';
          }

          function renderConstraints(constraints) {
            if (!constraints || !constraints.length) return '';
            var rows = constraints.map(function (c) {
              return '<tr>' +
                '<td>' + escapeHtml(c.name) + '</td>' +
                '<td>' + escapeHtml(c.type) + '</td>' +
                '<td class="code">' + escapeHtml(c.definition) + '</td>' +
                '</tr>';
            }).join('');
            return '<div class="metaLabel" style="margin-top:16px">Ràng buộc UNIQUE / CHECK (' + constraints.length + ')</div>' +
              '<table class="cols"><thead><tr><th>Tên ràng buộc</th><th>Loại</th><th>Định nghĩa</th></tr></thead>' +
              '<tbody>' + rows + '</tbody></table>';
          }

          function renderSection(label, text) {
            if (!text) return '';
            return '<div class="meta"><div class="metaLabel">' + escapeHtml(label) + '</div>' + escapeHtml(text) + '</div>';
          }

          function legendHtml() {
            return '<div class="legend" style="margin-top:12px">' +
              '<span><span class="swatch pk"></span>Khoá chính (level 0)</span>' +
              '</div>';
          }

          // query: kết quả tìm cột - tô chỗ khớp ở tên cột / mô tả.
          function buildColumnsTableHtml(columns, query) {
            var html = '<table class="cols"><thead><tr>' +
              '<th>STT</th><th>Tên cột</th><th>Kiểu</th><th>Null</th>' +
              '<th>Mô tả</th></tr></thead><tbody>';
            columns.forEach(function (c, i) {
              var rowClasses = [];
              if (c.level === 0) rowClasses.push('pk');
              html += '<tr class="' + rowClasses.join(' ') + '">' +
                '<td>' + (i + 1) + '</td>' +
                '<td>' + highlight(c.name, query) + '</td>' +
                '<td>' + escapeHtml(c.dataType) + '</td>' +
                '<td>' + escapeHtml(c.nullable) + '</td>' +
                '<td>' + (query && searchDescriptionBox.checked ? highlight(c.description, query) : escapeHtml(c.description)) + '</td>' +
                '</tr>';
            });
            html += '</tbody></table>';
            return html;
          }

          function renderColumnGroups(headingHtml, groups, query) {
            var html = headingHtml + legendHtml();
            groups.forEach(function (g) {
              html += '<div class="groupBlock"><h2>' + escapeHtml(g.table.name) +
                (isView(g.table) ? '<span class="badge">' + escapeHtml(g.table.kind) + '</span>' : '') +
                (g.table.japaneseName ? ' - ' + escapeHtml(g.table.japaneseName) : '') + '</h2>' +
                buildColumnsTableHtml(g.columns, query) + '</div>';
            });
            contentEl.innerHTML = html;
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
            return '<div class="metaLabel" style="margin-top:12px">Khoá ngoại (FOREIGN)</div>' +
              '<table class="cols"><thead><tr><th>Cột của bảng này</th><th>Bảng tham chiếu</th><th>Cột tham chiếu</th></tr></thead>' +
              '<tbody>' + rows + '</tbody></table>';
          }

          // ----- Sơ đồ quan hệ: bảng đang xem + các bảng nối trực tiếp qua khoá ngoại (SVG tự vẽ, không thư viện) -----
          // Trái = bảng con (có khoá ngoại trỏ tới bảng này), giữa = bảng đang xem, phải = bảng cha (bảng này trỏ tới).
          // Mỗi hộp chỉ liệt kê cột khoá chính + cột tham gia quan hệ; đường nối từ cột FK tới cột được tham chiếu (mũi tên).
          var incomingByTable = {};
          tables.forEach(function (t) {
            t.foreignKeys.forEach(function (f) {
              (incomingByTable[f.referencedTable] = incomingByTable[f.referencedTable] || []).push({ table: t, fk: f });
            });
          });
          var ER_MAX_SIDE = 8, ER_ROW = 18, ER_HEAD = 24, ER_PAD = 8, ER_GAP_Y = 14, ER_GAP_X = 90, ER_CHAR = 7.2;

          function splitColumns(s) {
            return (s || '').split(',').map(function (x) { return x.trim(); }).filter(function (x) { return x; });
          }

          function renderRelationDiagram(t) {
            var outgoing = t.foreignKeys, incoming = incomingByTable[t.name] || [];
            if (!outgoing.length && !incoming.length) {
              return '<div class="meta" style="margin-top:12px">Sơ đồ quan hệ: không có khoá ngoại nối với ' +
                (isView(t) ? 'view' : 'bảng') + ' này.</div>';
            }

            var boxes = {}, order = [];
            function box(name, side) {
              if (!boxes[name]) {
                boxes[name] = { name: name, side: side, rows: [], table: tablesByName[name] };
                order.push(boxes[name]);
              }
              return boxes[name];
            }
            function addRow(b, column) { if (column && b.rows.indexOf(column) < 0) b.rows.push(column); }

            var center = box(t.name, 'center');
            t.columns.forEach(function (c) { if (c.level === 0) addRow(center, c.name); });
            var links = [];
            outgoing.forEach(function (f) {
              var local = splitColumns(f.localColumns), ref = splitColumns(f.referencedColumns || f.localColumns);
              var self = f.referencedTable === t.name;
              var target = self ? center : box(f.referencedTable, 'right');
              local.forEach(function (c) { addRow(center, c); });
              ref.forEach(function (c) { addRow(target, c); });
              links.push({ from: center, fromColumn: local[0], to: target, toColumn: ref[0], self: self,
                title: t.name + ' (' + local.join(', ') + ') → ' + f.referencedTable + ' (' + ref.join(', ') + ')' });
            });
            incoming.forEach(function (e) {
              if (e.table.name === t.name) return; // tự tham chiếu đã vẽ ở trên
              var local = splitColumns(e.fk.localColumns), ref = splitColumns(e.fk.referencedColumns || e.fk.localColumns);
              var source = boxes[e.table.name] || box(e.table.name, 'left'); // vừa cha vừa con → giữ ở bên phải
              local.forEach(function (c) { addRow(source, c); });
              ref.forEach(function (c) { addRow(center, c); });
              links.push({ from: source, fromColumn: local[0], to: center, toColumn: ref[0],
                title: e.table.name + ' (' + local.join(', ') + ') → ' + t.name + ' (' + ref.join(', ') + ')' });
            });

            function side(name) {
              return order.filter(function (b) { return b.side === name; })
                .sort(function (a, b) { return a.name.localeCompare(b.name); });
            }
            var left = side('left'), right = side('right');
            var hiddenLeft = Math.max(0, left.length - ER_MAX_SIDE), hiddenRight = Math.max(0, right.length - ER_MAX_SIDE);
            function hiddenNames(list) { return list.slice(ER_MAX_SIDE).map(function (b) { return b.name; }).join(', '); }
            var hiddenLeftNames = hiddenNames(left), hiddenRightNames = hiddenNames(right);
            left = left.slice(0, ER_MAX_SIDE);
            right = right.slice(0, ER_MAX_SIDE);
            var shown = [center].concat(left, right);
            links = links.filter(function (l) { return shown.indexOf(l.from) >= 0 && shown.indexOf(l.to) >= 0; });

            shown.forEach(function (b) {
              var longest = b.name.length * (ER_CHAR + 0.6);
              b.rows.forEach(function (r) { longest = Math.max(longest, r.length * ER_CHAR + 22); });
              b.w = Math.max(130, Math.ceil(longest + 2 * ER_PAD));
              b.h = ER_HEAD + Math.max(1, b.rows.length) * ER_ROW + 6;
            });
            function stackHeight(list, hidden) {
              return list.reduce(function (s, b) { return s + b.h; }, 0) + Math.max(0, list.length - 1) * ER_GAP_Y + (hidden ? 22 : 0);
            }
            var maxWidth = function (list) { return list.reduce(function (m, b) { return Math.max(m, b.w); }, 0); };
            var leftW = maxWidth(left), rightW = maxWidth(right);
            var hasSelf = links.some(function (l) { return l.self; });
            var xCenter = left.length ? leftW + ER_GAP_X : 0;
            var xRight = xCenter + center.w + ER_GAP_X;
            var height = Math.max(center.h, stackHeight(left, hiddenLeft), stackHeight(right, hiddenRight)) + 8;
            var width = (right.length ? xRight + rightW : xCenter + center.w) + (hasSelf ? 60 : 0) + 4;
            if (hiddenRight) width = Math.max(width, xRight + 300); // chỗ cho dòng "+N bảng cha khác"

            // Xếp dọc, căn giữa theo chiều cao; cột trái căn phải (sát bảng giữa), cột phải căn trái. Trả y sau hộp cuối.
            function place(list, x, alignRightTo, hidden) {
              var y = 4 + (height - 8 - stackHeight(list, hidden)) / 2;
              list.forEach(function (b) { b.x = alignRightTo ? alignRightTo - b.w : x; b.y = y; y += b.h + ER_GAP_Y; });
              return y;
            }
            var notes = '';
            var yLeftEnd = place(left, 0, leftW, hiddenLeft);
            // "+N bảng khác": rê chuột xem tên (bảng ẩn vẫn có trong mục Khoá ngoại / mở được từ menu trái).
            if (hiddenLeft) notes += '<text class="er-note" x="0" y="' + (yLeftEnd + 6) + '"><title>' + escapeHtml(hiddenLeftNames) +
              '</title>+ ' + hiddenLeft + ' bảng con khác (rê chuột để xem tên)</text>';
            center.x = xCenter; center.y = 4 + (height - 8 - center.h) / 2;
            var yRightEnd = place(right, xRight, 0, hiddenRight);
            if (hiddenRight) notes += '<text class="er-note" x="' + xRight + '" y="' + (yRightEnd + 6) + '"><title>' + escapeHtml(hiddenRightNames) +
              '</title>+ ' + hiddenRight + ' bảng cha khác (rê chuột để xem tên)</text>';

            function rowY(b, column) {
              var i = b.rows.indexOf(column);
              return i < 0 ? b.y + ER_HEAD / 2 : b.y + ER_HEAD + i * ER_ROW + ER_ROW / 2 + 2;
            }
            function isPk(b, column) {
              return !!b.table && b.table.columns.some(function (c) { return c.level === 0 && c.name === column; });
            }
            function isFk(b, column) {
              return !!b.table && b.table.foreignKeys.some(function (f) { return splitColumns(f.localColumns).indexOf(column) >= 0; });
            }

            var svg = '<svg class="er" xmlns="http://www.w3.org/2000/svg" width="' + Math.ceil(width) + '" height="' + Math.ceil(height) +
              '" viewBox="0 0 ' + Math.ceil(width) + ' ' + Math.ceil(height) + '" role="img" aria-label="Sơ đồ quan hệ của ' + escapeHtml(t.name) + '">' +
              '<defs><marker id="erArrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto">' +
              '<path d="M0,0 L10,5 L0,10 z" fill="#4a90d9"/></marker></defs>';

            links.forEach(function (l) {
              var y1 = rowY(l.from, l.fromColumn), y2 = rowY(l.to, l.toColumn), x1, x2, d;
              if (l.self) {
                x1 = x2 = center.x + center.w;
                if (Math.abs(y1 - y2) < 4) { y1 -= 5; y2 += 5; }
                d = 'M' + x1 + ',' + y1 + ' C' + (x1 + 55) + ',' + y1 + ' ' + (x2 + 55) + ',' + y2 + ' ' + x2 + ',' + y2;
              } else {
                var forward = l.from.x < l.to.x;
                x1 = forward ? l.from.x + l.from.w : l.from.x;
                x2 = forward ? l.to.x : l.to.x + l.to.w;
                var dx = (forward ? 1 : -1) * Math.max(30, Math.abs(x2 - x1) / 2);
                d = 'M' + x1 + ',' + y1 + ' C' + (x1 + dx) + ',' + y1 + ' ' + (x2 - dx) + ',' + y2 + ' ' + x2 + ',' + y2;
              }
              svg += '<path class="er-link" d="' + d + '" marker-end="url(#erArrow)"><title>' + escapeHtml(l.title) + '</title></path>' +
                '<circle class="er-dot" cx="' + x1 + '" cy="' + y1 + '" r="2.5"/>';
            });

            shown.forEach(function (b) {
              var cls = 'er-box' + (b === center ? ' center' : '') + (!b.table ? ' missing' : '') + (b !== center && b.table ? ' link' : '');
              var tip = b === center ? '' : !b.table ? b.name + ' - không có trong file này (schema khác / đã lọc khi đọc)' : 'Bấm để mở ' + b.name;
              svg += '<g class="' + cls + '"' + (b !== center && b.table ? ' data-table="' + escapeHtml(b.name) + '"' : '') + '>' +
                (tip ? '<title>' + escapeHtml(tip) + '</title>' : '') +
                '<rect class="er-frame" x="' + b.x + '" y="' + b.y + '" width="' + b.w + '" height="' + b.h + '" rx="6"/>' +
                '<rect class="er-head" x="' + (b.x + 1) + '" y="' + (b.y + 1) + '" width="' + (b.w - 2) + '" height="' + (ER_HEAD - 1) + '" rx="5"/>' +
                '<text class="er-title" x="' + (b.x + ER_PAD) + '" y="' + (b.y + 16) + '">' + escapeHtml(b.name) + '</text>';
              b.rows.forEach(function (r, i) {
                var y = b.y + ER_HEAD + i * ER_ROW + 15;
                var tag = isPk(b, r) ? '<text class="er-tag" x="' + (b.x + ER_PAD) + '" y="' + y + '">PK</text>'
                  : isFk(b, r) ? '<text class="er-tag fk" x="' + (b.x + ER_PAD) + '" y="' + y + '">FK</text>' : '';
                svg += tag + '<text x="' + (b.x + ER_PAD + 20) + '" y="' + y + '">' + escapeHtml(r) + '</text>';
              });
              svg += '</g>';
            });
            svg += notes + '</svg>';

            var count = shown.length - 1 + hiddenLeft + hiddenRight;
            return '<details class="er-block" open><summary>Sơ đồ quan hệ (' + count + ' bảng nối trực tiếp)</summary>' +
              '<div class="er-hint">Trái: bảng có khoá ngoại trỏ tới ' + escapeHtml(t.name) + ' · Phải: bảng mà ' + escapeHtml(t.name) +
              ' trỏ tới · Mũi tên chỉ về cột được tham chiếu · Bấm 1 bảng để mở · Rê chuột lên đường nối để xem cột</div>' +
              '<div class="er-wrap">' + svg + '</div></details>';
          }

          // Bấm hộp bảng trong sơ đồ → mở bảng đó ngay trong trang (qua #table=..., nút Back của trình duyệt quay lại được).
          function bindRelationDiagram() {
            Array.prototype.forEach.call(contentEl.querySelectorAll('.er-box[data-table]'), function (g) {
              g.addEventListener('click', function () { location.hash = '#table=' + encodeURIComponent(g.getAttribute('data-table')); });
            });
          }

          function applyHash() {
            var match = /^#table=(.+)$/.exec(location.hash);
            if (match) {
              var name = decodeURIComponent(match[1]);
              columnBox.value = '';
              searchBox.value = name;
              if (tablesByName[name] && !kindMatches(tablesByName[name])) setKindFilter('all'); // link khoá ngoại tới bảng đang bị lọc
              renderList();
              showFullTable(tablesByName[name], findListItem(name));
            }
          }

          function renderColumnsTable(t) {
            var html = '<h1>' + escapeHtml(t.name) + (t.alias ? ' (' + escapeHtml(t.alias) + ')' : '') + '</h1>';
            html += '<div class="meta">' + escapeHtml(t.japaneseName) + (t.kind ? ' - ' + escapeHtml(t.kind) : '') + '\n';
            html += 'Nguồn: ' + escapeHtml(t.sourceFile) + ' / ' + escapeHtml(t.sourceSheet);
            if (!data.extendedInfo) {
              // .db read by an older version - no row counts / indexes / constraints were collected.
              html += '\n(Dữ liệu đọc bằng bản cũ: bấm lại "1. Đọc database" để có số dòng, index, ràng buộc UNIQUE / CHECK)';
            } else if (t.estimatedRows != null) {
              html += '\nSố dòng (ước tính theo thống kê của DB): ' + t.estimatedRows.toLocaleString('vi-VN');
            } else if (t.kind !== 'VIEW') {
              html += '\nSố dòng: chưa có thống kê (ANALYZE / thu thập thống kê trên DB rồi đọc lại)';
            }
            html += '</div>';
            html += renderSection('Mô tả (説明)', t.description);
            html += renderSection('Loại quản lý (管理タイプ)', t.managementType);
            html += renderSection('Mục cần lưu ý khi thay đổi (運用後の変更に注意が必要な項目)', t.cautionItems);
            html += renderSection('Cải cách / bãi bỏ (改廃)', t.revisionHistory);
            html += renderRelationDiagram(t);
            html += renderForeignKeys(t.foreignKeys);
            html += legendHtml();
            html += buildColumnsTableHtml(t.columns);
            html += renderIndexes(t.indexes);
            html += renderConstraints(t.constraints);
            contentEl.innerHTML = html;
            bindRelationDiagram();
            contentEl.scrollTop = 0;
          }

          function setActiveListItem(li) {
            var current = listEl.querySelector('li.active');
            if (current) current.classList.remove('active');
            if (li) li.classList.add('active');
          }

          // Dieu kien 1: chi Ten bang (Ten cot de trong) -> hien toan bo cau truc bang.
          function showFullTable(t, li) {
            if (!t) return;
            setActiveListItem(li);
            renderColumnsTable(t);
            document.title = 'CSDL - ' + t.name;
          }

          // Dieu kien 2: ca Ten bang va Ten cot -> chi hien dong cot do trong bang nay.
          function showTableColumnMatches(t, li, columnQuery) {
            setActiveListItem(li);
            var matchedCols = t.columns.filter(function (c) { return columnMatches(c, columnQuery); });
            var heading = '<h1>' + escapeHtml(t.name) + (t.alias ? ' (' + escapeHtml(t.alias) + ')' : '') + '</h1>' +
              '<div class="meta">Cột khớp "' + escapeHtml(columnQuery) + '": ' + matchedCols.length + ' / ' + t.columns.length + '</div>';
            renderColumnGroups(heading, [{ table: t, columns: matchedCols }], columnQuery);
            document.title = 'CSDL - ' + t.name + ' (cột: ' + columnQuery + ')';
          }

          // Dieu kien 3: chi Ten cot (Ten bang de trong) -> tim tat ca bang co cot do, nhom theo bang.
          function showColumnSearchAcrossTables(candidateTables, columnQuery) {
            setActiveListItem(null);
            var groups = [];
            candidateTables.forEach(function (t) {
              var matchedCols = t.columns.filter(function (c) { return columnMatches(c, columnQuery); });
              if (matchedCols.length) groups.push({ table: t, columns: matchedCols });
            });
            if (!groups.length) {
              contentEl.innerHTML = '<p>Không tìm thấy cột nào khớp "' + escapeHtml(columnQuery) + '".</p>';
              document.title = 'CSDL';
              return;
            }
            var heading = '<h1>Kết quả tìm cột "' + escapeHtml(columnQuery) + '"</h1>' +
              '<div class="meta">' + groups.length + ' bảng có cột khớp.</div>';
            renderColumnGroups(heading, groups, columnQuery);
            document.title = 'CSDL - cột ' + columnQuery + ' (' + groups.length + ' bảng)';
          }

          function performSearch() {
            renderList();

            var tableQuery = searchBox.value.trim().toLowerCase();
            var columnQuery = columnBox.value.trim().toLowerCase();

            if (!columnQuery) {
              if (!tableQuery) return;
              var candidates = tables.filter(function (t) { return tableNameMatches(t, tableQuery); });
              var exact = candidates.find(function (t) { return t.name.toLowerCase() === tableQuery; });
              var target = exact || (candidates.length === 1 ? candidates[0] : null);
              if (target) showFullTable(target, findListItem(target.name));
              return;
            }

            // tableNameMatches cũng lọc theo Bảng / View (kể cả khi ô Tên bảng trống).
            var candidateTables = tables.filter(function (t) { return tableNameMatches(t, tableQuery); });
            if (candidateTables.length === 1) {
              showTableColumnMatches(candidateTables[0], findListItem(candidateTables[0].name), columnQuery);
            } else {
              showColumnSearchAcrossTables(candidateTables, columnQuery);
            }
          }

          function escapeHtml(s) {
            return (s || '').replace(/[&<>"]/g, function (ch) {
              return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[ch];
            });
          }

          window.addEventListener('hashchange', applyHash);

          searchButton.addEventListener('click', performSearch);
          searchBox.addEventListener('keyup', function (e) { if (e.key === 'Enter') performSearch(); });
          searchBox.addEventListener('input', performSearch);
          columnBox.addEventListener('keyup', function (e) { if (e.key === 'Enter') performSearch(); });
          columnBox.addEventListener('input', performSearch);
          kindButtons.forEach(function (b) {
            b.addEventListener('click', function () { setKindFilter(b.dataset.kind); saveFilters(); performSearch(); });
          });
          searchDescriptionBox.addEventListener('change', function () { saveFilters(); performSearch(); });

          performSearch();
          applyHash();
        })();
        </script>
        </body>
        </html>
        """;
}
