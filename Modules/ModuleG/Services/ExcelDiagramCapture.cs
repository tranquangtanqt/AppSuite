using System.Runtime.InteropServices;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ModuleG.Services;

/// <summary>
/// Rasterizes a cell range to a PNG for content EPPlus can't read faithfully: floating shapes/
/// connector-arrows (the 処理関連図/サービス関連図 flow diagram on the 概要 sheet - see
/// OverviewSheetParser), which aren't <c>ExcelPicture</c> drawings and have no grid representation to
/// fall back on.
///
/// Uses Excel COM Interop (late-bound through <see cref="Type.GetTypeFromProgID"/> - no PIA/COM
/// reference needed in the csproj) purely for <c>Worksheet.ExportAsFixedFormat</c> to PDF - a
/// background print-pipeline operation that (unlike the <c>Range.CopyPicture</c> clipboard trick tried
/// first) needs no visible/focused/activated window at all, so it doesn't hit the "Unable to get the
/// CopyPicture property of the Range class" class of COM automation errors that come from window
/// activation/focus/message-pump timing. The PDF page is then rendered to PNG via <c>Windows.Data.Pdf</c>
/// - a WinRT API bundled with Windows itself, no extra NuGet package needed on this WinUI 3 TFM.
///
/// Requires Excel installed on the machine running the import. If not found, <see cref="TryStart"/>
/// fails once (cached - never retried) and every subsequent capture call is skipped, falling back to
/// the existing generic grid render. One instance is meant to live for a whole import batch: Excel
/// process startup is the expensive part (~1-2s), so it's reused across hundreds of files by
/// opening/closing just the workbook each time rather than relaunching Excel per file.
/// </summary>
public sealed class ExcelDiagramCapture : IDisposable
{
    private dynamic? _app;
    private bool _startAttempted;

    public bool TryStart(out string? error)
    {
        if (_app is not null)
        {
            error = null;
            return true;
        }

        if (_startAttempted)
        {
            error = "Excel khong kha dung (da thu khoi dong truoc do va that bai).";
            return false;
        }

        _startAttempted = true;
        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType is null)
            {
                error = "Khong tim thay Excel tren may nay (ProgID 'Excel.Application' khong dang ky).";
                return false;
            }

            dynamic app = Activator.CreateInstance(excelType)!;
            // Unlike the CopyPicture approach, ExportAsFixedFormat is a background print-pipeline
            // call - it doesn't need a visible, focused or activated window, so this can stay fully
            // hidden (no on-screen flash during a batch import).
            app.Visible = false;
            app.DisplayAlerts = false;
            _app = app;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _app = null;
            return false;
        }
    }

    /// <summary>Opens <paramref name="filePath"/> read-only, exports <paramref name="rangeAddress"/>
    /// on <paramref name="sheetName"/> as a PNG to <paramref name="outputPngPath"/>. Returns false
    /// (with <paramref name="error"/> set) on any failure - never fatal to the batch, caller falls
    /// back to the plain grid renderer for that section instead.</summary>
    public bool TryCaptureRange(string filePath, string sheetName, string rangeAddress, string outputPngPath, out string? error)
    {
        if (!TryStart(out error))
        {
            return false;
        }

        dynamic? workbook = null;
        var tempPdfPath = Path.Combine(Path.GetTempPath(), $"ModuleG_diagram_{Guid.NewGuid():N}.pdf");
        try
        {
            workbook = _app!.Workbooks.Open(filePath, 0, true);
            dynamic sheet = workbook.Sheets[sheetName];
            dynamic pageSetup = sheet.PageSetup;

            // Print just this range, scaled to fit exactly 1 page wide x 1 page tall (Zoom must be
            // disabled first - Excel ignores FitToPages while Zoom is a percentage) so the export is
            // always a single PDF page, with margins zeroed so the diagram fills it edge-to-edge.
            pageSetup.PrintArea = rangeAddress;
            pageSetup.Zoom = false;
            pageSetup.FitToPagesWide = 1;
            pageSetup.FitToPagesTall = 1;
            pageSetup.LeftMargin = 0;
            pageSetup.RightMargin = 0;
            pageSetup.TopMargin = 0;
            pageSetup.BottomMargin = 0;
            pageSetup.HeaderMargin = 0;
            pageSetup.FooterMargin = 0;
            pageSetup.CenterHorizontally = false;
            pageSetup.CenterVertically = false;

            sheet.ExportAsFixedFormat(0 /* xlTypePDF */, tempPdfPath);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPngPath)!);
            RenderPdfFirstPageToPng(tempPdfPath, outputPngPath);

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { workbook?.Close(false); } catch { /* best-effort cleanup */ }
            ReleaseComObject(workbook);
            try { File.Delete(tempPdfPath); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>Runs the WinRT PDF-render calls on a fresh ThreadPool (MTA) task and blocks on it,
    /// rather than awaiting/blocking directly on this (STA) thread: WinRT async completions can need
    /// to marshal back onto the apartment that started them, which would deadlock a blocking .Result
    /// on an STA thread with no message pump of its own. Routing through Task.Run keeps the whole
    /// async chain off this thread entirely, so the block-and-wait here is safe.</summary>
    private static void RenderPdfFirstPageToPng(string pdfPath, string outputPngPath) =>
        Task.Run(async () =>
        {
            var file = await StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDocument = await PdfDocument.LoadFromFileAsync(file);
            using var page = pdfDocument.GetPage(0);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream);

            stream.Seek(0);
            await using var pngStream = File.Create(outputPngPath);
            await stream.AsStreamForRead().CopyToAsync(pngStream);
        }).GetAwaiter().GetResult();

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    public void Dispose()
    {
        if (_app is null)
        {
            return;
        }

        try { _app.Quit(); } catch { /* best-effort cleanup */ }
        ReleaseComObject(_app);
        _app = null;
    }
}
