using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Mcf.CrudDiagram.HtmlGenerator.Models;

namespace Mcf.CrudDiagram.HtmlGenerator.Services;

/// <summary>
/// Renders Data\Database\Html\02_CRUD図.html: a lightweight manifest (code/name/module/doc/searchText
/// per logic - no sheet HTML) embedded inline as JSON, exactly like Mcf.Screen.HtmlGenerator's index. Embedded rather
/// than fetched at runtime for the same reason as Mcf.Screen.HtmlGenerator: opening a local file via file:// and
/// calling fetch()/XHR on another local file is blocked by CORS in Chromium-based browsers.
/// A manifest.json is also written alongside as a plain byproduct for external tooling/debugging,
/// but 02_CRUD図.html itself never reads it.
/// </summary>
public sealed class HtmlIndexGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Generate(CrudDatabase database, string htmlOutputDir)
    {
        var logics = database.GetAllLogics();
        Directory.CreateDirectory(htmlOutputDir);

        var payload = logics.Select(l => new
        {
            code = l.ScreenCode,
            name = l.ScreenName,
            moduleId = l.ModuleId,
            moduleName = l.ModuleName,
            subModuleId = l.SubModuleId,
            subModuleName = l.SubModuleName,
            docNumber = l.DocNumber,
            searchText = l.SearchText,
            htmlFile = l.HtmlFileName,
            blockCount = l.BlockCount,
        });

        var json = JsonSerializer.Serialize(payload, JsonOptions).Replace("</", "<\\/", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(htmlOutputDir, "manifest.json"), json);

        var html = IndexPageTemplate
            .Replace("%%GENERATED_AT%%", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.Ordinal)
            .Replace("%%DATA_JSON%%", json, StringComparison.Ordinal);

        var indexPath = Path.Combine(htmlOutputDir, "02_CRUD図.html");
        File.WriteAllText(indexPath, html);
        return indexPath;
    }

    private const string IndexPageTemplate =
        """
        <!doctype html>
        <html lang="vi">
        <head>
        <meta charset="utf-8">
        <title>CRUD図</title>
        <style>
          :root { color-scheme: light dark; --accent: #2b6cb0; --accent-soft: #4a90d91a; }
          * { box-sizing: border-box; }
          body { margin: 0; font-family: "Segoe UI", system-ui, sans-serif; display: flex; height: 100vh; }
          #sidebar { width: 400px; min-width: 320px; border-right: 1px solid #8884; display: flex; flex-direction: column; }
          #searchArea { padding: 12px; display: flex; flex-direction: column; gap: 8px; border-bottom: 1px solid #8884; }
          #searchArea input {
            width: 100%; padding: 8px 10px; font-size: 14px; border: 1px solid #8886; border-radius: 8px;
            background: Canvas; color: inherit;
          }
          #searchArea input:focus { outline: 2px solid var(--accent-soft); border-color: var(--accent); }
          #summary { padding: 8px 12px; font-size: 12px; opacity: .7; }
          #logicList { list-style: none; margin: 0; padding: 6px; overflow-y: auto; flex: 1; }
          #logicList li { padding: 0; margin-bottom: 4px; }
          #logicList a {
            display: block; padding: 8px 12px; color: inherit; text-decoration: none; border-radius: 8px;
            border-left: 3px solid transparent; transition: background .12s ease, border-color .12s ease;
          }
          #logicList a:hover { background: var(--accent-soft); border-left-color: var(--accent); }
          #logicList a.active { background: var(--accent-soft); border-left-color: var(--accent); font-weight: 600; }
          #logicList .row1 { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; }
          #logicList .code { font-weight: 600; font-family: Consolas, monospace; font-size: 13px; }
          #logicList .badge {
            font-size: 11px; padding: 1px 8px; border-radius: 10px; background: #8882; opacity: .8; white-space: nowrap;
          }
          #logicList .name { font-size: 12px; opacity: .75; margin-top: 2px; }
          #logicList .meta { font-size: 11px; opacity: .55; margin-top: 3px; }
          #empty { padding: 24px 12px; text-align: center; font-size: 13px; opacity: .6; display: none; }
          #content { flex: 1; display: flex; flex-direction: column; min-width: 0; }
          #contentPlaceholder { padding: 32px; overflow-y: auto; }
          #contentPlaceholder h2 { font-size: 18px; margin-top: 0; }
          #contentPlaceholder .hint { font-size: 13px; opacity: .75; line-height: 1.7; }
          #contentFrame { flex: 1; width: 100%; border: none; display: none; }
        </style>
        </head>
        <body>
        <div id="sidebar">
          <div id="searchArea">
            <input id="codeBox" type="text" placeholder="Ma logic / ten..." />
            <input id="contentBox" type="text" placeholder="Tim theo ten bang/object (使用オブジェクト)..." />
          </div>
          <div id="summary"></div>
          <ul id="logicList"></ul>
          <div id="empty">Khong tim thay logic nao khop.</div>
        </div>
        <div id="content">
          <div id="contentPlaceholder">
            <h2>CRUD図</h2>
            <p class="hint">Chon 1 logic o menu ben trai de xem ngay tai day, hoac loc bang 2 o tim kiem
            phia tren - ca hai dieu kien duoc ket hop <b>AND</b>. O thu 2 tim theo toan bo noi dung sheet,
            bao gom ten bang/object (使用オブジェクト) - dung de tra "bang X duoc logic nao dung".</p>
            <p class="hint">Xuat luc %%GENERATED_AT%%.</p>
          </div>
          <iframe id="contentFrame" name="contentFrame"></iframe>
        </div>
        <script id="app-data" type="application/json">%%DATA_JSON%%</script>
        <script>
        (function () {
          var logics = JSON.parse(document.getElementById('app-data').textContent);
          var listEl = document.getElementById('logicList');
          var emptyEl = document.getElementById('empty');
          var codeBox = document.getElementById('codeBox');
          var contentBox = document.getElementById('contentBox');
          var summaryEl = document.getElementById('summary');
          var placeholderEl = document.getElementById('contentPlaceholder');
          var frameEl = document.getElementById('contentFrame');
          var activeLink = null;

          function openLogic(link) {
            if (activeLink) activeLink.classList.remove('active');
            link.classList.add('active');
            activeLink = link;
            placeholderEl.style.display = 'none';
            frameEl.style.display = 'block';
          }

          summaryEl.textContent = 'Tong: ' + logics.length + ' logic.';

          function matches(logic, codeQuery, contentQuery) {
            if (codeQuery) {
              var hit = logic.code.toLowerCase().indexOf(codeQuery) >= 0 ||
                logic.name.toLowerCase().indexOf(codeQuery) >= 0;
              if (!hit) return false;
            }
            if (contentQuery) {
              if ((logic.searchText || '').toLowerCase().indexOf(contentQuery) < 0) return false;
            }
            return true;
          }

          function render() {
            var codeQuery = codeBox.value.trim().toLowerCase();
            var contentQuery = contentBox.value.trim().toLowerCase();
            var filtered = logics.filter(function (l) { return matches(l, codeQuery, contentQuery); });

            listEl.innerHTML = '';
            emptyEl.style.display = filtered.length === 0 ? 'block' : 'none';
            filtered.forEach(function (l) {
              var li = document.createElement('li');
              var a = document.createElement('a');
              a.href = l.htmlFile;
              a.target = 'contentFrame';
              a.innerHTML =
                '<div class="row1"><span class="code"></span><span class="badge"></span></div>' +
                '<div class="name"></div><div class="meta"></div>';
              a.querySelector('.code').textContent = l.code;
              a.querySelector('.badge').textContent = (l.blockCount || 0) + ' block';
              a.querySelector('.name').textContent = l.name;
              a.querySelector('.meta').textContent =
                [l.moduleName, l.subModuleName, l.docNumber].filter(Boolean).join(' | ');
              a.addEventListener('click', function () { openLogic(a); });
              li.appendChild(a);
              listEl.appendChild(li);
            });

            summaryEl.textContent = 'Hien ' + filtered.length + ' / ' + logics.length + ' logic.';
          }

          codeBox.addEventListener('input', render);
          contentBox.addEventListener('input', render);

          render();
        })();
        </script>
        </body>
        </html>
        """;
}
