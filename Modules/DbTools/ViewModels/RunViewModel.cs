using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;

namespace DbTools.ViewModels;

/// <summary>Nội dung hộp thoại hỏi lại trước khi Chạy thật.</summary>
public sealed record RunConfirmation(string Title, IReadOnlyList<string> Lines, string PrimaryText);

/// <summary>
/// Trang "Chạy thư mục SQL": đọc thư mục → danh sách file theo thứ tự chạy (tự sắp / chỉnh tay, nhớ theo thư mục) →
/// Kiểm tra (không ghi DB) / Chạy thử (rollback cuối) / Chạy (1 transaction, lỗi là rollback hết). Mỗi lần chạy ghi lịch sử + log.
/// </summary>
public sealed partial class RunViewModel : ObservableObject
{
    private readonly ILogger _log = AppLog.For("Run");
    private readonly StringBuilder _logText = new();
    private CancellationTokenSource? _cts;
    private bool _suspendOrderSave;

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    public ObservableCollection<FileRow> Files { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadFolderCommand))]
    private string _folder = AppState.Settings.LastFolder;

    [ObservableProperty]
    private bool _includeSubfolders = AppState.Settings.IncludeSubfolders;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileInfo), nameof(SchemaInput), nameof(IncludePublic), nameof(HasSchema))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(DryRunCommand), nameof(RunCommand))]
    private ConnectionProfile? _selectedProfile;

    /// <summary>Schema có trong DB đang chọn (ô Schema, gõ tay cũng được).</summary>
    public ObservableCollection<string> Schemas { get; } = [];

    /// <summary>Schema chạy script, nhớ theo hồ sơ. Để trống = mặc định (search_path của server / user, thường là public).</summary>
    public string SchemaInput
    {
        get => SelectedProfile?.Schema ?? string.Empty;
        set
        {
            var schema = value?.Trim() ?? string.Empty;
            if (SelectedProfile is not null && SelectedProfile.Schema != schema)
            {
                SelectedProfile.Schema = schema;
            }
        }
    }

    /// <summary>Đã chọn schema: tìm cả trong public (search_path = schema, public). Nhớ theo hồ sơ.</summary>
    public bool IncludePublic
    {
        get => SelectedProfile?.IncludePublic ?? true;
        set
        {
            if (SelectedProfile is not null)
            {
                SelectedProfile.IncludePublic = value;
            }
        }
    }

    public bool HasSchema => SelectedProfile?.SchemaOrNull is { } s && s != "public";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDetail))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(RemoveCommand), nameof(OpenFileCommand))]
    private FileRow? _selectedFile;

    /// <summary>Chạy script xoá ngược (DROP ... IF EXISTS) ngay trước, trong cùng transaction. Không nhớ giữa các lần mở app.</summary>
    [ObservableProperty]
    private bool _dropFirst;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(LoadFolderCommand), nameof(BrowseFolderCommand), nameof(AutoSortCommand), nameof(CheckCommand),
        nameof(DryRunCommand), nameof(RunCommand), nameof(CancelCommand), nameof(MoveUpCommand), nameof(MoveDownCommand),
        nameof(RemoveCommand), nameof(SaveDropScriptCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Chọn thư mục chứa file .sql rồi bấm Đọc thư mục.";

    [ObservableProperty]
    private string _summary = "Chưa đọc thư mục.";

    [ObservableProperty]
    private string _orderNote = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _logOutput = string.Empty;

    /// <summary>Trang gắn vào: hộp thoại hỏi lại trước khi Chạy thật.</summary>
    public Func<RunConfirmation, Task<bool>>? Confirm { get; set; }

    public bool IsIdle => !IsBusy;

    public bool IsEmpty => Files.Count == 0;

    public string ProfileInfo => SelectedProfile is null
        ? "Chưa có kết nối - thêm ở trang Kết nối."
        : $"{SelectedProfile.Username}@{SelectedProfile.Target} · {SelectedProfile.SchemaText}";

    public string SelectedDetail => SelectedFile is null
        ? "Chọn 1 file để xem chi tiết kiểm tra / lỗi."
        : $"{SelectedFile.RelativePath} · {SelectedFile.StatementCount} câu · {SelectedFile.File.EncodingName}"
          + (SelectedFile.Detail.Length > 0 ? Environment.NewLine + SelectedFile.Detail : string.Empty);

    public RunViewModel()
    {
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == AppState.Settings.LastProfile) ?? Profiles.FirstOrDefault();
        Files.CollectionChanged += Files_CollectionChanged;
    }

    /// <summary>Quay lại trang (vd sau khi thêm / xoá hồ sơ ở trang Kết nối): chưa chọn hoặc hồ sơ đang chọn đã bị xoá thì
    /// chọn hồ sơ lần trước / hồ sơ đầu. Gọi lúc trang hiện (không gọi giữa lúc danh sách đang đổi: ComboBox chưa có phần tử
    /// mới thì không hiện được lựa chọn).</summary>
    public void EnsureProfile()
    {
        if (SelectedProfile is null || !Profiles.Contains(SelectedProfile))
        {
            SelectedProfile = Profiles.FirstOrDefault(p => p.Name == AppState.Settings.LastProfile) ?? Profiles.FirstOrDefault();
        }
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? oldValue, ConnectionProfile? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= SelectedProfile_PropertyChanged;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += SelectedProfile_PropertyChanged;
        }
        Schemas.Clear();
        _ = LoadSchemasAsync();
    }

    /// <summary>Sửa hồ sơ đang chọn (trang Kết nối, ô Schema) → dòng user@host:port/db · schema cập nhật theo.</summary>
    private void SelectedProfile_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ProfileInfo));
        if (e.PropertyName is nameof(ConnectionProfile.Schema))
        {
            OnPropertyChanged(nameof(HasSchema));
            OnPropertyChanged(nameof(SchemaInput));
        }
    }

    /// <summary>Đọc danh sách schema của DB cho ô Schema (mở ô / đổi hồ sơ). Không kết nối được thì để trống - vẫn gõ tay được.</summary>
    public async Task LoadSchemasAsync()
    {
        if (SelectedProfile is not { } profile || profile.Database.Trim().Length == 0)
        {
            return;
        }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var schemas = await PgCatalog.ListSchemasAsync(profile, cts.Token);
            if (SelectedProfile != profile)
            {
                return;
            }
            var keep = SchemaInput;
            foreach (var s in schemas.Where(s => !Schemas.Contains(s)))
            {
                Schemas.Add(s);
            }
            if (SchemaInput != keep)
            {
                SchemaInput = keep; // ComboBox có thể đổi chữ khi danh sách đổi
            }
        }
        catch (Exception ex)
        {
            _log.LogInformation("Không đọc được danh sách schema: {Error}", Pg.Describe(ex));
        }
    }

    /// <summary>Schema đã chọn phải có trong DB (không tự tạo - tránh chạy vào schema gõ nhầm tên). False → đã báo lỗi.</summary>
    private async Task<bool> EnsureSchemaExistsAsync(ConnectionProfile profile, CancellationToken ct)
    {
        if (profile.SchemaOrNull is not { } schema || await PgCatalog.SchemaExistsAsync(profile, ct))
        {
            return true;
        }
        AppendLog($"✗ Schema \"{schema}\" chưa có trong database {profile.Target}. Tạo trước (CREATE SCHEMA {Pg.QuoteIdent(schema)}) hoặc chọn schema khác.");
        Status = $"Schema \"{schema}\" chưa có trong database - tạo trước hoặc chọn schema khác.";
        return false;
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value is not null)
        {
            AppState.Settings.LastProfile = value.Name;
            AppState.SaveSettings();
        }
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        AppState.Settings.IncludeSubfolders = value;
        AppState.SaveSettings();
    }

    /// <summary>Kéo dòng để đổi chỗ (ListView xoá rồi thêm lại), Lên / Xuống, Bỏ khỏi danh sách → đánh số lại + lưu thứ tự.</summary>
    private void Files_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmpty));
        if (_suspendOrderSave)
        {
            return;
        }
        Renumber();
        SaveOrder(); // kéo-thả của ListView = Remove rồi Add: lần lưu sau (đủ file) ghi đè lần trước
        UpdateSummary();
    }

    private void Renumber()
    {
        for (var i = 0; i < Files.Count; i++)
        {
            Files[i].Number = i + 1;
        }
    }

    // ----- Thư mục / thứ tự -----

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseFolderAsync()
    {
        if (await Shell.PickFolderAsync() is { } folder)
        {
            Folder = folder;
            await LoadFolderAsync();
        }
    }

    private bool CanLoad => IsIdle && Folder.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadFolderAsync()
    {
        var folder = Folder.Trim().Trim('"');
        if (!Directory.Exists(folder))
        {
            Status = $"Không thấy thư mục: {folder}";
            return;
        }
        IsBusy = true;
        try
        {
            folder = Path.GetFullPath(folder);
            Folder = folder;
            AppState.Settings.LastFolder = folder;
            AppState.SaveSettings();
            var key = AppSettings.OrderKey(folder);
            var excluded = AppState.Settings.ExcludedFiles.GetValueOrDefault(key) ?? [];
            var recursive = IncludeSubfolders;
            var files = await Task.Run(() => Directory
                .EnumerateFiles(folder, "*.sql", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(p => !p.Split(Path.DirectorySeparatorChar).Contains(".svn") && !p.Split(Path.DirectorySeparatorChar).Contains(".git"))
                .Select(p => SqlScriptFile.Load(p, folder))
                .ToList());
            var skipped = files.RemoveAll(f => excluded.Contains(f.RelativePath, StringComparer.OrdinalIgnoreCase));

            var saved = AppState.Settings.SavedOrders.GetValueOrDefault(key);
            var order = saved is { Count: > 0 } ? RunOrder.ApplySaved(files, saved) : RunOrder.AutoSort(files);
            SetFiles(order.Files);
            OrderNote = saved is { Count: > 0 } ? "Thứ tự: đã chỉnh tay (lưu theo thư mục)" : "Thứ tự: tự động";
            ClearLog();
            AppendLog($"Đọc {folder}{(recursive ? " (gồm thư mục con)" : string.Empty)}: {files.Count} file .sql, {files.Sum(f => f.Statements.Count)} câu.");
            if (skipped > 0)
            {
                AppendLog($"Bỏ qua {skipped} file đã bỏ khỏi danh sách lần trước (bấm Sắp lại tự động để lấy lại).");
            }
            foreach (var note in order.Notes)
            {
                AppendLog(note);
            }
            LogStaticIssues();
            Status = files.Count == 0 ? "Thư mục không có file .sql." : "Kiểm tra thứ tự rồi bấm Kiểm tra / Chạy thử.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Đọc thư mục lỗi");
            Status = "Không đọc được thư mục: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAutoSort => IsIdle && Folder.Trim().Length > 0;

    /// <summary>Bỏ thứ tự chỉnh tay + danh sách file đã bỏ, đọc lại thư mục và tự sắp.</summary>
    [RelayCommand(CanExecute = nameof(CanAutoSort))]
    private async Task AutoSortAsync()
    {
        if (Directory.Exists(Folder.Trim()))
        {
            var key = AppSettings.OrderKey(Folder.Trim());
            AppState.Settings.SavedOrders.Remove(key);
            AppState.Settings.ExcludedFiles.Remove(key);
            AppState.SaveSettings();
        }
        await LoadFolderAsync();
    }

    private void SetFiles(IEnumerable<SqlScriptFile> files)
    {
        _suspendOrderSave = true;
        Files.Clear();
        foreach (var f in files)
        {
            var row = new FileRow(f);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileRow.Detail) && SelectedFile == row)
                {
                    OnPropertyChanged(nameof(SelectedDetail));
                }
            };
            Files.Add(row);
        }
        _suspendOrderSave = false;
        Renumber();
        UpdateSummary();
        SelectedFile = null;
    }

    private void SaveOrder()
    {
        if (Files.Count == 0 || !Directory.Exists(Folder.Trim()))
        {
            return;
        }
        var key = AppSettings.OrderKey(Folder.Trim());
        AppState.Settings.SavedOrders[key] = Files.Select(f => f.RelativePath).ToList();
        AppState.SaveSettings();
        OrderNote = "Thứ tự: đã chỉnh tay (lưu theo thư mục)";
    }

    private bool CanMoveUp => IsIdle && SelectedFile is not null && Files.IndexOf(SelectedFile) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    private bool CanMoveDown => IsIdle && SelectedFile is not null && Files.IndexOf(SelectedFile) < Files.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        var row = SelectedFile!;
        var index = Files.IndexOf(row);
        Files.Move(index, index + delta);
        SelectedFile = row;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemove => IsIdle && SelectedFile is not null;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        var row = SelectedFile!;
        var index = Files.IndexOf(row);
        var key = AppSettings.OrderKey(Folder.Trim());
        if (!AppState.Settings.ExcludedFiles.TryGetValue(key, out var excluded))
        {
            AppState.Settings.ExcludedFiles[key] = excluded = [];
        }
        excluded.Add(row.RelativePath);
        Files.Remove(row);
        SelectedFile = Files.Count == 0 ? null : Files[Math.Min(index, Files.Count - 1)];
        AppendLog($"Bỏ {row.RelativePath} khỏi danh sách (đọc lại thư mục vẫn bỏ qua; Sắp lại tự động để lấy lại).");
    }

    private bool CanOpenFile => SelectedFile is not null;

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void OpenFile() => Shell.Open(SelectedFile!.FullPath);

    private void UpdateSummary()
    {
        if (Files.Count == 0)
        {
            Summary = "Chưa có file nào.";
            return;
        }
        var statements = Files.Sum(f => f.StatementCount);
        var danger = Files.Count(f => f.HasDanger);
        Summary = $"{Files.Count} file · {statements} câu" + (danger > 0 ? $" · {danger} file xoá / sửa dữ liệu" : string.Empty);
    }

    private void LogStaticIssues()
    {
        foreach (var row in Files)
        {
            foreach (var issue in row.File.Issues.Where(i => i.Level != IssueLevel.Info))
            {
                AppendLog($"  [{FileRow.LevelText(issue.Level)}] {row.RelativePath}: {issue}");
            }
        }
    }

    // ----- Kiểm tra / Chạy -----

    private bool CanUseDb => IsIdle && SelectedProfile is not null;

    /// <summary>Đọc lại file (có thể vừa svn update / sửa) giữ nguyên thứ tự. False nếu có file mất.</summary>
    private async Task<bool> ReloadFilesAsync()
    {
        var folder = Folder.Trim();
        var rows = Files.ToList();
        var missing = rows.Where(r => !File.Exists(r.FullPath)).Select(r => r.RelativePath).ToList();
        if (missing.Count > 0)
        {
            AppendLog("Không thấy file (đã xoá / đổi tên?): " + string.Join(", ", missing) + " - bấm Đọc thư mục.");
            Status = "Có file không còn trong thư mục - bấm Đọc thư mục.";
            return false;
        }
        var reloaded = await Task.Run(() => rows.Select(r => SqlScriptFile.Load(r.FullPath, folder)).ToList());
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].File = reloaded[i];
            rows[i].ApplyIssues(reloaded[i].Issues, string.Empty);
        }
        UpdateSummary();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanUseDb))]
    private async Task CheckAsync()
    {
        if (Files.Count == 0)
        {
            Status = "Chưa có file - bấm Đọc thư mục.";
            return;
        }
        IsBusy = true;
        ClearLog();
        try
        {
            if (!await ReloadFilesAsync())
            {
                return;
            }
            var profile = SelectedProfile!;
            AppendLog($"Kiểm tra {Files.Count} file với {profile.Username}@{profile.Target}, {profile.SchemaText} (chỉ đọc catalog, không thay đổi DB)...");
            Status = "Đang kiểm tra...";
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            if (!await EnsureSchemaExistsAsync(profile, cts.Token))
            {
                return;
            }
            var files = Files.Select(f => f.File).ToList();
            var existing = await PgCatalog.FindExistingAsync(profile, Precheck.CreatedNames(files), cts.Token);
            await DbChecksAsync(profile, files, existing, cts.Token);
            Status = "Kiểm tra xong - xem cột Kết quả. Bước tiếp: Chạy thử.";
        }
        catch (Exception ex)
        {
            AppendLog("✗ " + Pg.Describe(ex));
            Status = "Không kiểm tra được: " + Pg.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Đối chiếu từng file với DB + (nếu Xoá rồi tạo lại) báo bảng đang có dữ liệu sẽ bị xoá.</summary>
    private async Task<List<string>> DbChecksAsync(ConnectionProfile profile, List<SqlScriptFile> files, List<ExistingObject> existing, CancellationToken ct)
    {
        var warnings = 0;
        var createdRelations = Precheck.CreatedRelations(files);
        var errors = 0;
        for (var i = 0; i < Files.Count; i++)
        {
            var issues = files[i].Issues.Concat(Precheck.Compare(files[i], existing, DropFirst, createdRelations)).ToList();
            Files[i].ApplyIssues(issues, "Đạt kiểm tra");
            warnings += issues.Count(x => x.Level == IssueLevel.Warning);
            errors += issues.Count(x => x.Level == IssueLevel.Error);
            foreach (var issue in issues)
            {
                AppendLog($"  [{FileRow.LevelText(issue.Level)}] {files[i].RelativePath}: {issue}");
            }
        }
        var dataLoss = new List<string>();
        if (DropFirst)
        {
            var drop = DropScript.Build(files, profile.SchemaOrNull);
            var rows = await PgCatalog.CountRowsAsync(profile, drop.Tables, 100_001, ct);
            foreach (var (table, count) in rows.Where(r => r.Value > 0))
            {
                dataLoss.Add($"{table}: {(count > 100_000 ? "hơn 100.000" : count.ToString("N0"))} dòng");
            }
            AppendLog($"Xoá đối tượng cũ rồi tạo lại: {drop.Statements.Count} câu DROP, {drop.Tables.Count()} bảng.");
            foreach (var line in dataLoss)
            {
                AppendLog($"  [Cảnh báo] Bảng có dữ liệu sẽ bị xoá - {line}");
            }
        }
        AppendLog($"Kết quả: {errors} lỗi, {warnings} cảnh báo{(dataLoss.Count > 0 ? $", {dataLoss.Count} bảng có dữ liệu sẽ bị xoá" : string.Empty)}.");
        return dataLoss;
    }

    [RelayCommand(CanExecute = nameof(CanUseDb))]
    private Task DryRunAsync() => ExecuteAsync(dryRun: true);

    [RelayCommand(CanExecute = nameof(CanUseDb))]
    private Task RunAsync() => ExecuteAsync(dryRun: false);

    private async Task ExecuteAsync(bool dryRun)
    {
        if (Files.Count == 0)
        {
            Status = "Chưa có file - bấm Đọc thư mục.";
            return;
        }
        IsBusy = true;
        ClearLog();
        var started = DateTime.Now;
        var profile = SelectedProfile!;
        var mode = dryRun ? "Chạy thử" : "Chạy";
        var record = new RunRecord
        {
            Time = started,
            Mode = mode,
            DropFirst = DropFirst,
            Folder = Folder.Trim(),
            Profile = profile.Name,
            Target = profile.Target,
            User = profile.Username,
            Schema = profile.SchemaText,
            FileCount = Files.Count,
        };
        var attempted = false; // đã gửi lên server (qua kiểm tra + người dùng đồng ý) → ghi lịch sử
        try
        {
            if (!await ReloadFilesAsync())
            {
                return;
            }
            var files = Files.Select(f => f.File).ToList();
            var blocked = Files.Where(f => f.File.HasErrors).ToList();
            if (blocked.Count > 0)
            {
                foreach (var row in blocked)
                {
                    foreach (var issue in row.File.Issues.Where(i => i.Level == IssueLevel.Error))
                    {
                        AppendLog($"  [Lỗi] {row.RelativePath}: {issue}");
                    }
                }
                Status = $"{blocked.Count} file có lỗi không chạy được - sửa file hoặc Bỏ khỏi danh sách.";
                return;
            }

            var drop = DropFirst ? DropScript.Build(files, profile.SchemaOrNull) : null;
            Status = "Đang kiểm tra trước khi chạy...";
            List<string> dataLoss;
            using (var check = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                if (!await EnsureSchemaExistsAsync(profile, check.Token))
                {
                    return;
                }
                var existing = await PgCatalog.FindExistingAsync(profile, Precheck.CreatedNames(files), check.Token);
                dataLoss = await DbChecksAsync(profile, files, existing, check.Token);
            }

            if (!dryRun && Confirm is not null)
            {
                var lines = new List<string>
                {
                    $"Database: {profile.Username}@{profile.Target}" + (profile.Name.Length > 0 ? $"  (hồ sơ \"{profile.Name}\")" : string.Empty),
                    $"Schema: {(profile.SchemaOrNull is { } s ? s + (profile.IncludePublic && s != "public" ? " (tìm cả trong public)" : string.Empty) : "mặc định - theo search_path của server / user (thường là public)")}"
                        + " - tên có ghi schema trong file vẫn theo file.",
                    $"Thư mục: {Folder.Trim()}",
                    $"{files.Count} file, {files.Sum(f => f.Statements.Count)} câu - chạy trong 1 transaction: lỗi ở đâu là huỷ (rollback) hết.",
                };
                var danger = Files.Where(f => f.HasDanger).ToList();
                if (danger.Count > 0)
                {
                    lines.Add($"⚠ {danger.Count} file xoá / sửa dữ liệu: " + string.Join(", ", danger.Select(f => $"{f.RelativePath} ({f.Danger})")));
                }
                if (drop is not null)
                {
                    lines.Add($"⚠ Xoá đối tượng cũ rồi tạo lại: {drop.Statements.Count} câu DROP, gồm {drop.Tables.Count()} bảng (mất cả dữ liệu trong bảng).");
                    lines.AddRange(dataLoss.Select(d => "⚠ Bảng đang có dữ liệu: " + d));
                }
                var warned = Files.Count(f => f.Status == RowStatus.Warning);
                if (warned > 0)
                {
                    lines.Add($"{warned} file có cảnh báo khi kiểm tra (xem nhật ký) - nên Chạy thử trước.");
                }
                if (!await Confirm(new RunConfirmation($"Chạy lên {profile.Target}?", lines, "Chạy")))
                {
                    AppendLog("Đã huỷ - chưa chạy gì.");
                    Status = "Chưa chạy.";
                    return;
                }
            }

            foreach (var row in Files)
            {
                row.SetResult(RowStatus.None, string.Empty);
            }
            attempted = true;
            record.Revision = await VcsInfo.DescribeAsync(Folder.Trim());
            AppendLog($"{mode} lúc {started:yyyy-MM-dd HH:mm:ss}{(record.Revision.Length > 0 ? " · " + record.Revision : string.Empty)}");
            _cts = new CancellationTokenSource();
            CancelCommand.NotifyCanExecuteChanged();
            Status = dryRun ? "Đang chạy thử..." : "Đang chạy...";
            var failedIndex = -1;
            var progress = new Progress<RunEvent>(e =>
            {
                switch (e)
                {
                    case RunLog log:
                        AppendLog(log.Message);
                        break;
                    case FileStarted s:
                        Files[s.Index].SetResult(RowStatus.Running, "Đang chạy...");
                        Status = $"{(dryRun ? "Chạy thử" : "Chạy")} {s.Index + 1}/{Files.Count}: {Files[s.Index].RelativePath}";
                        break;
                    case FileFinished f:
                        Files[f.Index].SetResult(RowStatus.Ok, $"{(dryRun ? "Chạy được" : "Đã chạy")} · {f.Elapsed.TotalSeconds:0.00}s");
                        Progress = 100.0 * (f.Index + 1) / Files.Count;
                        break;
                    case FileFailed f:
                        failedIndex = f.Index;
                        Files[f.Index].SetResult(RowStatus.Error, $"Lỗi dòng {f.Line}", $"[Lỗi] Dòng {f.Line}: {f.Message}");
                        break;
                }
            });
            var outcome = await PgRunner.RunAsync(profile, files, drop, dryRun, progress, _cts.Token);
            // Progress<T> báo qua dispatcher: chờ các báo cáo cuối về tới rồi mới chốt trạng thái.
            await Task.Yield();

            if (!outcome.Committed)
            {
                for (var i = 0; i < Files.Count; i++)
                {
                    var row = Files[i];
                    if (row.Status == RowStatus.Ok && !dryRun)
                    {
                        row.SetResult(RowStatus.Info, "Chạy được (đã rollback)");
                    }
                    else if (row.Status is RowStatus.None or RowStatus.Running && i != failedIndex)
                    {
                        row.SetResult(RowStatus.NotRun, "Không chạy");
                    }
                }
            }
            if (failedIndex >= 0)
            {
                SelectedFile = Files[failedIndex];
            }
            record.Success = outcome.Success;
            record.FilesDone = outcome.FilesDone;
            record.Error = outcome.Error;
            record.DurationMs = (long)outcome.Elapsed.TotalMilliseconds;
            Progress = outcome.Success ? 100 : Progress;
            Status = outcome switch
            {
                { Success: true, Committed: true } => $"Xong: đã chạy {outcome.FilesDone} file lên {profile.Target} ({outcome.Elapsed.TotalSeconds:0.0} giây).",
                { Success: true } => $"Chạy thử đạt: {outcome.FilesDone} file chạy được, đã rollback - DB không đổi. Bấm Chạy để ghi thật.",
                _ => $"{mode} thất bại, đã rollback - DB không đổi. {outcome.Error}",
            };
            _log.LogInformation("{Mode} {Folder} → {Target}: {Result}", mode, record.Folder, record.Target, Status);
        }
        catch (Exception ex)
        {
            record.Error = Pg.Describe(ex);
            AppendLog("✗ " + record.Error);
            Status = $"{mode} thất bại: {record.Error}";
            _log.LogError(ex, "{Mode} lỗi", mode);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (attempted)
            {
                SaveHistory(record);
            }
            IsBusy = false;
        }
    }

    private void SaveHistory(RunRecord record)
    {
        try
        {
            record.LogPath = AppState.History.NewLogPath(record.Time);
            var header = new StringBuilder()
                .AppendLine($"DbTools - {record.Mode}{(record.DropFirst ? " (xoá đối tượng cũ rồi tạo lại)" : string.Empty)}")
                .AppendLine($"Thời gian: {record.Time:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"Database: {record.User}@{record.Target} (hồ sơ \"{record.Profile}\"), {record.Schema}")
                .AppendLine($"Thư mục: {record.Folder} {record.Revision}")
                .AppendLine($"Kết quả: {(record.Success ? "Đạt" : "Thất bại")} {record.Error}")
                .AppendLine("Thứ tự chạy:");
            foreach (var row in Files)
            {
                header.AppendLine($"  {row.Number,3}. {row.RelativePath}  [{row.StatusText}]");
            }
            header.AppendLine(new string('-', 60));
            File.WriteAllText(record.LogPath, header + _logText.ToString(), new UTF8Encoding(true));
            AppState.History.Add(record);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Không ghi được lịch sử chạy");
        }
    }

    private bool CanCancel => IsBusy && _cts is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        Status = "Đang huỷ...";
    }

    private bool CanSaveDropScript => IsIdle && Files.Count > 0;

    /// <summary>Ghi script xoá ngược ra file tạm rồi mở để xem / chạy tay.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveDropScript))]
    private void SaveDropScript()
    {
        try
        {
            var files = Files.Select(f => f.File).ToList();
            var drop = DropScript.Build(files, SelectedProfile?.SchemaOrNull);
            Directory.CreateDirectory(DataPaths.Temp);
            var name = new DirectoryInfo(Folder.Trim()).Name;
            var path = Path.Combine(DataPaths.Temp, $"drop_{name}_{DateTime.Now:yyyyMMdd_HHmmss}.sql");
            File.WriteAllText(path, drop.ToSql($"Script xoá ngược cho {Folder.Trim()} ({files.Count} file)"), new UTF8Encoding(false));
            AppendLog($"Đã ghi script xoá ({drop.Statements.Count} câu DROP{(drop.NotReversible.Count > 0 ? $", {drop.NotReversible.Count} câu dữ liệu cần xem tay" : string.Empty)}): {path}");
            Shell.Open(path);
        }
        catch (Exception ex)
        {
            AppendLog("✗ Không ghi được script xoá: " + ex.Message);
        }
    }

    // ----- Nhật ký -----

    [RelayCommand]
    private void CopyLog()
    {
        var package = new DataPackage();
        package.SetText(_logText.ToString());
        Clipboard.SetContent(package);
        Status = "Đã chép nhật ký.";
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(AppState.History.LogFolder);
        Shell.Open(AppState.History.LogFolder);
    }

    private void ClearLog()
    {
        _logText.Clear();
        LogOutput = string.Empty;
        Progress = 0;
    }

    private void AppendLog(string line)
    {
        _logText.AppendLine(line);
        LogOutput = _logText.ToString();
    }

}
