using System.Text;
using Mcf.Screen.HtmlGenerator.Models;
using OfficeOpenXml;

namespace Mcf.Screen.HtmlGenerator.Services;

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

        var seenHtmlFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processed = 0;

        foreach (var file in files)
        {
            processed++;
            try
            {
                var record = ImportOneFile(file, htmlOutputDir, seenHtmlFileNames, log);
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

    /// <summary>"画面イメージ" (the screen's own screenshot) isn't skipped like the sheets above - it
    /// still contributes its HTML/search text - but it's rendered up front and spliced into 概要's
    /// 【説明】 section (see OverviewSheetParser) rather than getting its own top-level sheet section/
    /// nav entry further down the page, so a reader sees the screenshot right after the description.</summary>
    private const string ScreenImageSheetName = "画面イメージ";

    private static ScreenRecord ImportOneFile(string file, string htmlOutputDir, HashSet<string> seenHtmlFileNames, Action<string> log)
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
            // Rendered before the main loop below (whatever its actual tab order in the workbook is)
            // so it's ready to hand to 概要's renderer when that sheet comes up.
            var screenImageSheet = package.Workbook.Worksheets.FirstOrDefault(s => s.Name == ScreenImageSheetName);
            var screenImageHtml = screenImageSheet?.Dimension is not null
                ? ExcelSheetHtmlRenderer.RenderSheet(screenImageSheet, imagesOutputDir, imagesRelativeUrl, searchText, log)
                : null;
            var screenImageSpliced = false;

            foreach (var sheet in package.Workbook.Worksheets)
            {
                if (sheet.Dimension is null || SkippedSheetNames.Contains(sheet.Name) || sheet.Name == ScreenImageSheetName)
                {
                    continue;
                }

                sheetIndex++;
                var anchorId = $"sheet-{sheetIndex}";
                string sheetHtml;
                if (sheet.Name == "概要")
                {
                    sheetHtml = ExcelSheetHtmlRenderer.RenderSheet(sheet, imagesOutputDir, imagesRelativeUrl, searchText, log, screenImageHtml);
                    screenImageSpliced = true;
                }
                else
                {
                    sheetHtml = ExcelSheetHtmlRenderer.RenderSheet(sheet, imagesOutputDir, imagesRelativeUrl, searchText, log);
                }

                sections.Append("<section class=\"sheet\" id=\"").Append(anchorId).Append("\"><h2>")
                    .Append(HtmlTemplates.Escape(sheet.Name)).Append("</h2>")
                    .Append(sheetHtml).Append("</section>");
                nav.Append("<a href=\"#").Append(anchorId).Append("\">").Append(HtmlTemplates.Escape(sheet.Name)).Append("</a>");
            }

            // No 概要 sheet to splice into (unexpected, but this codebase never silently drops content
            // over an assumption about layout) - fall back to a top-level section of its own instead
            // of losing the screenshot entirely.
            if (!screenImageSpliced && !string.IsNullOrEmpty(screenImageHtml))
            {
                sheetIndex++;
                var anchorId = $"sheet-{sheetIndex}";
                sections.Append("<section class=\"sheet\" id=\"").Append(anchorId).Append("\"><h2>")
                    .Append(HtmlTemplates.Escape(ScreenImageSheetName)).Append("</h2>")
                    .Append(screenImageHtml).Append("</section>");
                nav.Append("<a href=\"#").Append(anchorId).Append("\">").Append(HtmlTemplates.Escape(ScreenImageSheetName)).Append("</a>");
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
