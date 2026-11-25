using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;
using Npgsql;
using Windows.ApplicationModel.DataTransfer;

namespace DbTools.ViewModels;

/// <summary>1 lựa chọn ở ô "PostgreSQL client": Tự động hoặc 1 thư mục bin cụ thể.</summary>
public sealed record ClientOption(string Label, string Folder)
{
    public override string ToString() => Label;
}

/// <summary>
/// Trang "Sao lưu / Khôi phục": Dump (pg_dump), Restore (pg_restore / psql), Copy DB (cùng server: CREATE DATABASE ... TEMPLATE;
/// khác server: dump ra file tạm rồi restore). Mỗi việc ghi nhật ký + lịch sử chạy. Cài đặt: thư mục lưu dump (mặc định D:\DMP),
/// bộ PostgreSQL client dùng (mặc định tự động = bản mới nhất trên máy).
/// </summary>
public sealed partial class BackupViewModel : ObservableObject
{
    private const int MaxLogChars = 200_000;
    private readonly ILogger _log = AppLog.For("Backup");
    private readonly StringBuilder _logText = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    // ----- Cài đặt chung -----

    [ObservableProperty]
    private string _dumpFolder = AppState.Settings.DumpFolder;

    partial void OnDumpFolderChanged(string value)
    {
        AppState.Settings.DumpFolder = value.Trim();
        AppState.SaveSettings();
        OnPropertyChanged(nameof(DumpFilePreview));
    }

    public ObservableCollection<ClientOption> ClientOptions { get; } = [new("Tự động (bản mới nhất trên máy)", string.Empty)];

    [ObservableProperty]
    private ClientOption? _selectedClient;

    partial void OnSelectedClientChanged(ClientOption? value)
    {
        if (value is null)
        {
            return;
        }
        AppState.Settings.PgBinFolder = value.Folder;
        AppState.SaveSettings();
        _ = RefreshClientAsync();
    }

    [ObservableProperty]
    private string _clientInfo = "Đang tìm pg_dump / pg_restore trên máy...";

    private PgClient? _client;

    // ----- Việc đang chọn: 0 Dump, 1 Restore, 2 Copy DB -----

