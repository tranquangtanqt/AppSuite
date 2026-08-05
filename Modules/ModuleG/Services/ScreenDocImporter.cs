using System.Text;
using ModuleG.Models;
using OfficeOpenXml;

namespace ModuleG.Services;

/// <summary>
/// Walks every "画面説明書" *.xlsx under a root folder, renders each workbook's sheets to HTML via
/// <see cref="ExcelSheetHtmlRenderer"/>, writes the per-screen HTML file + any embedded images to
/// disk immediately (they're static content that never needs to round-trip through SQLite), and
/// returns the lightweight metadata that does belong in the DB/search index.
/// </summary>
public sealed class ScreenDocImporter
{
    /// <summary>Best-effort, one file at a time: a single corrupt/locked workbook is logged and
    /// skipped rather than aborting the whole batch (617 files - some outlier is expected).</summary>
    public List<ScreenRecord> ImportDirectory(string rootFolder, string htmlOutputDir, Action<string> log)
    {
        var records = new List<ScreenRecord>();
        if (!Directory.Exists(rootFolder))
        {
            log($"Khong tim thay thu muc: {rootFolder}");
            return records;
        }

        var files = Directory.EnumerateFiles(rootFolder, "*.xlsx", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal)) // Excel lock files
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        log($"Tim thay {files.Count} file .xlsx.");
        Directory.CreateDirectory(htmlOutputDir);

        // One Excel COM instance for the whole batch (see ExcelDiagramCapture) - rasterizes the
        // 処理関連図/サービス関連図 diagram on the 概要 sheet, which EPPlus can't read (floating
        // shapes/connectors, not an embedded picture or plain cell grid). Best-effort: if Excel isn't
        // installed on this machine, every file just falls back to the plain grid render for that
        // section instead - the whole import never fails because of this.
        using var diagramCapture = new ExcelDiagramCapture();
        if (diagramCapture.TryStart(out var startError))
        {
            log("Excel COM san sang - se chup anh so do (処理関連図/サービス関連図).");
        }
        else
        {
            log($"Khong khoi dong duoc Excel ({startError}) - so do se hien thi dang bang thay vi anh.");
        }

        var seenHtmlFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processed = 0;

        foreach (var file in files)
        {
            processed++;
            try
            {
                var record = ImportOneFile(file, htmlOutputDir, seenHtmlFileNames, diagramCapture, log);
                records.Add(record);
                if (processed % 25 == 0 || processed == files.Count)
                {
                    log($"[{processed}/{files.Count}] {record.ScreenCode} - {record.ScreenName}");
                }
            }
            catch (Exception ex)
            {
                log($"[{processed}/{files.Count}] LOI '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        return records;
    }

    /// <summary>Sheets skipped entirely - not rendered as a section, no nav entry, no contribution to
    /// search text. "変更来歴" (revision history) is administrative bookkeeping (who changed what row,
    /// when) that isn't useful to read when trying to understand a screen's behaviour; "表紙" (cover
    /// page) only repeats the mcframe/module/document-number metadata already shown in the page's own
    /// header chips (see <see cref="HtmlTemplates.ScreenPage"/>).</summary>
    private static readonly HashSet<string> SkippedSheetNames = new() { "表紙", "変更来歴" };

    private static ScreenRecord ImportOneFile(
        string file, string htmlOutputDir, HashSet<string> seenHtmlFileNames, ExcelDiagramCapture diagramCapture, Action<string> log)
    {
        var parsed = ScreenCodeParser.Parse(Path.GetFileNameWithoutExtension(file));
        var htmlFileName = MakeUniqueHtmlFileName(parsed.ScreenCode, seenHtmlFileNames);
        var imagesDirName = Path.GetFileNameWithoutExtension(htmlFileName);
        var imagesOutputDir = Path.Combine(htmlOutputDir, "Images", imagesDirName);
        var imagesRelativeUrl = $"Images/{imagesDirName}";

        var searchText = new StringBuilder();
        var sections = new StringBuilder();
        var nav = new StringBuilder();
        var sheetIndex = 0;

        using (var package = new ExcelPackage(new FileInfo(file)))
        {
            foreach (var sheet in package.Workbook.Worksheets)
            {
                if (sheet.Dimension is null || SkippedSheetNames.Contains(sheet.Name))
                {
                    continue;
                }

                sheetIndex++;
                var anchorId = $"sheet-{sheetIndex}";
                var sheetHtml = ExcelSheetHtmlRenderer.RenderSheet(sheet, imagesOutputDir, imagesRelativeUrl, searchText, diagramCapture, file, log);
                sections.Append("<section class=\"sheet\" id=\"").Append(anchorId).Append("\"><h2>")
                    .Append(HtmlTemplates.Escape(sheet.Name)).Append("</h2>")
                    .Append(sheetHtml).Append("</section>");
                nav.Append("<a href=\"#").Append(anchorId).Append("\">").Append(HtmlTemplates.Escape(sheet.Name)).Append("</a>");
            }
        }

        var pageTitle = $"{parsed.ScreenCode} - {parsed.ScreenName}";
        var html = HtmlTemplates.ScreenPage
            .Replace("%%TITLE%%", HtmlTemplates.Escape(pageTitle), StringComparison.Ordinal)
            .Replace("%%DOC_NUMBER%%", HtmlTemplates.Escape(parsed.DocNumber), StringComparison.Ordinal)
            .Replace("%%REVISION%%", HtmlTemplates.Escape(parsed.Revision), StringComparison.Ordinal)
            .Replace("%%SOURCE_FILE%%", HtmlTemplates.Escape(Path.GetFileName(file)), StringComparison.Ordinal)
            .Replace("%%NAV%%", nav.ToString(), StringComparison.Ordinal)
            .Replace("%%SECTIONS%%", sections.ToString(), StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(htmlOutputDir, htmlFileName), html, Encoding.UTF8);

        return new ScreenRecord
        {
            ScreenCode = parsed.ScreenCode,
            ScreenName = parsed.ScreenName,
            DocNumber = parsed.DocNumber,
            Revision = parsed.Revision,
            SourceFile = file,
            SearchText = searchText.ToString(),
            HtmlFileName = htmlFileName,
            SheetCount = sheetIndex,
        };
    }

    private static string MakeUniqueHtmlFileName(string screenCode, HashSet<string> seen)
    {
        var sanitized = ExcelSheetHtmlRenderer.SanitizeFileNamePart(screenCode);
        var candidate = $"{sanitized}.html";
        var suffix = 1;
        while (!seen.Add(candidate))
        {
            suffix++;
            candidate = $"{sanitized}_{suffix}.html";
        }

        return candidate;
    }
}
