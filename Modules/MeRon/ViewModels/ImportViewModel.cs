using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>1 dòng trong bảng xem trước: bảng nào, file nào, bao nhiêu dòng sẽ nạp, đang có bao nhiêu.</summary>
public sealed record ImportTableRow(string Title, string FileName, string FileRows, string CurrentRows);

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
    private async Task CheckAsync()
    {
        IsBusy = true;
        ClearPlan();
        Status = "Đang đọc file backup...";
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        try
        {
            var folder = FolderPath.Trim().Trim('"');
            var source = GoogleDriveBackupSource.ParseFolderId(folder) is { } driveId
                ? new GoogleDriveBackupSource(driveId, DriveDownloadRoot, Http, new Progress<string>(s => Status = s))
                : (IBackupSource)new LocalFolderBackupSource(folder);
            IsDownloading = source is GoogleDriveBackupSource;
            var backup = await source.GetLatestAsync(cancel.Token);
            IsDownloading = false;
            // Tìm được backup → nhớ thư mục / link (cả khi là thư mục cha) cho lần mở sau.
            _database.SetMeta(MeRonDatabase.MetaKeys.LastPickedFolder, folder);
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
        catch (Exception ex)
        {
            AppLog.For("Import").LogError(ex, "Kiểm tra backup lỗi: {Folder}", FolderPath);
            Status = "Lỗi khi đọc backup: " + ex.Message;
        }
        finally
        {
            _cancel = null;
            IsDownloading = false;
            IsBusy = false;
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
