using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ModuleH.Models;
using ModuleH.Services;

namespace ModuleH.ViewModels;

/// <summary>
/// Drives the pipeline: pick source folder -> read every *.xlsx under it -> save to
/// Data\Database\02_CRUD図.db -> export Data\Database\Html\02_CRUD図.html (+ 1 file per logic sheet).
/// Mirrors ModuleGViewModel's style (CommunityToolkit.Mvvm, manually constructed - no DI container).
/// </summary>
public sealed partial class ModuleHViewModel : ObservableObject
{
    private readonly CrudDocImporter _importer = new();
    private readonly CrudDatabase _database = new();
    private readonly HtmlIndexGenerator _htmlGenerator = new();
    private readonly SourceFolderSettingsStore _settingsStore = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private string? _htmlPath;

    [ObservableProperty]
    private string _sourceFolder = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "San sang.";

    [ObservableProperty]
    private bool _canImport;

    [ObservableProperty]
    private bool _canExportHtml;

    [ObservableProperty]
    private bool _canOpenHtml;

    public ObservableCollection<string> LogLines { get; } = [];

    public ModuleHViewModel()
    {
        SourceFolder = _settingsStore.Load().RootFolder;
        CanImport = !string.IsNullOrWhiteSpace(SourceFolder);
        CanExportHtml = _database.Exists;

        var expectedHtmlPath = Path.Combine(AppContext.BaseDirectory, "Data", "Database", "Html", "02_CRUD図.html");
        if (File.Exists(expectedHtmlPath))
        {
            _htmlPath = expectedHtmlPath;
            CanOpenHtml = true;
        }
    }

    /// <summary>Called by MainWindow after the user picks a folder via FolderPicker (a WinUI/View
    /// concern, so it lives in code-behind, not here) - persists it and updates CanImport.</summary>
    public void SetSourceFolder(string folder)
    {
        SourceFolder = folder;
        _settingsStore.Save(new SourceFolderSettings { RootFolder = folder });
        CanImport = !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder);
        ImportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private async Task ImportAsync()
    {
        IsBusy = true;
        CanExportHtml = false;
        LogLines.Clear();
        StatusText = "Dang doc du lieu Excel...";

        try
        {
            var htmlOutputDir = Path.Combine(AppContext.BaseDirectory, "Data", "Database", "Html");
            var folder = SourceFolder;
            var logics = await Task.Run(() => _importer.ImportDirectory(folder, htmlOutputDir, AppendLog));

            StatusText = $"Dang luu {logics.Count} logic vao SQLite...";
            await _database.ReplaceAllAsync(logics);

            StatusText = $"Da doc va luu xong: {logics.Count} logic. File: {_database.DatabasePath}";
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
            var htmlOutputDir = Path.Combine(AppContext.BaseDirectory, "Data", "Database", "Html");
            var path = await Task.Run(() => _htmlGenerator.Generate(_database, htmlOutputDir));
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

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_htmlPath) { UseShellExecute = true });
    }

    private bool CanRunImport() => !IsBusy && CanImport;

    private bool CanExport() => !IsBusy && CanExportHtml;

    partial void OnIsBusyChanged(bool value)
    {
        ImportCommand.NotifyCanExecuteChanged();
        ExportHtmlCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanImportChanged(bool value) => ImportCommand.NotifyCanExecuteChanged();

    partial void OnCanExportHtmlChanged(bool value) => ExportHtmlCommand.NotifyCanExecuteChanged();

    partial void OnCanOpenHtmlChanged(bool value) => OpenHtmlCommand.NotifyCanExecuteChanged();

    /// <summary>Called from CrudDocImporter while it runs inside Task.Run (a background thread) -
    /// LogLines is bound to the UI's ListView, so mutating it off the UI thread throws
    /// RPC_E_WRONG_THREAD (0x8001010E). Marshal the update back onto the UI thread via
    /// DispatcherQueue instead (same fix ModuleG needed).</summary>
    private void AppendLog(string line)
    {
        _dispatcherQueue.TryEnqueue(() => LogLines.Add(line));
    }
}
