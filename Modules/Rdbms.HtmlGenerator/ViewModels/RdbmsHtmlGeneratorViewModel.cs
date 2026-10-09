using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Rdbms.HtmlGenerator.Models;
using Rdbms.HtmlGenerator.Services;

namespace Rdbms.HtmlGenerator.ViewModels;

/// <summary>
/// Drives the pipeline: connect to PostgreSQL / Oracle / MySQL / SQL Server (settings from
/// Data\Config\config.xml) -> save schema to Data\Database\{databaseName}.db -> export
/// Data\Database\{databaseName}.html, where databaseName is whichever database/service is currently
/// selected to connect to. Mirrors McfDbDefHtmlGeneratorViewModel's style (CommunityToolkit.Mvvm,
/// manually constructed - no DI container).
/// </summary>
public sealed partial class RdbmsHtmlGeneratorViewModel : ObservableObject
{
    private readonly PostgresSchemaImporter _postgresImporter = new();
    private readonly OracleSchemaImporter _oracleImporter = new();
    private readonly MySqlSchemaImporter _mySqlImporter = new();
    private readonly SqlServerSchemaImporter _sqlServerImporter = new();
    private readonly HtmlReportGenerator _htmlGenerator = new();
    private readonly ConnectionSettingsStore _settingsStore = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly DatabaseConnectionsConfig _config;
    private RdbmsHtmlGeneratorDatabase _database;
    private string? _htmlPath;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "San sang.";

    [ObservableProperty]
    private DatabaseSourceType _selectedSource;

    [ObservableProperty]
    private bool _canExportHtml;

    [ObservableProperty]
    private bool _canOpenHtml;

    /// <summary>Bước người dùng nên bấm tiếp (1 = Doc Database, 2 = Xuat HTML, 3 = Mo file HTML) -
    /// nút của bước này được tô màu nhấn (AccentButtonStyle).</summary>
    [ObservableProperty]
    private int _nextStep = 1;

    public PostgresConnectionSettings PostgresConnectionSettings => _config.Postgres;

    public OracleConnectionSettings OracleConnectionSettings => _config.Oracle;

    public MySqlConnectionSettings MySqlConnectionSettings => _config.MySql;

    public SqlServerConnectionSettings SqlServerConnectionSettings => _config.SqlServer;

    public AppOptions Options => _config.Options;

    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>Raised (on the UI thread) when an import fails so the window can show a dialog -
    /// a status line alone is easy to miss and looks like the app is still hanging.</summary>
    public event Action<string, string>? ErrorOccurred;

    /// <summary>The name of the database/service currently selected to connect to - this is what
    /// Data\Database\{name}.db and {name}.html get named after, so different databases keep separate
    /// caches instead of overwriting each other.</summary>
    public string CurrentDatabaseName
    {
        get
        {
            var name = SelectedSource switch
            {
                DatabaseSourceType.Oracle => OracleDatabaseName,
                DatabaseSourceType.MySql => MySqlConnectionSettings.Database,
                DatabaseSourceType.SqlServer => SqlServerConnectionSettings.Database,
                _ => PostgresConnectionSettings.Database,
            };
            return string.IsNullOrWhiteSpace(name) ? "Rdbms.HtmlGenerator" : name;
        }
    }

    private string OracleDatabaseName => OracleConnectionSettings.ConnectBySid
        ? OracleConnectionSettings.Sid
        : OracleConnectionSettings.ServiceName;

    public RdbmsHtmlGeneratorViewModel()
    {
        _config = _settingsStore.Load();
        _selectedSource = Enum.IsDefined(_config.SelectedSource) ? _config.SelectedSource : DatabaseSourceType.Postgres;
        _database = new RdbmsHtmlGeneratorDatabase(CurrentDatabaseName);
        RefreshDatabaseTarget();
    }

    /// <summary>True when the currently selected source has enough info saved to attempt an import -
    /// checked lazily (not cached) so the Import button re-enables the instant Save is called.</summary>
    public bool CanImportDatabase => SelectedSource switch
    {
        DatabaseSourceType.Oracle => !string.IsNullOrWhiteSpace(OracleConnectionSettings.Host) && !string.IsNullOrWhiteSpace(OracleDatabaseName),
        DatabaseSourceType.MySql => !string.IsNullOrWhiteSpace(MySqlConnectionSettings.Host) && !string.IsNullOrWhiteSpace(MySqlConnectionSettings.Database),
        DatabaseSourceType.SqlServer => !string.IsNullOrWhiteSpace(SqlServerConnectionSettings.Host) && !string.IsNullOrWhiteSpace(SqlServerConnectionSettings.Database),
        _ => !string.IsNullOrWhiteSpace(PostgresConnectionSettings.Host) && !string.IsNullOrWhiteSpace(PostgresConnectionSettings.Database),
    };

