using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeRon.Core;
using MeRon.Core.Cloud;
using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>1 dòng trong bảng xem trước: bảng nào, file nào, bao nhiêu dòng sẽ nạp, đang có bao nhiêu.</summary>
public sealed record ImportTableRow(string Title, string FileName, string FileRows, string CurrentRows);

/// <summary>1 bản backup trong thư mục cha / folder Google Drive đang chọn (để chọn bản khác bản mới nhất).</summary>
public sealed partial class BackupChoiceRow(string name, Func<IBackupSource> createSource) : ObservableObject
{
    public string Name => name;

    public string Title => ImportViewModel.FormatBackupName(name);

    public Func<IBackupSource> CreateSource => createSource;

    /// <summary>Bản đang hiện ở phần xem trước.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    public string AccessibleName => $"Bản {Title}";
}

/// <summary>1 bản backup trong danh sách Google Drive (đăng nhập): thời điểm, nơi lưu, đủ bảng chưa.</summary>
public sealed record DriveBackupRow(DriveBackup Backup)
{
    public string Title => ImportViewModel.FormatBackupName(Backup.Name);

    public string Place => Backup.InOwnFolder ? $"Máy tính - folder \"{DriveUploader.OwnFolderName}\"" : "Điện thoại / folder chung";

    public string FilesText => Backup.IsComplete
        ? $"Đủ {Backup.TableCount} bảng"
        : $"Thiếu {Schema.BackupTables.Count - Backup.TableCount} / {Schema.BackupTables.Count} bảng";

    public bool IsIncomplete => !Backup.IsComplete;

    public string AccessibleName => $"Bản {Title} - {Place} - {FilesText}";
}

