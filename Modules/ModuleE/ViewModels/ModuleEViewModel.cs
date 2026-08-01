using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ModuleE.Models;
using ModuleE.Services;

namespace ModuleE.ViewModels;

/// <summary>
/// Drives the pipeline: connect to PostgreSQL or Oracle (settings from Data\Config\config.xml) ->
/// save schema to Data\Database\{databaseName}.db -> export Data\Database\{databaseName}.html, where
/// databaseName is whichever database/service is currently selected to connect to. Mirrors
/// ModuleDViewModel's style (CommunityToolkit.Mvvm, manually constructed - no DI container).
/// </summary>
public sealed partial class ModuleEViewModel : ObservableObject
{
    private readonly PostgresSchemaImporter _postgresImporter = new();
    private readonly OracleSchemaImporter _oracleImporter = new();
    private readonly HtmlReportGenerator _htmlGenerator = new();
    private readonly ConnectionSettingsStore _settingsStore = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private ModuleEDatabase _database;
    private string? _htmlPath;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "San sang.";

    [ObservableProperty]
    private DatabaseSourceType _selectedSource = DatabaseSourceType.Postgres;

    [ObservableProperty]
    private bool _canExportHtml;

    [ObservableProperty]
    private bool _canOpenHtml;

    public PostgresConnectionSettings PostgresConnectionSettings { get; private set; }

    public OracleConnectionSettings OracleConnectionSettings { get; private set; }

    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>The name of the database/service currently selected to connect to - this is what
    /// Data\Database\{name}.db and {name}.html get named after, so different databases keep separate
    /// caches instead of overwriting each other.</summary>
    public string CurrentDatabaseName => SelectedSource == DatabaseSourceType.Postgres
        ? (string.IsNullOrWhiteSpace(PostgresConnectionSettings.Database) ? "ModuleE" : PostgresConnectionSettings.Database)
        : (string.IsNullOrWhiteSpace(OracleDatabaseName) ? "ModuleE" : OracleDatabaseName);

    private string OracleDatabaseName => OracleConnectionSettings.ConnectBySid
        ? OracleConnectionSettings.Sid
        : OracleConnectionSettings.ServiceName;

    public ModuleEViewModel()
    {
        var config = _settingsStore.Load();
        PostgresConnectionSettings = config.Postgres;
        OracleConnectionSettings = config.Oracle;
        _database = new ModuleEDatabase(CurrentDatabaseName);
        RefreshDatabaseTarget();
    }

    /// <summary>True when the currently selected source has enough info saved to attempt an import -
    /// checked lazily (not cached) so the Import button re-enables the instant Save is called.</summary>
    public bool CanImportDatabase => SelectedSource == DatabaseSourceType.Postgres
        ? !string.IsNullOrWhiteSpace(PostgresConnectionSettings.Host) && !string.IsNullOrWhiteSpace(PostgresConnectionSettings.Database)
        : !string.IsNullOrWhiteSpace(OracleConnectionSettings.Host) && !string.IsNullOrWhiteSpace(OracleDatabaseName);

    public void SavePostgresConnectionSettings(PostgresConnectionSettings settings)
    {
        PostgresConnectionSettings = settings;
        _settingsStore.Save(new DatabaseConnectionsConfig { Postgres = settings, Oracle = OracleConnectionSettings });
        OnPropertyChanged(nameof(CanImportDatabase));
        ImportDatabaseCommand.NotifyCanExecuteChanged();
        RefreshDatabaseTarget();
        StatusText = $"Da luu thong tin ket noi PostgreSQL ({settings.Host}:{settings.Port}/{settings.Database}).";
    }

    public void SaveOracleConnectionSettings(OracleConnectionSettings settings)
    {
        OracleConnectionSettings = settings;
        _settingsStore.Save(new DatabaseConnectionsConfig { Postgres = PostgresConnectionSettings, Oracle = settings });
        OnPropertyChanged(nameof(CanImportDatabase));
        ImportDatabaseCommand.NotifyCanExecuteChanged();
        RefreshDatabaseTarget();
        StatusText = $"Da luu thong tin ket noi Oracle ({settings.Host}:{settings.Port}/{settings.ServiceName}).";
    }

    /// <summary>Re-points _database/_htmlPath at Data\Database\{CurrentDatabaseName}.{db,html} and
    /// refreshes CanExportHtml/CanOpenHtml against whatever already exists there from a previous run
    /// - called whenever the selected source or its saved settings change the target database name.</summary>
    private void RefreshDatabaseTarget()
    {
        _database = new ModuleEDatabase(CurrentDatabaseName);
        CanExportHtml = _database.Exists;

        var expectedHtmlPath = Path.Combine(AppContext.BaseDirectory, "Data", "Database", $"{CurrentDatabaseName}.html");
        _htmlPath = File.Exists(expectedHtmlPath) ? expectedHtmlPath : null;
        CanOpenHtml = _htmlPath is not null;
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportDatabaseAsync()
    {
        IsBusy = true;
        CanExportHtml = false;
        LogLines.Clear();
        StatusText = $"Dang doc du lieu tu {SelectedSource}...";

        try
        {
            var (tables, columns, foreignKeys) = SelectedSource == DatabaseSourceType.Postgres
                ? await Task.Run(() => _postgresImporter.ImportAsync(PostgresConnectionSettings, AppendLog))
                : await Task.Run(() => _oracleImporter.ImportAsync(OracleConnectionSettings, AppendLog));

            StatusText = $"Dang luu {tables.Count} bang / {columns.Count} cot vao SQLite...";
            await _database.ReplaceAllAsync(tables, columns, foreignKeys);

            StatusText = $"Da doc va luu xong: {tables.Count} bang, {columns.Count} cot, {foreignKeys.Count} khoa ngoai. File: {_database.DatabasePath}";
            CanExportHtml = true;
        }
        catch (Exception ex)
        {
            StatusText = $"Loi khi doc database: {ex.Message}";
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
            var path = await Task.Run(() => _htmlGenerator.Generate(_database, CurrentDatabaseName));
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

    private bool CanImport() => !IsBusy && CanImportDatabase;

    private bool CanExport() => !IsBusy && CanExportHtml;

    partial void OnIsBusyChanged(bool value)
    {
        ImportDatabaseCommand.NotifyCanExecuteChanged();
        ExportHtmlCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSourceChanged(DatabaseSourceType value)
    {
        OnPropertyChanged(nameof(CanImportDatabase));
        ImportDatabaseCommand.NotifyCanExecuteChanged();
        RefreshDatabaseTarget();
    }

    partial void OnCanExportHtmlChanged(bool value) => ExportHtmlCommand.NotifyCanExecuteChanged();

    partial void OnCanOpenHtmlChanged(bool value) => OpenHtmlCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Called from the schema importers while they run inside Task.Run (a background thread) -
    /// LogLines is bound to the UI's ListView, so mutating it off the UI thread throws
    /// RPC_E_WRONG_THREAD (0x8001010E). Marshal the update back onto the UI thread via DispatcherQueue.
    /// </summary>
    private void AppendLog(string line)
    {
        _dispatcherQueue.TryEnqueue(() => LogLines.Add(line));
    }
}