    /// <summary>Saves every tab of the "Thiet lap thong tin database" dialog at once.</summary>
    public void SaveConnectionSettings(
        PostgresConnectionSettings postgres, OracleConnectionSettings oracle,
        MySqlConnectionSettings mySql, SqlServerConnectionSettings sqlServer)
    {
        _config.Postgres = postgres;
        _config.Oracle = oracle;
        _config.MySql = mySql;
        _config.SqlServer = sqlServer;
        SaveConfig();
        OnPropertyChanged(nameof(CanImportDatabase));
        ImportDatabaseCommand.NotifyCanExecuteChanged();
        RefreshDatabaseTarget();
        StatusText = $"Da luu thong tin ket noi. Nguon dang chon: {SourceDisplayName(SelectedSource)} ({CurrentDatabaseName}).";
    }

    public void SaveOptions(AppOptions options)
    {
        _config.Options = options;
        SaveConfig();
        StatusText = $"Da luu cai dat: ket noi toi da {options.EffectiveConnectTimeoutSeconds} giay, " +
                     $"moi truy van toi da {options.EffectiveCommandTimeoutSeconds} giay.";
    }

    private void SaveConfig()
    {
        _config.SelectedSource = SelectedSource;
        try
        {
            _settingsStore.Save(_config);
        }
        catch (Exception ex)
        {
            StatusText = $"Khong luu duoc {_settingsStore.ConfigPath}: {ex.Message}";
        }
    }

    public static string SourceDisplayName(DatabaseSourceType source) => source switch
    {
        DatabaseSourceType.Oracle => "Oracle",
        DatabaseSourceType.MySql => "MySQL",
        DatabaseSourceType.SqlServer => "SQL Server",
        _ => "PostgreSQL",
    };

