using System;
using System.Threading;
using System.Threading.Tasks;

namespace ModuleB.Services;

/// <summary>
/// Opens a workbook in the user's installed Excel and selects a specific sheet/cell, via late-bound
/// COM automation (Type.GetTypeFromProgID + dynamic) rather than a Microsoft.Office.Interop.Excel
/// package reference - that avoids pinning to one Office/PIA version. Excel COM requires an STA
/// thread, so the call runs on a dedicated thread rather than the WinUI dispatcher thread pool.
/// </summary>
public static class ExcelInteropService
{
    public static Task OpenAndFocusCellAsync(string filePath, string sheetName, string cellReference)
    {
        var tcs = new TaskCompletionSource();

        var thread = new Thread(() =>
        {
            try
            {
                OpenAndFocusCellCore(filePath, sheetName, cellReference);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    private static void OpenAndFocusCellCore(string filePath, string sheetName, string cellReference)
    {
        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("Khong tim thay Excel tren may nay.");

        dynamic excelApp = Activator.CreateInstance(excelType)!;
        excelApp.Visible = true;

        dynamic workbook = excelApp.Workbooks.Open(filePath);
        dynamic worksheet = workbook.Worksheets[sheetName];
        worksheet.Activate();

        dynamic range = worksheet.Range[cellReference];
        range.Select();

        excelApp.ActiveWindow.Activate();
    }
}
