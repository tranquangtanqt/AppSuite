namespace ModuleH.Services;

/// <summary>Shared HTML string helpers/templates for both the per-logic pages
/// (<see cref="CrudDocImporter"/>) and the index page (<see cref="HtmlIndexGenerator"/>).</summary>
internal static class HtmlTemplates
{
    public static string Escape(string value) =>
        (value ?? string.Empty).Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    public const string CrudGridCss =
        """
        .crud-table { border-collapse: collapse; width: 100%; font-size: 13px; }
        .crud-table th, .crud-table td { border: 1px solid #8886; padding: 5px 10px; vertical-align: top; text-align: left; }
        .crud-table th { background: #c6e0b4; color: #375623; font-weight: 600; white-space: nowrap; }
        .crud-table tbody tr:hover td { background-color: #4a90d914; }
        .crud-table td.flag { text-align: center; font-weight: 600; }
        .crud-table tr.crud-block-row td:first-child, .crud-table tr.crud-block-row td:nth-child(2) { font-weight: 600; }
        .crud-table tr.crud-block-row { scroll-margin-top: 12px; }
        .crud-table a { color: var(--accent); text-decoration: none; }
        .crud-table a:hover { text-decoration: underline; }
        .crud-empty { padding: 10px 14px; font-size: 13px; opacity: .6; }
        @media (prefers-color-scheme: dark) {
          .crud-table th { background: #385723; color: #d3e7c5; }
        }
        .table-scroll {
          overflow-x: auto; margin-bottom: 14px; border-radius: 8px;
          border: 1px solid #8884; box-shadow: 0 1px 3px #0002;
        }
        .sheet-grid { border-collapse: collapse; width: 100%; table-layout: fixed; }
        .sheet-grid td { border: 1px solid #8887; padding: 5px 8px; vertical-align: top; font-size: 13px; line-height: 1.5; word-wrap: break-word; }
        """;

    public static readonly string LogicPage =
        """
        <!doctype html>
        <html lang="ja">
        <head>
        <meta charset="utf-8">
        <title>%%TITLE%%</title>
        <style>
          :root { color-scheme: light dark; --accent: #2b6cb0; --accent-soft: #4a90d91a; }
          * { box-sizing: border-box; }
          body {
            margin: 0; font-family: "Segoe UI", "Yu Gothic UI", system-ui, sans-serif;
            line-height: 1.6;
          }
          .page { max-width: 1400px; margin: 0 auto; padding: 20px 28px 80px; }
          header { margin-bottom: 4px; }
          header .back-link {
            font-size: 13px; text-decoration: none; opacity: .8; display: inline-flex; align-items: center; gap: 4px;
          }
          header .back-link:hover { text-decoration: underline; }
          h1 { font-size: 22px; margin: 10px 0 10px; }
          .doc-meta { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 20px; }
          .doc-meta .chip {
            font-size: 12px; padding: 3px 10px; border-radius: 12px; background: #8882; opacity: .85;
          }
          %%CRUD_GRID_CSS%%
          @media (prefers-color-scheme: dark) {
            :root { --accent: #7db6f2; --accent-soft: #4a90d92e; }
          }
        </style>
        </head>
        <body>
        <div class="page">
        <header>
          <a class="back-link" href="02_CRUD図.html" target="_parent">&larr; Quay lai danh sach</a>
          <h1>%%TITLE%%</h1>
          <div class="doc-meta">
            <span class="chip">Module: %%MODULE_ID%% - %%MODULE_NAME%%</span>
            <span class="chip">Sub-module: %%SUBMODULE_ID%% - %%SUBMODULE_NAME%%</span>
            <span class="chip">Van ban: %%DOC_NUMBER%%</span>
            <span class="chip">Version: %%VERSION%%</span>
            <span class="chip">Rev: %%REVISION%%</span>
            <span class="chip">Nguon: %%SOURCE_FILE%%</span>
          </div>
        </header>
        %%CONTENT%%
        </div>
        </body>
        </html>
        """.Replace("%%CRUD_GRID_CSS%%", CrudGridCss, StringComparison.Ordinal);
}
