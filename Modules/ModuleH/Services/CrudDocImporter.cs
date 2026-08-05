using System.Text;
using ModuleH.Models;
using OfficeOpenXml;

namespace ModuleH.Services;

/// <summary>
/// Walks every "CRUD図" *.xlsx under a root folder and renders 1 HTML page per non-skipped
/// *worksheet* (not per file, unlike ModuleG - see CrudRecord's doc comment: a single workbook here
/// can hold anywhere from a handful to hundreds of logic sheets, or even 1 sheet with 1000+ rows).
/// </summary>
public sealed class CrudDocImporter
{
    /// <summary>Sheets skipped entirely across every workbook - administrative/cover pages, not CRUD
    /// content.</summary>
    private static readonly HashSet<string> SkippedSheetNames = new() { "表紙", "変更来歴", "本ドキュメントについて" };

    /// <summary>Best-effort, one sheet at a time: a single corrupt/unexpected sheet is logged and
    /// skipped rather than aborting the whole batch.</summary>
    public List<CrudRecord> ImportDirectory(string rootFolder, string htmlOutputDir, Action<string> log)
    {
        var records = new List<CrudRecord>();
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

        // 使用オブジェクト cells sometimes reference another logic's specific ID block, not a table -
        // e.g. "MSBBL6020.Slo_Chk03" means "block Slo_Chk03 inside sheet MSBBL6020". Turning that into
        // a working link needs to know every sheet's eventual .html file name up front, which requires
        // a first pass over every workbook (same file-name/dedup logic as the main pass below, so the
        // 2 stay in sync) before any page is actually rendered.
        var screenCodeIndex = BuildScreenCodeIndex(files, log);

        var seenHtmlFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedSheets = 0;

        foreach (var file in files)
        {
            try
            {
                using var package = new ExcelPackage(new FileInfo(file));
                foreach (var sheet in package.Workbook.Worksheets)
                {
                    if (sheet.Dimension is null || SkippedSheetNames.Contains(sheet.Name))
                    {
                        continue;
                    }

                    processedSheets++;
                    try
                    {
                        var record = ImportOneSheet(sheet, file, htmlOutputDir, seenHtmlFileNames, screenCodeIndex);
                        records.Add(record);
                        if (processedSheets % 25 == 0)
                        {
                            log($"[{processedSheets}] {record.ScreenCode} - {record.ScreenName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        log($"LOI sheet '{sheet.Name}' trong '{Path.GetFileName(file)}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                log($"LOI mo file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        log($"Xong: {processedSheets} sheet -> {records.Count} logic.");
        return records;
    }

    /// <summary>Cheap first pass: just the sheet names + their eventual .html file names (identical
    /// dedup order/logic to the main pass, since both enumerate the same files/sheets in the same
    /// deterministic order) - no parsing of row content, that happens only in the main pass.</summary>
    private static Dictionary<string, string> BuildScreenCodeIndex(List<string> files, Action<string> log)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                using var package = new ExcelPackage(new FileInfo(file));
                foreach (var sheet in package.Workbook.Worksheets)
                {
                    if (sheet.Dimension is null || SkippedSheetNames.Contains(sheet.Name))
                    {
                        continue;
                    }

                    index[sheet.Name] = MakeUniqueHtmlFileName(sheet.Name, seen);
                }
            }
            catch (Exception ex)
            {
                // Best-effort - a file that fails here will fail again (and get logged) in the main
                // pass; skipping it just means cross-references into it won't be linked.
                log($"LOI do truoc danh sach ScreenCode trong '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        return index;
    }

    private static CrudRecord ImportOneSheet(
        ExcelWorksheet sheet, string file, string htmlOutputDir, HashSet<string> seenHtmlFileNames, IReadOnlyDictionary<string, string> screenCodeIndex)
    {
        var searchText = new StringBuilder();
        var model = CrudSheetParser.Parse(sheet, searchText);
        var bodyHtml = CrudHtmlRenderer.Render(model, sheet, screenCodeIndex);

        var htmlFileName = MakeUniqueHtmlFileName(sheet.Name, seenHtmlFileNames);
        var pageTitle = $"{sheet.Name} - {model.ScreenName}";
        var html = HtmlTemplates.LogicPage
            .Replace("%%TITLE%%", HtmlTemplates.Escape(pageTitle), StringComparison.Ordinal)
            .Replace("%%MODULE_ID%%", HtmlTemplates.Escape(model.ModuleId), StringComparison.Ordinal)
            .Replace("%%MODULE_NAME%%", HtmlTemplates.Escape(model.ModuleName), StringComparison.Ordinal)
            .Replace("%%SUBMODULE_ID%%", HtmlTemplates.Escape(model.SubModuleId), StringComparison.Ordinal)
            .Replace("%%SUBMODULE_NAME%%", HtmlTemplates.Escape(model.SubModuleName), StringComparison.Ordinal)
            .Replace("%%DOC_NUMBER%%", HtmlTemplates.Escape(model.DocNumber), StringComparison.Ordinal)
            .Replace("%%VERSION%%", HtmlTemplates.Escape(model.Version), StringComparison.Ordinal)
            .Replace("%%REVISION%%", HtmlTemplates.Escape(model.Revision), StringComparison.Ordinal)
            .Replace("%%SOURCE_FILE%%", HtmlTemplates.Escape(Path.GetFileName(file)), StringComparison.Ordinal)
            .Replace("%%CONTENT%%", bodyHtml, StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(htmlOutputDir, htmlFileName), html, Encoding.UTF8);

        return new CrudRecord
        {
            ScreenCode = sheet.Name,
            ScreenName = model.ScreenName,
            ModuleId = model.ModuleId,
            ModuleName = model.ModuleName,
            SubModuleId = model.SubModuleId,
            SubModuleName = model.SubModuleName,
            DocNumber = model.DocNumber,
            Version = model.Version,
            Revision = model.Revision,
            SourceFile = file,
            SearchText = searchText.ToString(),
            HtmlFileName = htmlFileName,
            BlockCount = model.BlockCount,
        };
    }

    private static string MakeUniqueHtmlFileName(string screenCode, HashSet<string> seen)
    {
        var sanitized = SanitizeFileNamePart(screenCode);
        var candidate = $"{sanitized}.html";
        var suffix = 1;
        while (!seen.Add(candidate))
        {
            suffix++;
            candidate = $"{sanitized}_{suffix}.html";
        }

        return candidate;
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
