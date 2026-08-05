namespace ModuleG.Services;

/// <summary>Shared HTML string helpers/templates for both the per-screen pages
/// (<see cref="ScreenDocImporter"/>) and the index page (<see cref="HtmlIndexGenerator"/>).</summary>
internal static class HtmlTemplates
{
    public static string Escape(string value) =>
        (value ?? string.Empty).Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    public const string SheetGridCss =
        """
        .table-scroll {
          overflow-x: auto; margin-bottom: 14px; border-radius: 8px;
          border: 1px solid #8884; box-shadow: 0 1px 3px #0002;
        }
        .sheet-grid { border-collapse: collapse; width: 100%; table-layout: fixed; }
        .sheet-grid td { border: 1px solid #8887; padding: 5px 8px; vertical-align: top; font-size: 13px; line-height: 1.5; word-wrap: break-word; }
        .sheet-grid tr:hover td { background-color: #4a90d914; }
        .sheet-image { margin: 14px 0; }
        .sheet-image img { max-width: 100%; border: 1px solid #8886; border-radius: 4px; box-shadow: 0 1px 4px #0002; }

        .semantic-doc { display: flex; flex-direction: column; gap: 22px; }
        .ov-section h3 {
          font-size: 14px; margin: 0 0 10px; padding: 4px 10px; border-radius: 6px;
          background: var(--accent-soft); color: var(--accent); display: inline-block;
        }
        .ov-freetext { font-size: 13px; }
        .ov-freetext .ov-line { margin: 2px 0; }
        .ov-table, .ov-kv { border-collapse: collapse; width: 100%; font-size: 13px; }
        .ov-table th, .ov-table td, .ov-kv th, .ov-kv td { border: 1px solid #8886; padding: 6px 10px; vertical-align: top; text-align: left; }
        .ov-table th { background: #4a90d926; font-weight: 600; white-space: nowrap; }
        .ov-table tbody tr:hover td { background-color: #4a90d914; }
        .ov-kv th { background: #4a90d926; font-weight: 600; width: 180px; white-space: nowrap; }
        .diag-block { padding: 14px 16px; border: 1px solid #8884; border-radius: 8px; margin-bottom: 14px; }
        .diag-head { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 10px; }
        .diag-head .chip { font-size: 12px; padding: 3px 10px; border-radius: 12px; background: #8882; }
        .diag-detail { margin: 10px 0 10px 16px; padding-left: 12px; border-left: 3px solid var(--accent-soft); }
        .diag-detail .ov-kv, .diag-detail .ov-table { margin-bottom: 8px; }

        .item-table thead th { background: #c6e0b4; color: #375623; }
        .item-group-row td {
          background: #a9d18e; color: #274a12; font-weight: 700; padding: 7px 10px;
          border-top: 2px solid #8886;
        }
        @media (prefers-color-scheme: dark) {
          .item-table thead th { background: #385723; color: #d3e7c5; }
          .item-group-row td { background: #4a7332; color: #eaf5e0; }
        }
        """;

    public static readonly string ScreenPage =
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
          .page { max-width: 1600px; margin: 0 auto; padding: 20px 28px 80px; }
          header { margin-bottom: 4px; }
          header .back-link {
            font-size: 13px; text-decoration: none; opacity: .8; display: inline-flex; align-items: center; gap: 4px;
          }
          header .back-link:hover { text-decoration: underline; }
          h1 { font-size: 22px; margin: 10px 0 10px; }
          .doc-meta { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 16px; }
          .doc-meta .chip {
            font-size: 12px; padding: 3px 10px; border-radius: 12px; background: #8882; opacity: .85;
          }
          nav.toc {
            position: sticky; top: 0; z-index: 10; background: Canvas; padding: 10px 0;
            border-bottom: 1px solid #8884; margin-bottom: 22px; display: flex; flex-wrap: wrap; gap: 6px 10px;
          }
          nav.toc a {
            font-size: 13px; text-decoration: none; padding: 4px 12px; border-radius: 14px;
            background: var(--accent-soft); color: var(--accent); transition: background .15s ease;
          }
          nav.toc a:hover { background: #4a90d933; }
          section.sheet { margin-bottom: 36px; scroll-margin-top: 56px; }
          section.sheet h2 {
            font-size: 15px; border-left: 4px solid var(--accent); padding-left: 10px; margin: 0 0 12px;
            display: flex; align-items: center; gap: 8px;
          }
          %%SHEET_GRID_CSS%%
          .back-to-top {
            position: fixed; right: 24px; bottom: 24px; font-size: 12px; text-decoration: none;
            padding: 8px 14px; border-radius: 20px; background: var(--accent); color: #fff;
            box-shadow: 0 2px 8px #0004;
          }
          @media (prefers-color-scheme: dark) {
            :root { --accent: #7db6f2; --accent-soft: #4a90d92e; }
          }
        </style>
        </head>
        <body>
        <div class="page">
        <header>
          <a class="back-link" href="01_画面説明書.html" target="_parent">&larr; Quay lai danh sach</a>
          <h1>%%TITLE%%</h1>
          <div class="doc-meta">
            <span class="chip">Van ban: %%DOC_NUMBER%%</span>
            <span class="chip">Revision: %%REVISION%%</span>
            <span class="chip">Nguon: %%SOURCE_FILE%%</span>
          </div>
        </header>
        <nav class="toc" id="sheet-toc">%%NAV%%</nav>
        %%SECTIONS%%
        <a class="back-to-top" href="#sheet-toc">&uarr; Len dau trang</a>
        </div>
        </body>
        </html>
        """.Replace("%%SHEET_GRID_CSS%%", SheetGridCss, StringComparison.Ordinal);
}