/// <summary>
/// Trang Nhập dữ liệu: chọn thư mục (hoặc dán link folder Google Drive) → tự đọc + kiểm tra cả bộ backup (chưa ghi gì)
/// → xem trước số dòng từng bảng → Nhập (thay toàn bộ). Phần nạp nằm ở Core (<see cref="BackupImporter"/>); trang này
/// chỉ lấy file qua <see cref="IBackupSource"/> (thư mục trên máy hoặc Google Drive).
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    /// <summary>Dùng chung cho cả app (1 HttpClient / tiến trình). Hết 60 giây không trả lời → báo lỗi mạng.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Bản tải từ Google Drive: %TEMP%\AppSuite\MeRon\GoogleDrive\&lt;tên bản&gt; (tải lại thì ghi đè).</summary>
    private static readonly string DriveDownloadRoot = Path.Combine(Path.GetTempPath(), "AppSuite", "MeRon", "GoogleDrive");

    private readonly MeRonDatabase _database = AppServices.Database;
    private ImportPlan? _plan;
    private CancellationTokenSource? _cancel;

    public ImportViewModel()
    {
        FolderPath = _database.GetMeta(MeRonDatabase.MetaKeys.LastPickedFolder) ?? string.Empty;
        RefreshLastImport();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(ImportCommand))]
    private bool _isBusy;

    /// <summary>Tên bản backup sẽ nạp (vd 20261011_231039) + thư mục của nó.</summary>
    [ObservableProperty]
    private string _backupTitle = string.Empty;

    /// <summary>Đang tải từ Google Drive → hiện nút Huỷ.</summary>
    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _lastImport = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _canImport;

    public ObservableCollection<ImportTableRow> Tables { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public bool HasPlan => Tables.Count > 0;

    public bool HasErrors => Errors.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool ImportEnabled => !IsBusy && CanImport;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ImportEnabled));

    partial void OnCanImportChanged(bool value) => OnPropertyChanged(nameof(ImportEnabled));

    private void RaisePlanChanged()
    {
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
    }

    /// <summary>Gọi sau khi người dùng chọn thư mục: kiểm tra ngay.</summary>
    public async Task SetFolderAsync(string folder)
    {
        FolderPath = folder;
        await CheckAsync();
    }

    private bool CanCheck() => !IsBusy && !string.IsNullOrWhiteSpace(FolderPath);

    /// <summary>Đọc + kiểm tra cả bộ backup, chưa ghi gì vào database. Ô thư mục là link folder Google Drive → tải về
    /// thư mục tạm trước (<see cref="GoogleDriveBackupSource"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckAsync()
    {
        var folder = FolderPath.Trim().Trim('"');
        var progress = new Progress<string>(s => Status = s);
        if (GoogleDriveBackupSource.ParseFolderId(folder) is { } driveId)
        {
            var drive = new GoogleDriveBackupSource(driveId, DriveDownloadRoot, Http, progress);
            return CheckSourceAsync(drive, folder, async ct => (await drive.ListBackupFoldersAsync(ct))
                .Select(e => new BackupChoiceRow(e.Name, () => new GoogleDriveBackupSource(e.Id, DriveDownloadRoot, Http, progress, knownName: e.Name)))
                .ToList());
        }
        var local = new LocalFolderBackupSource(folder);
        return CheckSourceAsync(local, folder, _ => Task.FromResult<IReadOnlyList<BackupChoiceRow>>(local.ListBackupFolders()
            .Select(d => new BackupChoiceRow(Path.GetFileName(d), () => new LocalFolderBackupSource(d)))
            .ToList()));
    }

    /// <summary>Tất cả bản trong thư mục cha / folder Google Drive đang chọn, mới nhất trước (trống khi chọn thẳng 1 bản).</summary>
    public ObservableCollection<BackupChoiceRow> FolderBackups { get; } = [];

    public bool HasFolderBackups => FolderBackups.Count > 0;

    public string FolderBackupsTitle => $"Các bản backup trong thư mục / folder này ({FolderBackups.Count}) - mới nhất trước, bấm để chọn bản khác";

    /// <summary>Bấm 1 bản trong danh sách thư mục cha / folder: kiểm tra bản đó (giữ danh sách).</summary>
    public Task CheckFolderBackupAsync(BackupChoiceRow row)
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }
        foreach (var r in FolderBackups)
        {
            r.IsCurrent = r == row;
        }
        return CheckSourceAsync(row.CreateSource(), null);
    }

    /// <param name="rememberFolder">Thư mục / link sẽ nhớ cho lần mở sau (null = bản chọn trong 1 danh sách, không nhớ).</param>
    /// <param name="listChoices">Đọc lại danh sách các bản trong thư mục cha / folder (null = giữ danh sách đang có).</param>
    private async Task CheckSourceAsync(IBackupSource source, string? rememberFolder,
        Func<CancellationToken, Task<IReadOnlyList<BackupChoiceRow>>>? listChoices = null)
    {
        IsBusy = true;
        ClearPlan();
        Status = "Đang đọc file backup...";
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        try
        {
            IsDownloading = source is not LocalFolderBackupSource;
            if (listChoices is not null)
            {
                SetFolderBackups([]);
                SetFolderBackups(await listChoices(cancel.Token));
            }
            var backup = await source.GetLatestAsync(cancel.Token);
            if (listChoices is not null)
            {
                // Tự lấy bản mới nhất → đánh dấu bản đó trong danh sách.
                foreach (var row in FolderBackups)
                {
                    row.IsCurrent = row.Name == backup.Name;
                }
            }
            IsDownloading = false;
            if (rememberFolder is not null)
            {
                // Tìm được backup → nhớ thư mục / link (cả khi là thư mục cha) cho lần mở sau.
                _database.SetMeta(MeRonDatabase.MetaKeys.LastPickedFolder, rememberFolder);
            }
            Status = "Đang kiểm tra file backup...";
            var plan = await Task.Run(() => BackupImporter.Prepare(backup));
            ShowPlan(plan);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Status = "Đã huỷ tải từ Google Drive. Dữ liệu hiện tại không bị động tới.";
        }
        catch (BackupSourceException ex)
        {
            Status = ex.Message;
        }
        catch (GoogleAuthException ex)
        {
            AppLog.For("Import").LogError(ex, "Tải backup từ Google Drive lỗi");
            Status = ex.Message;
            await HandleGoogleErrorAsync(ex);
        }
        catch (Exception ex)
        {
            AppLog.For("Import").LogError(ex, "Kiểm tra backup lỗi: {Folder}", rememberFolder ?? "(Google Drive)");
            Status = "Lỗi khi đọc backup: " + ex.Message;
        }
        finally
        {
            _cancel = null;
            IsDownloading = false;
            IsBusy = false;
        }
    }

    private void SetFolderBackups(IReadOnlyList<BackupChoiceRow> rows)
    {
        FolderBackups.Clear();
        foreach (var row in rows)
        {
            FolderBackups.Add(row);
        }
        OnPropertyChanged(nameof(HasFolderBackups));
        OnPropertyChanged(nameof(FolderBackupsTitle));
    }

    // ---- Google Drive (đăng nhập): danh sách các bản backup, chọn 1 bản để nhập ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(IsSignedOut))]
    private string _googleEmail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut))]
    private bool _isGoogleConfigured;

    [ObservableProperty]
    private string _driveListStatus = string.Empty;

    public ObservableCollection<DriveBackupRow> DriveBackups { get; } = [];

    public bool IsSignedIn => GoogleEmail.Length > 0;

    public bool IsSignedOut => IsGoogleConfigured && GoogleEmail.Length == 0;

    public bool HasDriveBackups => DriveBackups.Count > 0;

    /// <summary>Vào trang: đọc lại trạng thái đăng nhập (có thể vừa đăng nhập / đăng xuất ở trang Xuất).</summary>
    public void RefreshGoogle()
    {
        IsGoogleConfigured = GoogleServices.Auth is not null;
        var email = GoogleServices.Auth?.Email ?? string.Empty;
        if (email != GoogleEmail)
        {
            DriveBackups.Clear();
            OnPropertyChanged(nameof(HasDriveBackups));
        }
        GoogleEmail = email;
        DriveListStatus = IsGoogleConfigured
            ? IsSignedIn ? DriveListStatus : "Đăng nhập Google để xem các bản backup điện thoại / máy tính đã tải lên."
            : $"Chưa cấu hình Google: đặt file client_secret_….json (OAuth client loại Desktop app) vào {GoogleServices.ConfigFolder} " +
              "hoặc thư mục Assets cạnh MeRon.exe, rồi mở lại app. Vẫn nhập được bằng thư mục hoặc link folder bên trên.";
    }

    /// <summary>Mở trình duyệt để đăng nhập Google (chờ tối đa 5 phút), xong thì đọc luôn danh sách bản backup.</summary>
    public async Task SignInAsync()
    {
        if (GoogleServices.Auth is not { } auth || IsBusy)
        {
            return;
        }
        IsBusy = true;
        DriveListStatus = "Đang chờ đăng nhập trên trình duyệt... (chọn tài khoản Google, tick ô quyền Google Drive rồi bấm Continue)";
        try
        {
            GoogleEmail = await auth.SignInAsync(ExportViewModel.OpenUrl, TimeSpan.FromMinutes(5));
            AppLog.For("Google").LogInformation("Đăng nhập Google (trang Nhập): {Email}; quyền: {Scopes}", GoogleEmail, auth.LastGrantedScopes);
        }
        catch (Exception ex)
        {
            AppLog.For("Google").LogError(ex, "Đăng nhập Google lỗi; quyền Google cấp: {Scopes}", auth.LastGrantedScopes ?? "(chưa tới bước cấp quyền)");
            DriveListStatus = ex is GoogleAuthException ? ex.Message : "Đăng nhập Google lỗi: " + ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }
        await LoadDriveBackupsAsync();
    }

    public async Task SignOutAsync()
    {
        if (GoogleServices.Auth is not { } auth)
        {
            return;
        }
        await auth.SignOutAsync();
        AppLog.For("Google").LogInformation("Đăng xuất Google (trang Nhập): {Email}", GoogleEmail);
        GoogleEmail = string.Empty;
        DriveBackups.Clear();
        OnPropertyChanged(nameof(HasDriveBackups));
        DriveListStatus = "Đã đăng xuất Google.";
    }

    /// <summary>Đọc danh sách bản backup trên Google Drive (mới nhất trước).</summary>
    public async Task LoadDriveBackupsAsync()
    {
        if (GoogleServices.Auth is not { } auth || IsBusy)
        {
            return;
        }
        IsBusy = true;
        DriveListStatus = "Đang đọc danh sách bản backup trên Google Drive...";
        try
        {
            var backups = await new DriveBackupBrowser(GoogleServices.Http, auth.GetAccessTokenAsync).ListAsync();
            DriveBackups.Clear();
            foreach (var backup in backups)
            {
                DriveBackups.Add(new DriveBackupRow(backup));
            }
            AppLog.For("Google").LogInformation("Danh sách backup trên Drive: {Count} bản", backups.Count);
            DriveListStatus = backups.Count == 0
                ? "Chưa thấy bản backup nào trên Google Drive mà MeRon đọc được. App chỉ thấy bản do app điện thoại hoặc MeRon tải lên " +
                  "(folder tự tạo / file tự kéo lên trang web Drive thì không thấy) - dùng ô thư mục / link folder bên trên."
                : $"{backups.Count} bản backup, mới nhất trước. Bấm 1 bản để kiểm tra rồi nhập.";
        }
        catch (Exception ex)
        {
            AppLog.For("Google").LogError(ex, "Đọc danh sách backup trên Drive lỗi");
            DriveListStatus = ex is GoogleAuthException ? ex.Message : "Đọc danh sách trên Google Drive lỗi: " + ex.Message;
            if (ex is GoogleAuthException auth403)
            {
                await HandleGoogleErrorAsync(auth403);
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasDriveBackups));
        }
    }

    /// <summary>Bấm 1 bản trong danh sách: tải về thư mục tạm rồi kiểm tra như thư mục trên máy.</summary>
    public Task CheckDriveBackupAsync(DriveBackupRow row)
    {
        if (GoogleServices.Auth is not { } auth || IsBusy)
        {
            return Task.CompletedTask;
        }
        var browser = new DriveBackupBrowser(GoogleServices.Http, auth.GetAccessTokenAsync);
        // Danh sách của thư mục / link bên trên không còn liên quan → ẩn đi.
        return CheckSourceAsync(new DriveApiBackupSource(row.Backup, browser, DriveDownloadRoot, new Progress<string>(s => Status = s)), null,
            _ => Task.FromResult<IReadOnlyList<BackupChoiceRow>>([]));
    }

    /// <summary>Phiên thiếu quyền Drive / bị thu hồi → đăng xuất để người dùng đăng nhập lại.</summary>
    private async Task HandleGoogleErrorAsync(GoogleAuthException ex)
    {
        if (ex.NeedsSignIn && GoogleServices.Auth is { } auth)
        {
            await auth.SignOutAsync();
        }
        GoogleEmail = GoogleServices.Auth?.Email ?? string.Empty;
        if (!IsSignedIn)
        {
            DriveBackups.Clear();
            OnPropertyChanged(nameof(HasDriveBackups));
        }
    }

    /// <summary>Huỷ lúc đang tải từ Google Drive (đọc thư mục trên máy thì nhanh, không cần).</summary>
    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    private void ShowPlan(ImportPlan plan)
    {
        _plan = plan;
        BackupTitle = $"Bản backup {FormatBackupName(plan.Backup.Name)}  -  {plan.Backup.Source}";
        foreach (var table in Schema.BackupTables)
        {
            var data = plan.Tables.FirstOrDefault(t => t.Table == table);
            Tables.Add(new ImportTableRow(
                table.Title,
                plan.Backup.Files.TryGetValue(table, out var file) ? Path.GetFileName(file) : "(thiếu file)",
                data is null ? "-" : data.Rows.Count.ToString("N0", Vi),
                _database.Count(table.Name).ToString("N0", Vi)));
        }
        foreach (var error in plan.Errors)
        {
            Errors.Add(error);
        }
        foreach (var warning in plan.Warnings)
        {
            Warnings.Add(warning);
        }
        CanImport = plan.CanImport;
        Status = plan.CanImport
            ? "Đã kiểm tra xong, không có lỗi. Bấm \"Nhập (thay toàn bộ)\" để nạp."
            : $"Có {plan.Errors.Count} lỗi - sửa file backup hoặc chọn bản khác. Dữ liệu hiện tại chưa bị động tới.";
        RaisePlanChanged();
    }

    private bool CanRunImport() => !IsBusy && CanImport;

    /// <summary>Nạp thật (View hỏi xác nhận trước khi gọi).</summary>
    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private async Task ImportAsync()
    {
        if (_plan is not { CanImport: true } plan)
        {
            return;
        }
        IsBusy = true;
        Status = "Đang nạp dữ liệu...";
        try
        {
            var result = await Task.Run(() => BackupImporter.Apply(plan, _database));
            AppLog.For("Import").LogInformation("Đã nhập {Name} từ {Source}: {Counts}", plan.Backup.Name, plan.Backup.Source,
                string.Join(", ", result.RowCounts.Select(kv => $"{kv.Key}={kv.Value}")));
            var summary = $"Đã nhập xong bản {FormatBackupName(plan.Backup.Name)}: " +
                          $"{VnFormat.Number(result.RowCounts.GetValueOrDefault("Orders"))} đơn hàng, " +
                          $"{VnFormat.Number(result.RowCounts.GetValueOrDefault("Users"))} khách hàng, " +
                          $"{VnFormat.Number(result.RowCounts.GetValueOrDefault("Products"))} món ăn.";
            ClearPlan();
            RefreshLastImport();
            Status = result.SafetyCopyPath is null ? summary : summary + $" Dữ liệu cũ đã lưu ở {result.SafetyCopyPath}.";
        }
        catch (Exception ex)
        {
            AppLog.For("Import").LogError(ex, "Nhập backup lỗi: {Source}", plan.Backup.Source);
            Status = "Nhập lỗi, dữ liệu giữ nguyên như trước: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string ConfirmText =>
        _plan is null
            ? string.Empty
            : $"Toàn bộ dữ liệu hiện có trên máy này (cả những gì đã thêm / sửa trên máy tính) sẽ bị XOÁ và thay bằng bản backup " +
              $"{FormatBackupName(_plan.Backup.Name)}.\n\nBản dữ liệu hiện tại được tự động lưu lại trong thư mục Backups trước khi nạp.";

    public string DatabasePath => _database.Path;

    private void ClearPlan()
    {
        _plan = null;
        CanImport = false;
        BackupTitle = string.Empty;
        Tables.Clear();
        Errors.Clear();
        Warnings.Clear();
        RaisePlanChanged();
    }

    private void RefreshLastImport()
    {
        var name = _database.GetMeta(MeRonDatabase.MetaKeys.LastImportBackupName);
        var at = _database.GetMeta(MeRonDatabase.MetaKeys.LastImportAt);
        LastImport = name is null
            ? "Chưa nhập dữ liệu lần nào."
            : $"Lần nhập gần nhất: bản {FormatBackupName(name)}, nhập lúc {FormatTimestamp(at)}.";
    }

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>20261011_231039 → 11/10/2026 23:10:39 (tên khác dạng thì giữ nguyên).</summary>
    public static string FormatBackupName(string name) =>
        DateTime.TryParseExact(name, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
            : name;

    private static string FormatTimestamp(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : value ?? "?";
}
