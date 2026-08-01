using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ModuleD.Services;

namespace ModuleD.ViewModels;

/// <summary>
/// Drives the three-step pipeline: read Data\Excel\*.xlsm -> save to Data\Database\ModuleD.db ->
/// export Data\Database\ModuleD.html. Mirrors ModuleC's SearchViewModel style (CommunityToolkit.Mvvm,
/// manually constructed - no DI container).
/// </summary>
public sealed partial class ModuleDViewModel : ObservableObject
{
    private readonly ExcelDbDefImporter _importer = new();
    private readonly ModuleDDatabase _database = new();
    private readonly HtmlReportGenerator _htmlGenerator = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private string? _htmlPath;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "San sang.";

    [ObservableProperty]
    private bool _canExportHtml;

    [ObservableProperty]
    private bool _canOpenHtml;

    public ObservableCollection<string> LogLines { get; } = [];

    public ModuleDViewModel()
    {
        CanExportHtml = _database.Exists;
        var expectedHtmlPath = Path.Combine(AppContext.BaseDirectory, "Data", "Database", "ModuleD.html");
        if (File.Exists(expectedHtmlPath))
        {
            _htmlPath = expectedHtmlPath;
            CanOpenHtml = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ImportExcelAsync()
    {
        IsBusy = true;
        CanExportHtml = false;
        LogLines.Clear();
        StatusText = "Dang doc du lieu Excel...";

        try
        {
            var excelDir = Path.Combine(AppContext.BaseDirectory, "Data", "Excel");
            var (tables, columns, foreignKeys) = await Task.Run(() =>
                _importer.ImportDirectory(excelDir, AppendLog));

            StatusText = $"Dang luu {tables.Count} bang / {columns.Count} cot vao SQLite...";
            await _database.ReplaceAllAsync(tables, columns, foreignKeys);

            StatusText = $"Da doc va luu xong: {tables.Count} bang, {columns.Count} cot. File: {_database.DatabasePath}";
            CanExportHtml = true;
        }
        catch (Exception ex)
        {
            StatusText = $"Loi khi doc Excel: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportHtmlAsync()
    {
        IsBusy = true;
        StatusText = "Dang xuat file HTML...";

        try
        {
            var path = await Task.Run(() => _htmlGenerator.Generate(_database));
            _htmlPath = path;
            CanOpenHtml = true;
            StatusText = $"Da xuat HTML: {path}";
            AppendLog($"Da xuat: {path}");
        }
        catch (Exception ex)
        {
            StatusText = $"Loi khi xuat HTML: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenHtml))]
    private void OpenHtml()
    {
        if (_htmlPath is null || !File.Exists(_htmlPath))
        {
            StatusText = "Chua co file HTML - hay xuat truoc.";
            return;
        }

        Process.Start(new ProcessStartInfo(_htmlPath) { UseShellExecute = true });
    }

    private bool CanRun() => !IsBusy;

    private bool CanExport() => !IsBusy && CanExportHtml;

    partial void OnIsBusyChanged(bool value)
    {
        ImportExcelCommand.NotifyCanExecuteChanged();
        ExportHtmlCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanExportHtmlChanged(bool value) => ExportHtmlCommand.NotifyCanExecuteChanged();

    partial void OnCanOpenHtmlChanged(bool value) => OpenHtmlCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Called from ExcelDbDefImporter while it runs inside Task.Run (a background thread) - LogLines
    /// is bound to the UI's ListView, so mutating it off the UI thread throws RPC_E_WRONG_THREAD
    /// (0x8001010E). Marshal the update back onto the UI thread via DispatcherQueue instead.
    /// </summary>
    private void AppendLog(string line)
    {
        _dispatcherQueue.TryEnqueue(() => LogLines.Add(line));
    }
}
