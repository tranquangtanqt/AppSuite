using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    /// <summary>Hồ sơ kết nối đang chọn (dev / test / prod...) - mọi thông tin kết nối bên dưới lấy từ đây.</summary>
    public ConnectionProfile CurrentProfile => ConnectionProfiles.Find(_config, _config.SelectedProfile) ?? _config.Profiles[0];

    /// <summary>Tên các hồ sơ cho combobox Hồ sơ (theo thứ tự trong config.xml).</summary>
    public ObservableCollection<string> ProfileNames { get; } = [];

    public PostgresConnectionSettings PostgresConnectionSettings => CurrentProfile.Postgres;

    public OracleConnectionSettings OracleConnectionSettings => CurrentProfile.Oracle;

    public MySqlConnectionSettings MySqlConnectionSettings => CurrentProfile.MySql;

    public SqlServerConnectionSettings SqlServerConnectionSettings => CurrentProfile.SqlServer;

    public AppOptions Options => _config.Options;

    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>Raised (on the UI thread) when an import fails so the window can show a dialog -
    /// a status line alone is easy to miss and looks like the app is still hanging.</summary>
    public event Action<string, string>? ErrorOccurred;

    /// <summary>The name of the database/service currently selected to connect to (shown in status lines).</summary>
    public string CurrentDatabaseName
    {
        get
        {
            var name = CurrentProfile.DatabaseName;
            return string.IsNullOrWhiteSpace(name) ? "Rdbms.HtmlGenerator" : name;
        }
    }

    /// <summary>Data\Database\{name}.db and {name}.html get named after this: database + profile
    /// ("orders (prod)"), so different databases - and the same database on dev / prod - keep separate caches
    /// instead of overwriting each other. Profile "Mặc định" keeps the pre-profile name "{database}".</summary>
    public string CurrentFileBaseName => ConnectionProfiles.FileBaseName(CurrentProfile);

    private string OracleDatabaseName => OracleConnectionSettings.ConnectBySid
        ? OracleConnectionSettings.Sid
        : OracleConnectionSettings.ServiceName;

    public RdbmsHtmlGeneratorViewModel()
    {
        _config = _settingsStore.Load();
        foreach (var profile in _config.Profiles)
        {
            ProfileNames.Add(profile.Name);
        }
        _selectedSource = CurrentProfile.Source;
        _database = new RdbmsHtmlGeneratorDatabase(CurrentFileBaseName);
        RefreshDatabaseTarget();
    }

    // ----- Hồ sơ kết nối (dev / test / prod...) -----

    /// <summary>Đổi sang hồ sơ khác: nguồn + thông tin kết nối + file .db / .html của hồ sơ đó.</summary>
    public void SelectProfile(string name)
    {
        if (ConnectionProfiles.Find(_config, name) is not { } profile || ReferenceEquals(profile, CurrentProfile))
        {
            return;
        }
        _config.SelectedProfile = profile.Name;
        ApplyCurrentProfile();
        StatusText = $"Hồ sơ \"{profile.Name}\": {SourceDisplayName(SelectedSource)} ({CurrentDatabaseName}).";
    }

    /// <summary>Thêm hồ sơ = sao chép hồ sơ đang chọn (sửa Host / Database... trong Thiết lập kết nối) rồi chọn nó.
    /// Trả lỗi tên (null = đã thêm).</summary>
    public string? AddProfile(string name)
    {
        if (ConnectionProfiles.ValidateName(_config, name) is { } error)
        {
            return error;
        }
        var profile = CurrentProfile.Clone(name.Trim());
        _config.Profiles.Add(profile);
        ProfileNames.Add(profile.Name);
        _config.SelectedProfile = profile.Name;
        ApplyCurrentProfile();
        StatusText = $"Đã thêm hồ sơ \"{profile.Name}\" (sao chép thông tin kết nối) - bấm Thiết lập kết nối để sửa.";
        return null;
    }

    public string? RenameProfile(string name)
    {
        var profile = CurrentProfile;
        if (ConnectionProfiles.ValidateName(_config, name, except: profile) is { } error)
        {
            return error;
        }
        var oldName = profile.Name;
        profile.Name = name.Trim();
        _config.SelectedProfile = profile.Name;
        ProfileNames[_config.Profiles.IndexOf(profile)] = profile.Name;
        ApplyCurrentProfile(); // tên file .db / .html đổi theo tên hồ sơ
        StatusText = $"Đã đổi tên hồ sơ \"{oldName}\" thành \"{profile.Name}\".";
        return null;
    }

    /// <summary>Xoá hồ sơ đang chọn (không xoá được hồ sơ cuối cùng). File .db / .html đã xuất giữ nguyên.</summary>
    public bool DeleteProfile()
    {
        if (_config.Profiles.Count <= 1)
        {
            return false;
        }
        var profile = CurrentProfile;
        var index = _config.Profiles.IndexOf(profile);
        _config.Profiles.RemoveAt(index);
        ProfileNames.RemoveAt(index);
        _config.SelectedProfile = _config.Profiles[Math.Min(index, _config.Profiles.Count - 1)].Name;
        ApplyCurrentProfile();
        StatusText = $"Đã xoá hồ sơ \"{profile.Name}\". Đang dùng hồ sơ \"{CurrentProfile.Name}\".";
        return true;
    }

    public bool CanDeleteProfile => _config.Profiles.Count > 1;

    /// <summary>Cho hộp thoại tên hồ sơ (gợi ý tên chưa dùng).</summary>
    public DatabaseConnectionsConfig Config => _config;

    /// <summary>Sau khi đổi / thêm / đổi tên / xoá hồ sơ: nguồn của hồ sơ, nút Đọc, file đích, ghi config.xml.</summary>
    private void ApplyCurrentProfile()
    {
        OnPropertyChanged(nameof(CurrentProfile));
        OnPropertyChanged(nameof(CanDeleteProfile));
        if (SelectedSource != CurrentProfile.Source)
        {
            SelectedSource = CurrentProfile.Source; // OnSelectedSourceChanged làm phần còn lại
            return;
        }
        OnPropertyChanged(nameof(CanImportDatabase));
        ImportDatabaseCommand.NotifyCanExecuteChanged();
        RefreshDatabaseTarget();
        SaveConfig();
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
        var profile = CurrentProfile;
        profile.Postgres = postgres;
        profile.Oracle = oracle;
        profile.MySql = mySql;
        profile.SqlServer = sqlServer;
        SaveConfig();
        OnPropertyChanged(nameof(CanImportDatabase));
        ImportDatabaseCommand.NotifyCanExecuteChanged();
        RefreshDatabaseTarget();
        StatusText = $"Đã lưu thông tin kết nối của hồ sơ \"{profile.Name}\". Nguồn đang chọn: {SourceDisplayName(SelectedSource)} ({CurrentDatabaseName}).";
    }

    public void SaveOptions(AppOptions options)
    {
        _config.Options = options;
        SaveConfig();
        StatusText = $"Đã lưu cài đặt: kết nối tối đa {options.EffectiveConnectTimeoutSeconds} giây, " +
                     $"mỗi truy vấn tối đa {options.EffectiveCommandTimeoutSeconds} giây, giữ {options.EffectiveHistoryCount} bản đọc cũ.";
        // Giảm số bản giữ lại thì dọn ngay cho file đang chọn (các file khác dọn ở lần đọc sau của chúng).
        if (_database.Exists)
        {
            SchemaHistory.Prune(_database.DatabasePath, options.EffectiveHistoryCount);
            RefreshPreviousRead();
        }
    }

    private void SaveConfig()
    {
        CurrentProfile.Source = SelectedSource;
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
        _database = new RdbmsHtmlGeneratorDatabase(CurrentFileBaseName);
        CanExportHtml = _database.Exists;

        var expectedHtmlPath = Path.Combine(AppContext.BaseDirectory, "Data", "Database", $"{CurrentFileBaseName}.html");
        _htmlPath = File.Exists(expectedHtmlPath) ? expectedHtmlPath : null;
        CanOpenHtml = _htmlPath is not null;

        // HTML cũ hơn .db (đọc lại DB nhưng chưa xuất) thì bước tiếp theo vẫn là Xuất HTML.
        NextStep = !CanExportHtml ? 1
            : _htmlPath is not null && File.GetLastWriteTimeUtc(_htmlPath) >= File.GetLastWriteTimeUtc(_database.DatabasePath) ? 3
            : 2;
        OnPropertyChanged(nameof(ConnectionSummary));
        RefreshPreviousRead();
    }

    /// <summary>Bản cũ mới nhất trong Lịch sử của file .db đang chọn (null = chưa có) - cho nút "So với bản trước".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviousRead), nameof(PreviousReadText))]
    [NotifyCanExecuteChangedFor(nameof(CompareWithPreviousCommand))]
    private HistoryEntry? _previousRead;

    public bool HasPreviousRead => PreviousRead is not null;

    public string PreviousReadText => PreviousRead is { } p ? $"So với bản trước (đọc lúc {p.ReadAt:dd/MM HH:mm})" : "";

    private void RefreshPreviousRead()
    {
        try
        {
            PreviousRead = _database.Exists ? SchemaHistory.Latest(_database.DatabasePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PreviousRead = null;
        }
    }

    /// <summary>Dòng dưới Hồ sơ / Nguồn: đang trỏ tới đâu + đã đọc lúc nào.</summary>
    public string ConnectionSummary
    {
        get
        {
            var target = SelectedSource switch
            {
                DatabaseSourceType.Oracle => $"{OracleConnectionSettings.Host}:{OracleConnectionSettings.Port}/{OracleDatabaseName}",
                DatabaseSourceType.MySql => $"{MySqlConnectionSettings.Host}:{MySqlConnectionSettings.Port}/{MySqlConnectionSettings.Database}",
                DatabaseSourceType.SqlServer => $"{SqlServerSchemaImporter.BuildDataSource(SqlServerConnectionSettings)}/{SqlServerConnectionSettings.Database}",
                _ => $"{PostgresConnectionSettings.Host}:{PostgresConnectionSettings.Port}/{PostgresConnectionSettings.Database}",
            };
            if (!CanImportDatabase)
            {
                return "Chưa thiết lập kết nối cho nguồn này - bấm Thiết lập kết nối";
            }
            var read = _database.Exists
                ? $"đã đọc lúc {File.GetLastWriteTime(_database.DatabasePath):dd/MM HH:mm}"
                : "chưa đọc lần nào";
            return $"{target}   ·   {read}";
        }
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
            var import = await importTask.WaitAsync(cancellationToken);

            // Importers swallow per-row / foreign-key errors into the log, so a cancel can come back as a normal
            // (partial) result - never save that over the previous .db.
            cancellationToken.ThrowIfCancellationRequested();

            // Same table name in 2+ schemas -> "schema.table" for those only (SQLite / HTML key tables by name).
            (import, var duplicateNames) = TableNameQualifier.QualifyDuplicates(import);
            if (duplicateNames.Count > 0)
            {
                AppendLog($"{duplicateNames.Count} tên bảng trùng ở nhiều schema → đặt tên dạng schema.bảng: {string.Join(", ", duplicateNames)}");
            }

            IsImporting = false; // saving to SQLite is quick and must not be interrupted half-way
            StatusText = $"Đang lưu {import.Tables.Count} bảng / {import.Columns.Count} cột vào SQLite...";

            // Bản đọc trước → Lịch sử ngay trước khi ghi đè (để so "bản cũ ↔ bản mới" không phải tự chép file).
            string? previousCopy = null;
            try
            {
                previousCopy = SchemaHistory.Save(_database.DatabasePath);
            }
            catch (Exception ex)
            {
                AppendLog($"Không lưu được bản đọc trước vào {SchemaHistory.FolderName}: {ex.Message}");
            }

            try
            {
                await _database.ReplaceAllAsync(import);
            }
            catch
            {
                DeleteQuietly(previousCopy); // .db cũ còn nguyên (transaction) → bản chép là thừa
                throw;
            }

            StatusText = $"Đã đọc và lưu xong: {import.Tables.Count} bảng, {import.Columns.Count} cột, {import.ForeignKeys.Count} khoá ngoại, " +
                         $"{import.Indexes.Count} index, {import.Constraints.Count} ràng buộc. File: {_database.DatabasePath}";
            CanExportHtml = true;
            NextStep = 2;
            OnPropertyChanged(nameof(ConnectionSummary));

            if (previousCopy is not null)
            {
                await LogChangesSincePreviousAsync(previousCopy);
            }
            RefreshPreviousRead();
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

    /// <summary>Sau khi đọc: so bản vừa chép vào Lịch sử với bản mới, ghi tóm tắt vào nhật ký. Giống hệt → xoá bản chép (không
    /// giữ bản trùng); khác → giữ, dọn bớt theo Cài đặt (0 = không giữ). Lỗi ở đây không làm hỏng lần đọc.</summary>
    private async Task LogChangesSincePreviousAsync(string previousCopy)
    {
        var readAt = File.GetLastWriteTime(previousCopy);
        try
        {
            var currentPath = _database.DatabasePath;
            var diff = await Task.Run(() => SchemaComparer.Compare(
                RdbmsHtmlGeneratorDatabase.OpenFile(previousCopy).ReadSnapshot("Bản trước"),
                RdbmsHtmlGeneratorDatabase.OpenFile(currentPath).ReadSnapshot("Bản mới")));

            if (diff.IsIdentical)
            {
                DeleteQuietly(previousCopy);
                AppendLog($"Không có thay đổi so với lần đọc trước ({readAt:dd/MM HH:mm}).");
                return;
            }

            AppendLog($"So với lần đọc trước ({readAt:dd/MM HH:mm}): {DescribeChanges(diff)}." +
                      (Options.EffectiveHistoryCount > 0 ? " Bấm \"So với bản trước\" để xem chi tiết." : ""));
            SchemaHistory.Prune(currentPath, Options.EffectiveHistoryCount);
        }
        catch (Exception ex)
        {
            AppendLog($"Không so được với lần đọc trước: {ex.Message}");
        }
    }

    /// <summary>A = bản cũ, B = bản mới: "chỉ có ở B" = bảng mới.</summary>
    private static string DescribeChanges(SchemaDiff diff)
    {
        var parts = new List<string>();
        if (diff.TablesOnlyInB.Count > 0)
        {
            parts.Add($"{diff.TablesOnlyInB.Count} bảng mới");
        }
        if (diff.TablesOnlyInA.Count > 0)
        {
            parts.Add($"{diff.TablesOnlyInA.Count} bảng bị bỏ");
        }
        if (diff.ChangedTables.Count > 0)
        {
            var onlyDescription = diff.ChangedTables.Count(t => t.OnlyDescriptionChanges);
            parts.Add($"{diff.ChangedTables.Count} bảng có thay đổi" + (onlyDescription > 0 ? $" ({onlyDescription} bảng chỉ đổi mô tả)" : ""));
        }
        return string.Join(", ", parts);
    }

    private static void DeleteQuietly(string? path)
    {
        if (path is null)
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
            var path = await Task.Run(() => _htmlGenerator.Generate(_database, CurrentFileBaseName));
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

    // ----- Xuất thêm: Excel định nghĩa bảng, Kiểm tra chất lượng -----

    /// <summary>Data\Database\{tên} テーブル定義書.xlsx từ file .db đang chọn, rồi mở file.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportTableDefinitionAsync()
    {
        IsBusy = true;
        StatusText = "Đang xuất Excel định nghĩa bảng (テーブル定義書)...";
        var path = Path.Combine(RdbmsHtmlGeneratorDatabase.DataDirectory, $"{SafeFileName(CurrentFileBaseName)} テーブル定義書.xlsx");
        try
        {
            var databasePath = _database.DatabasePath;
            var baseName = CurrentFileBaseName;
            var sheets = await Task.Run(() => TableDefinitionWorkbook.Write(
                RdbmsHtmlGeneratorDatabase.OpenFile(databasePath).ReadSnapshot(baseName), baseName, DateTime.Now, path));
            StatusText = $"Đã xuất Excel định nghĩa bảng: {sheets} bảng / view (mỗi bảng 1 sheet). File: {path}";
            AppendLog($"Đã xuất: {path}");
            OpenExported(path);
        }
        catch (IOException ex)
        {
            StatusText = $"Không ghi được {Path.GetFileName(path)} - file đang mở trong Excel? Đóng file rồi xuất lại. ({ex.Message})";
            AppendLog($"LỖI xuất Excel: {ex.Message}");
        }
        catch (Exception ex)
        {
            StatusText = $"Lỗi khi xuất Excel: {ex.Message}";
            AppendLog($"LỖI xuất Excel: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Data\Database\Kiểm tra chất lượng {tên}.html từ file .db đang chọn, rồi mở trong trình duyệt.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task CheckQualityAsync()
    {
        IsBusy = true;
        StatusText = "Đang kiểm tra chất lượng schema...";
        try
        {
            var databasePath = _database.DatabasePath;
            var baseName = CurrentFileBaseName;
            var dictionary = $"{SafeFileName(baseName)}.html";
            var path = Path.Combine(RdbmsHtmlGeneratorDatabase.DataDirectory, $"Kiểm tra chất lượng {SafeFileName(baseName)}.html");
            var report = await Task.Run(() =>
            {
                var result = SchemaQualityChecker.Check(RdbmsHtmlGeneratorDatabase.OpenFile(databasePath).ReadSnapshot(baseName));
                var dictionaryExists = File.Exists(Path.Combine(RdbmsHtmlGeneratorDatabase.DataDirectory, dictionary));
                File.WriteAllText(path, SchemaQualityReport.Build(result, File.GetLastWriteTime(databasePath), DateTime.Now,
                    dictionaryExists ? dictionary : null));
                return result;
            });
            var summary = $"{report.Count(QualitySeverity.Error)} nên sửa, {report.Count(QualitySeverity.Warning)} cân nhắc, " +
                          $"{report.Count(QualitySeverity.Info)} thông tin";
            StatusText = $"Kiểm tra chất lượng {baseName}: {summary}.";
            AppendLog($"Kiểm tra chất lượng: {summary}.");
            AppendLog($"Đã xuất: {path}");
            OpenExported(path);
        }
        catch (Exception ex)
        {
            StatusText = $"Lỗi khi kiểm tra chất lượng: {ex.Message}";
            AppendLog($"LỖI kiểm tra chất lượng: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Mở file vừa xuất bằng chương trình mặc định (máy chưa có Excel thì chỉ ghi nhật ký).</summary>
    private void OpenExported(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog($"Không mở được file ({ex.Message}) - mở tay trong thư mục Data\\Database.");
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
    }

    // ----- So sánh schema -----

    /// <summary>1 file .db có thể so sánh (mỗi lần "1. Đọc database" của 1 hồ sơ / nguồn tạo 1 file; bản cũ nằm trong
    /// Lịch sử - <paramref name="IsHistory"/>).</summary>
    public sealed record SchemaFile(string Name, string Path, DateTime ReadAt, bool IsHistory = false)
    {
        public string Display => IsHistory ? $"{Name}   (bản cũ)" : $"{Name}   (đọc lúc {ReadAt:dd/MM/yyyy HH:mm})";

        /// <summary>ComboBox hiện ToString() (DisplayMemberPath không đọc được record C# thường trong WinUI 3).</summary>
        public override string ToString() => Display;

        /// <summary>Bản cũ của file <paramref name="baseName"/> - tên ghi kèm lúc đọc (vào cả tên trang kết quả).</summary>
        public static SchemaFile FromHistory(string baseName, HistoryEntry entry) =>
            new($"{baseName} - bản {entry.ReadAt:dd-MM-yyyy HH.mm}", entry.Path, entry.ReadAt, IsHistory: true);
    }

    /// <summary>Các file .db trong Data\Database + các bản cũ trong Lịch sử, mới đọc nhất lên trước.</summary>
    public List<SchemaFile> ListSchemaFiles()
    {
        var directory = RdbmsHtmlGeneratorDatabase.DataDirectory;
        if (!Directory.Exists(directory))
        {
            return [];
        }
        var files = new List<SchemaFile>();
        foreach (var path in Directory.GetFiles(directory, "*.db"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            files.Add(new SchemaFile(name, path, File.GetLastWriteTime(path)));
            files.AddRange(SchemaHistory.List(path).Select(entry => SchemaFile.FromHistory(name, entry)));
        }
        files.Sort((x, y) => y.ReadAt.CompareTo(x.ReadAt));
        return files;
    }

    /// <summary>"So với bản trước": A = bản cũ mới nhất trong Lịch sử, B = bản đang dùng → trang So sánh.</summary>
    [RelayCommand(CanExecute = nameof(CanCompareWithPrevious))]
    private async Task CompareWithPreviousAsync()
    {
        RefreshPreviousRead(); // file có thể đã bị xoá tay
        if (PreviousRead is not { } previous)
        {
            StatusText = "Chưa có bản đọc trước để so.";
            return;
        }
        var current = new SchemaFile(CurrentFileBaseName, _database.DatabasePath, File.GetLastWriteTime(_database.DatabasePath));
        if (await CompareSchemasAsync(SchemaFile.FromHistory(CurrentFileBaseName, previous), current) is { } error)
        {
            ErrorOccurred?.Invoke("Không so sánh được", error);
        }
    }

    private bool CanCompareWithPrevious() => !IsBusy && PreviousRead is not null;

    /// <summary>File .db của hồ sơ / nguồn đang chọn (null nếu chưa đọc) - chọn sẵn làm A.</summary>
    public string? CurrentSchemaFilePath => _database.Exists ? _database.DatabasePath : null;

    /// <summary>So sánh A (gốc) với B → Data\Database\So sánh {A} - {B}.html, mở trong trình duyệt. Trả lỗi (null = xong).</summary>
    public async Task<string?> CompareSchemasAsync(SchemaFile a, SchemaFile b)
    {
        if (string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase))
        {
            return "Hãy chọn 2 file khác nhau.";
        }
        IsBusy = true;
        StatusText = $"Đang so sánh {a.Name} với {b.Name}...";
        try
        {
            var (diff, path) = await Task.Run(() =>
            {
                var snapshotA = RdbmsHtmlGeneratorDatabase.OpenFile(a.Path).ReadSnapshot(a.Name);
                var snapshotB = RdbmsHtmlGeneratorDatabase.OpenFile(b.Path).ReadSnapshot(b.Name);
                var result = SchemaComparer.Compare(snapshotA, snapshotB);
                var invalid = Path.GetInvalidFileNameChars();
                var fileName = new string($"So sánh {a.Name} - {b.Name}".Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
                var htmlPath = Path.Combine(RdbmsHtmlGeneratorDatabase.DataDirectory, fileName + ".html");
                File.WriteAllText(htmlPath, SchemaCompareReport.Build(result, DateTime.Now));
                return (result, htmlPath);
            });

            var summary = diff.IsIdentical
                ? "giống nhau"
                : $"{diff.TablesOnlyInA.Count} bảng chỉ có ở A, {diff.TablesOnlyInB.Count} bảng chỉ có ở B, " +
                  $"{diff.ChangedTables.Count} bảng có thay đổi, {diff.UnchangedTableCount} bảng giống nhau";
            StatusText = $"So sánh {a.Name} → {b.Name}: {summary}.";
            AppendLog($"So sánh A = {a.Name} (đọc lúc {a.ReadAt:dd/MM HH:mm}), B = {b.Name} (đọc lúc {b.ReadAt:dd/MM HH:mm}): {summary}.");
            if (!diff.IndexesCompared)
            {
                AppendLog("Chưa so index / ràng buộc: 1 trong 2 file đọc bằng bản cũ - đọc lại file đó để so đủ.");
            }
            AppendLog($"Đã xuất: {path}");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            StatusText = $"Lỗi khi so sánh: {ex.Message}";
            AppendLog($"LỖI so sánh: {ex.Message}");
            return ex.Message;
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
        CompareWithPreviousCommand.NotifyCanExecuteChanged();
        ExportTableDefinitionCommand.NotifyCanExecuteChanged();
        CheckQualityCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSourceChanged(DatabaseSourceType value)
    {
        // Trước RefreshDatabaseTarget: tên file .db / .html lấy theo CurrentProfile.Source. Trước đây Source chỉ đổi
        // trong SaveConfig (gọi sau) → đổi Oracle sang MySQL vẫn trỏ file của Oracle: "2. Xuất HTML" xuất bản Oracle,
        // "1. Đọc database" ghi dữ liệu MySQL đè lên .db của Oracle.
        CurrentProfile.Source = value;
        OnPropertyChanged(nameof(CanImportDatabase));
        ImportDatabaseCommand.NotifyCanExecuteChanged();
        RefreshDatabaseTarget();
        SaveConfig(); // nhớ nguồn đã chọn cho lần mở sau
        StatusText = !CanImportDatabase ? $"Nguồn {SourceDisplayName(value)}: chưa thiết lập kết nối."
            : NextStep == 1 ? $"Nguồn {SourceDisplayName(value)} ({CurrentDatabaseName}): chưa đọc - bấm 1. Đọc database."
            : $"Nguồn {SourceDisplayName(value)} ({CurrentDatabaseName}): đã có dữ liệu đọc lần trước.";
    }

    partial void OnCanExportHtmlChanged(bool value)
    {
        ExportHtmlCommand.NotifyCanExecuteChanged();
        ExportTableDefinitionCommand.NotifyCanExecuteChanged();
        CheckQualityCommand.NotifyCanExecuteChanged();
    }

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