    /// <summary>Re-points _database/_htmlPath at Data\Database\{CurrentDatabaseName}.{db,html} and
    /// refreshes CanExportHtml/CanOpenHtml against whatever already exists there from a previous run
    /// - called whenever the selected source or its saved settings change the target database name.</summary>
    private void RefreshDatabaseTarget()
    {
        _database = new RdbmsHtmlGeneratorDatabase(CurrentDatabaseName);
        CanExportHtml = _database.Exists;

        var expectedHtmlPath = Path.Combine(AppContext.BaseDirectory, "Data", "Database", $"{CurrentDatabaseName}.html");
        _htmlPath = File.Exists(expectedHtmlPath) ? expectedHtmlPath : null;
        CanOpenHtml = _htmlPath is not null;

        // HTML cũ hơn .db (đọc lại DB nhưng chưa xuất) thì bước tiếp theo vẫn là Xuất HTML.
        NextStep = !CanExportHtml ? 1
            : _htmlPath is not null && File.GetLastWriteTimeUtc(_htmlPath) >= File.GetLastWriteTimeUtc(_database.DatabasePath) ? 3
            : 2;
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportDatabaseAsync()
    {
        IsBusy = true;
        CanExportHtml = false;
        LogLines.Clear();
        StatusText = $"Dang doc du lieu tu {SourceDisplayName(SelectedSource)}...";

        try
        {
            var options = Options;
            var (tables, columns, foreignKeys) = SelectedSource switch
            {
                DatabaseSourceType.Oracle => await Task.Run(() => _oracleImporter.ImportAsync(OracleConnectionSettings, options, AppendLog)),
                DatabaseSourceType.MySql => await Task.Run(() => _mySqlImporter.ImportAsync(MySqlConnectionSettings, options, AppendLog)),
                DatabaseSourceType.SqlServer => await Task.Run(() => _sqlServerImporter.ImportAsync(SqlServerConnectionSettings, options, AppendLog)),
                _ => await Task.Run(() => _postgresImporter.ImportAsync(PostgresConnectionSettings, options, AppendLog)),
            };

            StatusText = $"Dang luu {tables.Count} bang / {columns.Count} cot vao SQLite...";
            await _database.ReplaceAllAsync(tables, columns, foreignKeys);

            StatusText = $"Da doc va luu xong: {tables.Count} bang, {columns.Count} cot, {foreignKeys.Count} khoa ngoai. File: {_database.DatabasePath}";
            CanExportHtml = true;
            NextStep = 2;
        }
        catch (Exception ex)
        {
            NextStep = 1;
            var hint = DescribeConnectionError(ex);
            StatusText = $"Loi khi doc database: {ex.Message}";
            AppendLog($"LOI: {ex.Message}");
            ErrorOccurred?.Invoke("Không đọc được database", $"{hint}\n\nChi tiết: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Turns the driver exception into a plain-language reason + what to check.</summary>
    private string DescribeConnectionError(Exception ex)
    {
        var target = SelectedSource switch
        {
            DatabaseSourceType.Oracle => $"{OracleConnectionSettings.Host}:{OracleConnectionSettings.Port}",
            DatabaseSourceType.MySql => $"{MySqlConnectionSettings.Host}:{MySqlConnectionSettings.Port}",
            DatabaseSourceType.SqlServer => SqlServerSchemaImporter.BuildDataSource(SqlServerConnectionSettings),
            _ => $"{PostgresConnectionSettings.Host}:{PostgresConnectionSettings.Port}",
        };
        var unreachable = $"Không kết nối được tới {target} (đã chờ tối đa {Options.EffectiveConnectTimeoutSeconds} giây).\n" +
                          "Kiểm tra: Host / Port đúng chưa, máy chủ database có đang chạy không, đã bật VPN / cùng mạng chưa, firewall có chặn cổng không. " +
                          "Mạng chậm thì tăng \"Giới hạn thời gian kết nối\" trong Cài đặt.";
        const string wrongLogin = "Sai Username hoặc Password (hoặc tài khoản không được phép đăng nhập).";

        if (ex is not DatabaseConnectException { InnerException: { } connectError })
        {
            // Connected fine - a schema query failed afterwards. Driver timeouts all surface as one of these.
            var isTimeout = ex is TimeoutException || ex.InnerException is TimeoutException
                || ex is Npgsql.PostgresException { SqlState: "57014" }
                || ex is Oracle.ManagedDataAccess.Client.OracleException { Number: 1013 }
                || ex is MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.CommandTimeoutExpired }
                || ex is Microsoft.Data.SqlClient.SqlException { Number: -2 };
            return isTimeout
                ? $"Đã kết nối được nhưng truy vấn đọc schema chạy quá {Options.EffectiveCommandTimeoutSeconds} giây. " +
                  "Database lớn / máy chủ chậm thì tăng \"Giới hạn thời gian truy vấn\" trong Cài đặt, hoặc nhập Schema để đọc ít hơn."
                : "Đã kết nối được nhưng đọc schema bị lỗi (tài khoản có thể thiếu quyền xem danh mục hệ thống).";
        }

        return connectError switch
        {
            Npgsql.PostgresException { SqlState: "28P01" or "28000" } => wrongLogin,
            Npgsql.PostgresException { SqlState: "3D000" } => $"Database \"{PostgresConnectionSettings.Database}\" không tồn tại trên máy chủ.",
            Npgsql.PostgresException => "Máy chủ PostgreSQL từ chối kết nối.",

            Oracle.ManagedDataAccess.Client.OracleException { Number: 1017 } => wrongLogin,
            Oracle.ManagedDataAccess.Client.OracleException { Number: 12514 or 12505 } =>
                "Máy chủ không biết Service Name / SID này - kiểm tra lại ô Service Name hoặc SID.",

            MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.AccessDenied } => wrongLogin,
            MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.UnknownDatabase } =>
                $"Database \"{MySqlConnectionSettings.Database}\" không tồn tại trên máy chủ.",

            Microsoft.Data.SqlClient.SqlException { Number: 18456 } => wrongLogin,
            Microsoft.Data.SqlClient.SqlException { Number: 4060 } =>
                $"Không mở được database \"{SqlServerConnectionSettings.Database}\" (không tồn tại hoặc tài khoản không có quyền).",

            // Everything else while opening: host unreachable / refused / timed out / DNS / TLS.
            _ => unreachable,
        };
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
            NextStep = 3;
            StatusText = $"Da xuat HTML: {path}";
            AppendLog($"Da xuat: {path}");
            if (Options.OpenHtmlAfterExport)
            {
                OpenHtml();
            }

            if (Options.OpenFolderAfterExport)
            {
                // Mở Explorer tại Data\Database với file HTML vừa xuất được chọn sẵn.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
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
        SaveConfig(); // nhớ nguồn đã chọn cho lần mở sau
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
