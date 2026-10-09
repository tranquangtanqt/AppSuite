using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
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
    private string _statusText = "Sẵn sàng.";

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
        StatusText = $"Đã lưu thông tin kết nối. Nguồn đang chọn: {SourceDisplayName(SelectedSource)} ({CurrentDatabaseName}).";
    }

    public void SaveOptions(AppOptions options)
    {
        _config.Options = options;
        SaveConfig();
        StatusText = $"Đã lưu cài đặt: kết nối tối đa {options.EffectiveConnectTimeoutSeconds} giây, " +
                     $"mỗi truy vấn tối đa {options.EffectiveCommandTimeoutSeconds} giây.";
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
            StatusText = $"Không lưu được {_settingsStore.ConfigPath}: {ex.Message}";
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

    /// <summary>True only while "1. Đọc database" runs - shows the "Huỷ" button next to it.</summary>
    [ObservableProperty]
    private bool _isImporting;

    /// <summary>IncludeCancelCommand generates ImportDatabaseCancelCommand ("Huỷ"), which cancels
    /// <paramref name="cancellationToken"/>. Cancel / failure leave the previous .db untouched (it is only
    /// replaced after a complete read), so "2. Xuất HTML" stays available for it.</summary>
    [RelayCommand(CanExecute = nameof(CanImport), IncludeCancelCommand = true)]
    private async Task ImportDatabaseAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        IsImporting = true;
        CanExportHtml = false;
        LogLines.Clear();
        StatusText = $"Đang đọc dữ liệu từ {SourceDisplayName(SelectedSource)}...";

        try
        {
            var options = Options;
            // After "Huỷ" the abandoned read may still log a line or two before it notices - drop those.
            void Log(string line)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    AppendLog(line);
                }
            }

            var importTask = SelectedSource switch
            {
                DatabaseSourceType.Oracle => Task.Run(() => _oracleImporter.ImportAsync(OracleConnectionSettings, options, Log, cancellationToken)),
                DatabaseSourceType.MySql => Task.Run(() => _mySqlImporter.ImportAsync(MySqlConnectionSettings, options, Log, cancellationToken)),
                DatabaseSourceType.SqlServer => Task.Run(() => _sqlServerImporter.ImportAsync(SqlServerConnectionSettings, options, Log, cancellationToken)),
                _ => Task.Run(() => _postgresImporter.ImportAsync(PostgresConnectionSettings, options, Log, cancellationToken)),
            };

            // WaitAsync: "Huỷ" returns at once even while a driver is stuck opening the connection (ODP.NET's
            // OpenAsync ignores the token) - that attempt ends on its own within the connect timeout.
            var (tables, columns, foreignKeys) = await importTask.WaitAsync(cancellationToken);

            // Importers swallow per-row / foreign-key errors into the log, so a cancel can come back as a normal
            // (partial) result - never save that over the previous .db.
            cancellationToken.ThrowIfCancellationRequested();

            // Same table name in 2+ schemas -> "schema.table" for those only (SQLite / HTML key tables by name).
            List<string> duplicateNames;
            (tables, columns, foreignKeys, duplicateNames) = TableNameQualifier.QualifyDuplicates(tables, columns, foreignKeys);
            if (duplicateNames.Count > 0)
            {
                AppendLog($"{duplicateNames.Count} tên bảng trùng ở nhiều schema → đặt tên dạng schema.bảng: {string.Join(", ", duplicateNames)}");
            }

            IsImporting = false; // saving to SQLite is quick and must not be interrupted half-way
            StatusText = $"Đang lưu {tables.Count} bảng / {columns.Count} cột vào SQLite...";
            await _database.ReplaceAllAsync(tables, columns, foreignKeys);

            StatusText = $"Đã đọc và lưu xong: {tables.Count} bảng, {columns.Count} cột, {foreignKeys.Count} khoá ngoại. File: {_database.DatabasePath}";
            CanExportHtml = true;
            NextStep = 2;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Drivers report a cancel differently (OperationCanceledException, SqlException "Operation
            // cancelled by user"...) - the token is the reliable signal. Back to whatever is on disk.
            RefreshDatabaseTarget();
            StatusText = _database.Exists
                ? "Đã huỷ đọc database - dữ liệu đọc lần trước vẫn giữ nguyên, vẫn xuất HTML được."
                : "Đã huỷ đọc database.";
            AppendLog("Đã huỷ.");
        }
        catch (Exception ex)
        {
            // The old .db (if any) is untouched - keep "2. Xuất HTML" usable for it, but point at step 1.
            CanExportHtml = _database.Exists;
            NextStep = 1;
            var hint = DescribeConnectionError(ex, SettingsFor(SelectedSource));
            StatusText = $"Lỗi khi đọc database: {ex.Message}";
            AppendLog($"LỖI: {ex.Message}");
            ErrorOccurred?.Invoke("Không đọc được database", $"{hint}\n\nChi tiết: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
            IsBusy = false;
        }
    }

    private object SettingsFor(DatabaseSourceType source) => source switch
    {
        DatabaseSourceType.Oracle => OracleConnectionSettings,
        DatabaseSourceType.MySql => MySqlConnectionSettings,
        DatabaseSourceType.SqlServer => SqlServerConnectionSettings,
        _ => PostgresConnectionSettings,
    };

    /// <summary>
    /// "Thu ket noi" in the settings dialog: connects with the values currently typed in (not yet saved) and
    /// returns a message for the dialog - server version + how many tables/views "1. Doc Database" would read
    /// (0 usually means a wrong Schema), or the same plain-language hint the import error dialog shows.
    /// </summary>
    /// <param name="settings">One of the four *ConnectionSettings types, built from the dialog's boxes.</param>
    /// <returns>Success = connected; TableCount = null when not connected (0 = connected but nothing to read).</returns>
    public async Task<(bool Success, int? TableCount, string Message)> TestConnectionAsync(object settings, CancellationToken cancellationToken)
    {
        var options = Options;
        try
        {
            var result = settings switch
            {
                OracleConnectionSettings o => await Task.Run(() => _oracleImporter.TestConnectionAsync(o, options, cancellationToken), cancellationToken),
                MySqlConnectionSettings m => await Task.Run(() => _mySqlImporter.TestConnectionAsync(m, options, cancellationToken), cancellationToken),
                SqlServerConnectionSettings s => await Task.Run(() => _sqlServerImporter.TestConnectionAsync(s, options, cancellationToken), cancellationToken),
                PostgresConnectionSettings p => await Task.Run(() => _postgresImporter.TestConnectionAsync(p, options, cancellationToken), cancellationToken),
                _ => throw new ArgumentException("Unknown settings type", nameof(settings)),
            };

            var message = $"Kết nối thành công - {result.ServerVersion}.\nSẽ đọc {result.TableCount} bảng / view ({result.Scope}).";
            if (result.TableCount == 0)
            {
                message += "\nKhông có bảng nào để đọc - kiểm tra lại Schema (Oracle: để trống = trùng tên Username) hoặc quyền của tài khoản.";
            }

            return (true, result.TableCount, message);
        }
        catch (OperationCanceledException)
        {
            return (false, null, "Đã huỷ.");
        }
        catch (Exception ex)
        {
            return (false, null, $"{DescribeConnectionError(ex, settings)}\n\nChi tiết: {ex.Message}");
        }
    }

    /// <summary>Turns the driver exception into a plain-language reason + what to check.</summary>
    /// <param name="settings">The connection settings that were used (saved ones for an import, the dialog's
    /// unsaved values for "Thu ket noi") - one of the four *ConnectionSettings types.</param>
    private string DescribeConnectionError(Exception ex, object settings)
    {
        var (target, database) = settings switch
        {
            OracleConnectionSettings o => ($"{o.Host}:{o.Port}", o.ConnectBySid ? o.Sid : o.ServiceName),
            MySqlConnectionSettings m => ($"{m.Host}:{m.Port}", m.Database),
            SqlServerConnectionSettings s => (SqlServerSchemaImporter.BuildDataSource(s), s.Database),
            PostgresConnectionSettings p => ($"{p.Host}:{p.Port}", p.Database),
            _ => ("máy chủ", ""),
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
            Npgsql.PostgresException { SqlState: "3D000" } => $"Database \"{database}\" không tồn tại trên máy chủ.",
            Npgsql.PostgresException => "Máy chủ PostgreSQL từ chối kết nối.",

            Oracle.ManagedDataAccess.Client.OracleException { Number: 1017 } => wrongLogin,
            // ODP.NET reports a wrong service as ORA-50201 with the real ORA-12514 / 12505 nested inside.
            Oracle.ManagedDataAccess.Client.OracleException o when o.Number is 12514 or 12505
                    || ChainContains(o, "ORA-12514") || ChainContains(o, "ORA-12505") =>
                $"Listener ở {target} đang chạy nhưng không có Service Name / SID \"{database}\" - kiểm tra lại ô Service Name hoặc SID " +
                "(Service Name thường có cả tên miền, vd orcl.congty.local; xem bằng lệnh lsnrctl status trên máy chủ).",

            MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.AccessDenied } => wrongLogin,
            MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.UnknownDatabase } =>
                $"Database \"{database}\" không tồn tại trên máy chủ.",

            Microsoft.Data.SqlClient.SqlException { Number: 18456 } => wrongLogin,
            Microsoft.Data.SqlClient.SqlException { Number: 4060 } =>
                $"Không mở được database \"{database}\" (không tồn tại hoặc tài khoản không có quyền).",

            // Everything else while opening: host unreachable / refused / timed out / DNS / TLS.
            _ => unreachable,
        };
    }

    private static bool ChainContains(Exception ex, string text)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportHtmlAsync()
    {
        IsBusy = true;
        StatusText = "Đang xuất file HTML...";

        try
        {
            var path = await Task.Run(() => _htmlGenerator.Generate(_database, CurrentDatabaseName));
            _htmlPath = path;
            CanOpenHtml = true;
            NextStep = 3;
            StatusText = $"Đã xuất HTML: {path}";
            AppendLog($"Đã xuất: {path}");
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
            StatusText = $"Lỗi khi xuất HTML: {ex.Message}";
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
            StatusText = "Chưa có file HTML - hãy xuất trước.";
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