    [ObservableProperty]
    private int _tab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(DumpCommand), nameof(RestoreCommand), nameof(CopyCommand), nameof(CancelCommand),
        nameof(InspectCommand), nameof(BrowseDumpFolderCommand), nameof(BrowseRestoreFileCommand), nameof(BrowseClientFolderCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private string _status = "Chọn việc: Dump (sao lưu ra file), Restore (khôi phục từ file) hoặc Copy DB.";

    [ObservableProperty]
    private string _logOutput = string.Empty;

    /// <summary>Trang gắn vào: hộp thoại hỏi lại.</summary>
    public Func<RunConfirmation, Task<bool>>? Confirm { get; set; }

    /// <summary>Trang gắn vào: chọn file dump (null = không chọn).</summary>
    public Func<Task<string?>>? PickFile { get; set; }

    // ----- Dump -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DumpFilePreview))]
    private ConnectionProfile? _dumpProfile;

    public ObservableCollection<string> DumpDatabases { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DumpFilePreview))]
    private string _dumpDatabase = string.Empty;

    /// <summary>0 = custom (.dump, pg_restore), 1 = SQL thường (.sql, psql).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DumpFilePreview))]
    private int _dumpFormatIndex;

    [ObservableProperty]
    private bool _dumpSchemaOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DumpFilePreview))]
    private string _dumpSchema = string.Empty;

    private DumpFormat SelectedDumpFormat => DumpFormatIndex == 1 ? DumpFormat.Plain : DumpFormat.Custom;

    public string DumpFilePreview => DumpDatabase.Trim().Length == 0
        ? "Chọn database cần dump."
        : "Ghi ra: " + Path.Combine(DumpFolder.Trim(), PgArgs.DumpFileName(DumpDatabase, SelectedDumpFormat, DateTime.Now, DumpSchema)) + "  (+ file .log cùng tên)";

    partial void OnDumpProfileChanged(ConnectionProfile? value)
    {
        DumpDatabase = value?.Database ?? string.Empty;
        DumpDatabases.Clear();
    }

    // ----- Restore -----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    private string _restoreFile = string.Empty;

    [ObservableProperty]
    private string _restoreInfo = "Chọn file dump (.dump của pg_dump -Fc, hoặc .sql).";

    private DumpArchiveInfo? _restoreArchive;

    [ObservableProperty]
    private ConnectionProfile? _restoreProfile;

    public ObservableCollection<string> RestoreDatabases { get; } = [];

    [ObservableProperty]
    private string _restoreDatabase = string.Empty;

    /// <summary>True = tạo DB mới (tên chưa có) rồi restore vào; false = ghi vào DB có sẵn.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestoreToExisting))]
    private bool _restoreToNew = true;

    public bool RestoreToExisting
    {
        get => !RestoreToNew;
        set => RestoreToNew = !value;
    }

    [ObservableProperty]
    private bool _restoreClean;

    [ObservableProperty]
    private bool _restoreNoOwner;

    [ObservableProperty]
    private bool _restoreSingleTransaction = true;

    /// <summary>Tuỳ chọn chỉ dùng được với file custom / tar (pg_restore); file SQL thường chạy bằng psql.</summary>
    [ObservableProperty]
    private bool _restoreIsArchive = true;

    partial void OnRestoreProfileChanged(ConnectionProfile? value) => RestoreDatabases.Clear();

    partial void OnRestoreFileChanged(string value)
    {
        _restoreArchive = null;
        RestoreInfo = File.Exists(value.Trim().Trim('"')) ? "Bấm Xem nội dung (hoặc chọn lại file) để đọc thông tin file dump." : "Chọn file dump (.dump của pg_dump -Fc, hoặc .sql).";
    }

    // ----- Copy DB -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyModeInfo))]
    private ConnectionProfile? _copySourceProfile;

    public ObservableCollection<string> CopySourceDatabases { get; } = [];

    [ObservableProperty]
    private string _copySourceDatabase = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyModeInfo))]
    private ConnectionProfile? _copyTargetProfile;

    [ObservableProperty]
    private string _copyTargetDatabase = string.Empty;

    [ObservableProperty]
    private bool _copyNoOwner = true;

    public bool CopySameServer => CopySourceProfile is not null && CopyTargetProfile is not null && PgAdmin.SameServer(CopySourceProfile, CopyTargetProfile);

    public string CopyModeInfo => CopySourceProfile is null || CopyTargetProfile is null
        ? "Chọn server nguồn và đích."
        : CopySameServer
            ? "Cùng server: copy nhanh bằng CREATE DATABASE <mới> TEMPLATE <nguồn> (chép nguyên khối). DB nguồn không được có phiên nào khác đang kết nối - có thì app hỏi ngắt phiên."
            : $"Khác server: pg_dump DB nguồn ra file tạm trong {DumpFolder.Trim()}, tạo DB mới trên server đích rồi pg_restore vào; xong thì xoá file tạm.";

    partial void OnCopySourceProfileChanged(ConnectionProfile? value)
    {
        CopySourceDatabase = value?.Database ?? string.Empty;
        CopySourceDatabases.Clear();
    }

    partial void OnCopySourceDatabaseChanged(string value)
    {
        if (value.Trim().Length > 0)
        {
            CopyTargetDatabase = $"{value.Trim()}_copy_{DateTime.Now:yyyyMMdd}";
        }
    }

    public BackupViewModel()
    {
        var first = AppState.LastProfile;
        DumpProfile = RestoreProfile = CopySourceProfile = CopyTargetProfile = first;
        if (AppState.Settings.PgBinFolder.Length > 0)
        {
            ClientOptions.Add(new ClientOption(AppState.Settings.PgBinFolder, AppState.Settings.PgBinFolder));
            SelectedClient = ClientOptions[1];
        }
        else
        {
            SelectedClient = ClientOptions[0];
        }
    }

    /// <summary>Quay lại trang: hồ sơ đang chọn đã bị xoá thì chọn lại; thư mục dump / client có thể vừa đổi ở trang Cài đặt.</summary>
    public void EnsureProfiles()
    {
        if (DumpFolder.Trim() != AppState.Settings.DumpFolder)
        {
            DumpFolder = AppState.Settings.DumpFolder;
        }
        var bin = AppState.Settings.PgBinFolder;
        if (!string.Equals(SelectedClient?.Folder ?? string.Empty, bin, StringComparison.OrdinalIgnoreCase))
        {
            var option = ClientOptions.FirstOrDefault(o => string.Equals(o.Folder, bin, StringComparison.OrdinalIgnoreCase));
            if (option is null)
            {
                option = new ClientOption(bin, bin);
                ClientOptions.Add(option);
            }
            SelectedClient = option;
        }
        var first = AppState.LastProfile;
        ConnectionProfile? Fix(ConnectionProfile? p) => p is not null && Profiles.Contains(p) ? p : first;
        DumpProfile = Fix(DumpProfile);
        RestoreProfile = Fix(RestoreProfile);
        CopySourceProfile = Fix(CopySourceProfile);
        CopyTargetProfile = Fix(CopyTargetProfile);
    }

    /// <summary>Tìm bộ client trên máy (đầu trang / đổi lựa chọn): thêm các bản tìm được vào ô chọn, báo bản đang dùng.</summary>
    public async Task RefreshClientAsync()
    {
        try
        {
            var found = await PgTools.FindAsync();
            foreach (var c in found.Where(c => !ClientOptions.Any(o => string.Equals(o.Folder, c.BinFolder, StringComparison.OrdinalIgnoreCase))))
            {
                ClientOptions.Add(new ClientOption(c.Display, c.BinFolder));
            }
            var folder = SelectedClient?.Folder ?? string.Empty;
            _client = folder.Length == 0 ? PgVersion.Choose(found) : await PgTools.ProbeAsync(folder);
            ClientInfo = _client is null
                ? (folder.Length == 0
                    ? "Không tìm thấy pg_dump.exe (Program Files\\PostgreSQL\\*\\bin, PATH) - cài PostgreSQL (bản client) hoặc Chọn thư mục bin."
                    : $"Thư mục {folder} không có pg_dump.exe chạy được.")
                : $"Dùng pg_dump / pg_restore / psql bản {_client.Version.Major}.{_client.Version.Minor} ở {_client.BinFolder}";
        }
        catch (Exception ex)
        {
            ClientInfo = "Không tìm được PostgreSQL client: " + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseClientFolderAsync()
    {
        if (await Shell.PickFolderAsync() is not { } folder)
        {
            return;
        }
        if (File.Exists(Path.Combine(folder, "bin", "pg_dump.exe")))
        {
            folder = Path.Combine(folder, "bin"); // chọn thư mục cài (C:\...\PostgreSQL\17) cũng được
        }
        var option = ClientOptions.FirstOrDefault(o => string.Equals(o.Folder, folder, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            option = new ClientOption(folder, folder);
            ClientOptions.Add(option);
        }
        SelectedClient = option;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseDumpFolderAsync()
    {
        if (await Shell.PickFolderAsync() is { } folder)
        {
            DumpFolder = folder;
        }
    }

    [RelayCommand]
    private void OpenDumpFolder()
    {
        if (Directory.Exists(DumpFolder.Trim()))
        {
            Shell.Open(DumpFolder.Trim());
        }
        else
        {
            Status = $"Chưa có thư mục {DumpFolder.Trim()} (sẽ tự tạo khi Dump).";
        }
    }

    /// <summary>Danh sách database trên server của hồ sơ (mở ô database). Không kết nối được thì để trống - vẫn gõ tay được.</summary>
    public async Task LoadDatabasesAsync(ConnectionProfile? profile, ObservableCollection<string> target)
    {
        if (profile is null)
        {
            return;
        }
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(10));
            foreach (var db in (await PgAdmin.ListDatabasesAsync(profile, cts.Token)).Where(db => !target.Contains(db)))
            {
                target.Add(db);
            }
        }
        catch (Exception ex)
        {
            _log.LogInformation("Không đọc được danh sách database: {Error}", Pg.Describe(ex));
        }
    }

    // ----- Dump -----

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task DumpAsync() => RunJobAsync("Dump", async (record, ct) =>
    {
        var profile = DumpProfile ?? throw new UserError("Chưa chọn kết nối - thêm ở trang Kết nối.");
        var database = DumpDatabase.Trim();
        if (database.Length == 0)
        {
            throw new UserError("Chưa nhập database cần dump.");
        }
        var client = RequireClient();
        await CheckClientVersionAsync(profile, client, ct);
        var folder = EnsureDumpFolder();
        var options = new DumpOptions(SelectedDumpFormat, DumpSchemaOnly, DumpSchema.Trim().Length > 0 ? DumpSchema.Trim() : null);
        var file = PgArgs.UniquePath(folder, PgArgs.DumpFileName(database, options.Format, DateTime.Now, options.Schema));
        var target = PgAdmin.Target(profile, database);
        record.Target = $"{target.Host}:{target.Port}/{database}";
        record.User = target.User;
        record.Folder = file;
        record.LogPath = Path.ChangeExtension(file, ".log");
        AppendLog($"Dump {target.Display} → {file}");
        AppendLog($"Kiểu: {(options.Format == DumpFormat.Custom ? "custom (-Fc)" : "SQL thường (-Fp)")}{(options.SchemaOnly ? ", chỉ cấu trúc (-s)" : string.Empty)}{(options.Schema is null ? string.Empty : $", chỉ schema {options.Schema}")}");
        var code = await RunToolAsync(client.Exe("pg_dump"), PgArgs.Dump(target, options, file), profile.Password, ct);
        if (code != 0)
        {
            TryDeleteFile(file);
            throw new UserError($"pg_dump lỗi (mã {code}) - xem nhật ký. Đã xoá file dump dở dang.");
        }
        var size = new FileInfo(file).Length;
        AppendLog($"Xong: {file} ({FormatSize(size)})");
        return $"Đã dump {database} → {Path.GetFileName(file)} ({FormatSize(size)}).";
    });

    // ----- Restore -----

    private bool CanInspect => IsIdle && RestoreFile.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseRestoreFileAsync()
    {
        if (PickFile is not null && await PickFile() is { } file)
        {
            RestoreFile = file;
            await InspectAsync();
        }
    }

    /// <summary>Đọc thông tin file dump (pg_restore -l / phần đầu file .sql) để hiện dưới ô chọn file.</summary>
    [RelayCommand(CanExecute = nameof(CanInspect))]
    private async Task InspectAsync()
    {
        var file = RestoreFile.Trim().Trim('"');
        if (!File.Exists(file))
        {
            RestoreInfo = "Không thấy file: " + file;
            _restoreArchive = null;
            return;
        }
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(1 * 60));
            _restoreArchive = await PgTools.InspectAsync(_client, file, cts.Token);
            RestoreIsArchive = !_restoreArchive.IsPlainSql;
            RestoreInfo = _restoreArchive.Summary() + $"{Environment.NewLine}Dung lượng {FormatSize(new FileInfo(file).Length)}";
            if (RestoreDatabase.Trim().Length == 0)
            {
                RestoreDatabase = _restoreArchive.DbName.Length > 0 ? _restoreArchive.DbName : Path.GetFileNameWithoutExtension(file);
            }
        }
        catch (Exception ex)
        {
            _restoreArchive = null;
            RestoreInfo = "Không đọc được file dump: " + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task RestoreAsync() => RunJobAsync("Restore", async (record, ct) =>
    {
        var profile = RestoreProfile ?? throw new UserError("Chưa chọn kết nối - thêm ở trang Kết nối.");
        var file = RestoreFile.Trim().Trim('"');
        var database = RestoreDatabase.Trim();
        if (!File.Exists(file))
        {
            throw new UserError("Chưa chọn file dump / không thấy file: " + file);
        }
        if (database.Length == 0)
        {
            throw new UserError("Chưa nhập database đích.");
        }
        var client = RequireClient();
        var info = _restoreArchive ?? await PgTools.InspectAsync(client, file, ct);
        _restoreArchive = info;
        var target = PgAdmin.Target(profile, database);
        record.Target = $"{target.Host}:{target.Port}/{database}";
        record.User = target.User;
        record.Folder = file;

        var exists = await PgAdmin.DatabaseExistsAsync(profile, database, ct);
        if (RestoreToNew && exists)
        {
            throw new UserError($"Database {database} đã có trên {profile.Host}:{profile.Port} - đặt tên khác, hoặc chọn \"Ghi vào DB có sẵn\".");
        }
        if (!RestoreToNew && !exists)
        {
            throw new UserError($"Chưa có database {database} trên {profile.Host}:{profile.Port} - chọn \"Tạo DB mới\".");
        }
        var options = RestoreOptionsFor(info.IsPlainSql);
        var lines = new List<string>
        {
            $"File: {file}",
            info.Summary(),
            $"Đích: {target.Display}" + (RestoreToNew ? " - tạo database mới (trống, từ template0)" : " - ghi vào database đang có"),
            options.SingleTransaction
                ? "Lỗi là huỷ hết (1 transaction): DB đích không bị dở dang."
                : "⚠ Không dùng 1 transaction: lỗi giữa chừng thì phần đã restore vẫn còn.",
        };
        if (!RestoreToNew)
        {
            lines.Add(options.Clean
                ? "⚠ Xoá đối tượng trùng tên trước (--clean --if-exists): bảng trong DB đích trùng tên với file dump sẽ bị xoá cả dữ liệu rồi tạo lại."
                : "Không xoá đối tượng cũ: bảng / hàm trùng tên với file dump sẽ báo lỗi \"already exists\".");
        }
        var otherOwners = info.Owners.Where(o => o != target.User).ToList();
        if (otherOwners.Count > 0 && !options.NoOwner && !info.IsPlainSql)
        {
            lines.Add($"⚠ File dump có owner {string.Join(", ", otherOwners)} khác user {target.User}: user đó chưa có trên server đích thì sẽ lỗi - bật \"Bỏ owner / quyền\".");
        }
        if (Confirm is not null && !await Confirm(new RunConfirmation($"Restore vào {database}?", lines, "Restore",
                ProductionGuard.ForRestore(profile.IsProduction, database, profile.Name))))
        {
            throw new UserError("Chưa restore gì.", cancelled: true);
        }
        record.LogPath = AppState.History.NewLogPath(DateTime.Now);
        if (RestoreToNew)
        {
            AppendLog(PgSql.CreateDatabase(database));
            await PgAdmin.CreateDatabaseAsync(profile, database, ct);
        }
        AppendLog($"Restore {file} → {target.Display}");
        var code = info.IsPlainSql
            ? await RunToolAsync(client.Exe("psql"), PgArgs.Psql(target, options.SingleTransaction, file), profile.Password, ct)
            : await RunToolAsync(client.Exe("pg_restore"), PgArgs.Restore(target, options, file), profile.Password, ct);
        if (code != 0)
        {
            throw new UserError($"{(info.IsPlainSql ? "psql" : "pg_restore")} lỗi (mã {code}) - xem nhật ký."
                + (options.SingleTransaction ? " Đã rollback: DB đích không đổi." : " Một phần có thể đã được restore.")
                + (RestoreToNew ? $" Database {database} đã được tạo (trống) - xoá tay nếu không dùng." : string.Empty));
        }
        AppendLog("Xong.");
        return $"Đã restore {Path.GetFileName(file)} vào {database}.";
    });

    // ----- Copy DB -----

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task CopyAsync() => RunJobAsync("Copy DB", async (record, ct) =>
    {
        var source = CopySourceProfile ?? throw new UserError("Chưa chọn server nguồn.");
        var destination = CopyTargetProfile ?? throw new UserError("Chưa chọn server đích.");
        var sourceDb = CopySourceDatabase.Trim();
        var targetDb = CopyTargetDatabase.Trim();
        if (sourceDb.Length == 0 || targetDb.Length == 0)
        {
            throw new UserError("Nhập đủ database nguồn và tên database mới.");
        }
        var same = CopySameServer;
        if (same && sourceDb == targetDb)
        {
            throw new UserError("Tên database mới trùng database nguồn.");
        }
        record.Target = $"{destination.Host}:{destination.Port}/{targetDb}";
        record.User = destination.Username;
        record.Folder = $"{source.Host}:{source.Port}/{sourceDb}";
        if (!await PgAdmin.DatabaseExistsAsync(source, sourceDb, ct))
        {
            throw new UserError($"Không có database {sourceDb} trên {source.Host}:{source.Port}.");
        }
        if (await PgAdmin.DatabaseExistsAsync(destination, targetDb, ct))
        {
            throw new UserError($"Database {targetDb} đã có trên {destination.Host}:{destination.Port} - đặt tên khác.");
        }
        record.LogPath = AppState.History.NewLogPath(DateTime.Now);
        var lines = new List<string>
        {
            $"Nguồn: {source.Username}@{source.Host}:{source.Port}/{sourceDb}",
            $"Đích: {destination.Username}@{destination.Host}:{destination.Port}/{targetDb} (database mới)",
        };
        if (same)
        {
            var sessions = await PgAdmin.ListSessionsAsync(source, sourceDb, ct);
            lines.Add("Cách copy: CREATE DATABASE ... TEMPLATE (cùng server, rất nhanh).");
            if (sessions.Count > 0)
            {
                lines.Add($"⚠ DB nguồn đang có {sessions.Count} phiên kết nối ({Describe(sessions)}). Copy bằng TEMPLATE cần ngắt hết - bấm \"Ngắt phiên rồi copy\".");
            }
            if (Confirm is not null && !await Confirm(new RunConfirmation($"Copy {sourceDb} → {targetDb}?", lines, sessions.Count > 0 ? "Ngắt phiên rồi copy" : "Copy",
                    ProductionGuard.ForCopy(source.IsProduction, destination.IsProduction, sessions.Count > 0, sourceDb, targetDb))))
            {
                throw new UserError("Chưa copy gì.", cancelled: true);
            }
            var consent = sessions.Count > 0; // người dùng đã đồng ý ngắt phiên
            for (var attempt = 1; ; attempt++)
            {
                if (consent)
                {
                    var killed = await PgAdmin.TerminateDatabaseAsync(source, sourceDb, ct);
                    AppendLog($"Ngắt {killed} phiên đang kết nối vào {sourceDb}.");
                    await Task.Delay(500, ct);
                }
                try
                {
                    AppendLog(PgSql.CopyDatabase(sourceDb, targetDb));
                    await PgAdmin.CopyDatabaseAsync(source, sourceDb, targetDb, ct);
                    break;
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ObjectInUse && consent && attempt < 3)
                {
                    AppendLog($"DB nguồn vẫn có phiên kết nối ({ex.MessageText}) - ngắt lại...");
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ObjectInUse)
                {
                    throw new UserError($"DB nguồn đang có phiên kết nối ({ex.MessageText}) - bấm Copy lại (app sẽ hỏi ngắt phiên) hoặc ngắt ở trang Phiên kết nối.");
                }
            }
            AppendLog("Xong.");
            return $"Đã copy {sourceDb} → {targetDb} (TEMPLATE).";
        }

        var client = RequireClient();
        await CheckClientVersionAsync(source, client, ct);
        lines.Add($"Cách copy: pg_dump ra file tạm trong {DumpFolder.Trim()} → tạo database mới trên server đích → pg_restore (1 transaction) → xoá file tạm.");
        if (CopyNoOwner)
        {
            lines.Add("Bỏ owner / quyền (--no-owner --no-privileges): đối tượng thuộc user của server đích.");
        }
        if (Confirm is not null && !await Confirm(new RunConfirmation($"Copy {sourceDb} → {destination.Host}/{targetDb}?", lines, "Copy",
                ProductionGuard.ForCopy(source.IsProduction, destination.IsProduction, killsSourceSessions: false, sourceDb, targetDb))))
        {
            throw new UserError("Chưa copy gì.", cancelled: true);
        }
        var temp = CopyTempPath(sourceDb, EnsureDumpFolder());
        try
        {
            AppendLog($"1/3 pg_dump {source.Host}:{source.Port}/{sourceDb} → {temp}");
            var code = await RunToolAsync(client.Exe("pg_dump"), PgArgs.Dump(PgAdmin.Target(source, sourceDb), new DumpOptions(DumpFormat.Custom), temp), source.Password, ct);
            if (code != 0)
            {
                throw new UserError($"pg_dump lỗi (mã {code}) - xem nhật ký. Chưa tạo gì trên server đích.");
            }
            AppendLog($"2/3 {PgSql.CreateDatabase(targetDb)} trên {destination.Host}:{destination.Port}");
            await PgAdmin.CreateDatabaseAsync(destination, targetDb, ct);
            AppendLog($"3/3 pg_restore → {destination.Host}:{destination.Port}/{targetDb}");
            code = await RunToolAsync(client.Exe("pg_restore"),
                PgArgs.Restore(PgAdmin.Target(destination, targetDb), new RestoreOptions(NoOwner: CopyNoOwner), temp), destination.Password, ct);
            if (code != 0)
            {
                throw new UserError($"pg_restore lỗi (mã {code}) - xem nhật ký. Đã rollback; database {targetDb} đã tạo (trống) trên server đích - xoá tay nếu không dùng.");
            }
        }
        finally
        {
            TryDeleteFile(temp);
        }
        AppendLog("Xong, đã xoá file tạm.");
        return $"Đã copy {sourceDb} → {destination.Host}:{destination.Port}/{targetDb}.";
    });

    private static string Describe(List<PgSession> sessions) =>
        string.Join(", ", sessions.Take(5).Select(s => $"{s.User}{(s.Application.Length > 0 ? " / " + s.Application : string.Empty)} từ {s.Client}"))
        + (sessions.Count > 5 ? ", ..." : string.Empty);

    // ----- Xem lệnh: các bước sẽ chạy, dạng script .bat (không có mật khẩu) -----

    /// <summary>Trang gắn vào: hiện script (tiêu đề, nội dung) - có nút Copy.</summary>
    public Func<string, string, Task>? ShowCommands { get; set; }

    /// <summary>Tuỳ chọn restore thật sự dùng (chung cho Restore và Xem lệnh): --clean chỉ khi ghi vào DB có sẵn; file .sql (psql) không
    /// có --clean / --no-owner.</summary>
    private RestoreOptions RestoreOptionsFor(bool plainSql) =>
        new(RestoreClean && !RestoreToNew && !plainSql, RestoreNoOwner && !plainSql, RestoreSingleTransaction);

    [RelayCommand]
    private Task PreviewDumpAsync()
    {
        if (DumpProfile is not { } profile || DumpDatabase.Trim().Length == 0)
        {
            return ShowAsync("Xem lệnh - Dump", "Chọn server và database cần dump trước.");
        }
        var database = DumpDatabase.Trim();
        var options = new DumpOptions(SelectedDumpFormat, DumpSchemaOnly, DumpSchema.Trim().Length > 0 ? DumpSchema.Trim() : null);
        var file = PgArgs.UniquePath(DumpFolder.Trim(), PgArgs.DumpFileName(database, options.Format, DateTime.Now, options.Schema));
        return ShowAsync("Xem lệnh - Dump", CommandPreview.ForDump(_client, profile.Name, PgAdmin.Target(profile, database), options, file));
    }

    [RelayCommand]
    private Task PreviewRestoreAsync()
    {
        var file = RestoreFile.Trim().Trim('"');
        if (RestoreProfile is not { } profile || RestoreDatabase.Trim().Length == 0 || file.Length == 0)
        {
            return ShowAsync("Xem lệnh - Restore", "Chọn file dump, server và database đích trước.");
        }
        var plain = _restoreArchive?.IsPlainSql ?? IsPlainSqlFile(file);
        return ShowAsync("Xem lệnh - Restore", CommandPreview.ForRestore(_client, profile.Name, PgAdmin.Target(profile, RestoreDatabase.Trim()),
            RestoreOptionsFor(plain), file, plain, RestoreToNew));
    }

    [RelayCommand]
    private Task PreviewCopyAsync()
    {
        if (CopySourceProfile is not { } source || CopyTargetProfile is not { } destination
            || CopySourceDatabase.Trim().Length == 0 || CopyTargetDatabase.Trim().Length == 0)
        {
            return ShowAsync("Xem lệnh - Copy DB", "Nhập đủ server / database nguồn và tên database mới trước.");
        }
        var sourceDb = CopySourceDatabase.Trim();
        var targetDb = CopyTargetDatabase.Trim();
        var text = CopySameServer
            ? CommandPreview.ForCopySameServer(_client, source.Name, PgAdmin.Target(source, sourceDb), sourceDb, targetDb)
            : CommandPreview.ForCopyOtherServer(_client, source.Name, PgAdmin.Target(source, sourceDb), destination.Name,
                PgAdmin.Target(destination, targetDb), CopyNoOwner, CopyTempPath(sourceDb, DumpFolder.Trim()));
        return ShowAsync("Xem lệnh - Copy DB", text);
    }

    private Task ShowAsync(string title, string text) => ShowCommands?.Invoke(title, text) ?? Task.CompletedTask;

    private static string CopyTempPath(string sourceDb, string folder) =>
        PgArgs.UniquePath(folder, $"copy_{PgArgs.SafeFileName(sourceDb)}_{DateTime.Now:yyyyMMdd_HHmmss}.dump");

    /// <summary>Chưa bấm Xem nội dung: đoán kiểu file theo vài byte đầu (không có chữ ký PGDMP / tar = SQL thường).</summary>
    private static bool IsPlainSqlFile(string file)
    {
        try
        {
            var head = new byte[512];
            using var stream = File.OpenRead(file);
            var n = stream.Read(head, 0, head.Length);
            return !DumpArchiveInfo.IsArchive(head.AsSpan(0, n));
        }
        catch (Exception)
        {
            return Path.GetExtension(file).Equals(".sql", StringComparison.OrdinalIgnoreCase);
        }
    }

    // ----- Chạy 1 việc: trạng thái, nhật ký, lịch sử -----

    /// <summary>Lỗi đã biết, báo thẳng cho người dùng (không cần chi tiết kỹ thuật). <paramref name="cancelled"/>: người dùng bấm Huỷ ở
    /// hộp thoại hỏi lại - không ghi lịch sử.</summary>
    private sealed class UserError(string message, bool cancelled = false) : Exception(message)
    {
        public bool Cancelled { get; } = cancelled;
    }

    private async Task RunJobAsync(string mode, Func<RunRecord, CancellationToken, Task<string>> job)
    {
        IsBusy = true;
        ClearLog();
        var started = DateTime.Now;
        var record = new RunRecord { Time = started, Mode = mode, Schema = string.Empty };
        var watch = Stopwatch.StartNew();
        var save = true;
        _cts = new CancellationTokenSource();
        CancelCommand.NotifyCanExecuteChanged();
        Status = $"Đang {mode}...";
        try
        {
            var message = await job(record, _cts.Token);
            record.Success = true;
            Status = message;
        }
        catch (UserError ex) when (ex.Cancelled)
        {
            save = false;
            AppendLog(ex.Message);
            Status = ex.Message;
        }
        catch (OperationCanceledException)
        {
            record.Error = "Đã huỷ";
            AppendLog($"Đã huỷ ({mode}) - tiến trình đã dừng.");
            Status = $"Đã huỷ {mode}.";
        }
        catch (Exception ex)
        {
            record.Error = ex is UserError ? ex.Message : Pg.Describe(ex);
            AppendLog("✗ " + record.Error);
            Status = $"{mode} thất bại: {record.Error}";
            if (ex is not UserError)
            {
                _log.LogError(ex, "{Mode} lỗi", mode);
            }
        }
        finally
        {
            record.DurationMs = watch.ElapsedMilliseconds;
            _cts.Dispose();
            _cts = null;
            if (save && record.Target.Length > 0)
            {
                SaveHistory(record);
            }
            _log.LogInformation("{Mode} {Target}: {Status}", mode, record.Target, Status);
            IsBusy = false;
        }
    }

    private void SaveHistory(RunRecord record)
    {
        try
        {
            if (record.LogPath.Length == 0)
            {
                record.LogPath = AppState.History.NewLogPath(record.Time);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(record.LogPath)!);
            var header = $"DbTools - {record.Mode}{Environment.NewLine}Thời gian: {record.Time:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
                + $"Kết quả: {(record.Success ? "Đạt" : "Thất bại")} {record.Error}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}";
            File.WriteAllText(record.LogPath, header + _logText, new UTF8Encoding(true));
            AppState.History.Add(record);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Không ghi được lịch sử / log");
        }
    }

    /// <summary>Chạy 1 công cụ; dòng in ra gom lại, cứ 0,25 giây đẩy lên nhật ký 1 lần (pg_restore -v in hàng nghìn dòng).</summary>
    private async Task<int> RunToolAsync(string exe, List<string> args, string password, CancellationToken ct)
    {
        AppendLog($"> {Path.GetFileName(exe)} {string.Join(' ', args.Select(a => a.Contains(' ') || a.Contains('"') ? "'" + a + "'" : a))}");
        var pending = new ConcurrentQueue<string>();
        var task = Task.Run(() => PgTools.RunAsync(exe, args, password, pending.Enqueue, ct), ct);
        while (!task.IsCompleted)
        {
            await Task.WhenAny(task, Task.Delay(250, CancellationToken.None));
            Flush();
        }
        Flush();
        return await task;

        void Flush()
        {
            if (pending.IsEmpty)
            {
                return;
            }
            var sb = new StringBuilder();
            while (pending.TryDequeue(out var line))
            {
                sb.AppendLine("  " + line);
            }
            _logText.Append(sb);
            ShowLog();
        }
    }

    private PgClient RequireClient() => _client ?? throw new UserError(
        "Không tìm thấy pg_dump / pg_restore - cài PostgreSQL (bản client) hoặc chọn thư mục bin ở ô PostgreSQL client.");

    private async Task CheckClientVersionAsync(ConnectionProfile profile, PgClient client, CancellationToken ct)
    {
        var major = await PgAdmin.ServerMajorAsync(profile, ct);
        AppendLog($"Server PostgreSQL {major}, pg_dump {client.Version.Major}.{client.Version.Minor} ({client.BinFolder})");
        if (PgVersion.Mismatch(client, major) is { } warning)
        {
            throw new UserError(warning);
        }
    }

    private string EnsureDumpFolder()
    {
        var folder = DumpFolder.Trim();
        if (folder.Length == 0)
        {
            throw new UserError("Chưa đặt thư mục lưu file dump.");
        }
        try
        {
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception ex)
        {
            throw new UserError($"Không tạo được thư mục lưu dump {folder}: {ex.Message} - đổi ở ô Thư mục lưu file dump.");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // để lại
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };

    private bool CanCancel => IsBusy && _cts is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        Status = "Đang huỷ...";
    }

    [RelayCommand]
    private void CopyLog()
    {
        var package = new DataPackage();
        package.SetText(_logText.ToString());
        Clipboard.SetContent(package);
        Status = "Đã chép nhật ký.";
    }

    private void ClearLog()
    {
        _logText.Clear();
        LogOutput = string.Empty;
    }

    private void AppendLog(string line)
    {
        _logText.AppendLine(line);
        ShowLog();
    }

    /// <summary>Ô nhật ký chỉ hiện phần cuối (file log vẫn đủ): TextBox chậm hẳn khi chữ quá dài.</summary>
    private void ShowLog() => LogOutput = _logText.Length <= MaxLogChars
        ? _logText.ToString()
        : "... (đầu nhật ký bị ẩn - xem đủ trong file log)" + Environment.NewLine + _logText.ToString(_logText.Length - MaxLogChars, MaxLogChars);
}
