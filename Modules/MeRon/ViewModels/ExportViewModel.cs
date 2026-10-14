using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Cloud;
using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>
/// Trang Xuất / Xoá dữ liệu: xuất 8 bảng ra bộ backup TSV đúng định dạng điện thoại; xoá toàn bộ dữ liệu ở máy (View hỏi
/// xác nhận 2 lần như điện thoại). Cả 2 không bao giờ làm mất dữ liệu không lấy lại được: xoá luôn chép bản sao trước.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private readonly MeRonDatabase _database = AppServices.Database;

    public ExportViewModel()
    {
        FolderPath = _database.GetMeta(MeRonDatabase.MetaKeys.LastExportFolder)
                     ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeRon");
        RefreshCounts();
    }

    [ObservableProperty]
    private string _folderPath;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _exportStatus = string.Empty;

    /// <summary>Thư mục bộ backup vừa xuất (để mở trong Explorer).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExport))]
    private string _lastExport = string.Empty;

    [ObservableProperty]
    private string _wipeStatus = string.Empty;

    [ObservableProperty]
    private string _currentData = string.Empty;

    public bool HasExport => LastExport.Length > 0;

    public string BackupsFolder => _database.BackupsFolder;

    public void RefreshCounts()
    {
        try
        {
            CurrentData = "Đang có: " + string.Join(", ", Schema.BackupTables.Select(t => $"{t.Title} {VnFormat.Number(_database.Count(t.Name))}"));
        }
        catch (Exception ex)
        {
            CurrentData = "Không đọc được database: " + ex.Message;
        }
    }

    public async Task ExportAsync()
    {
        IsBusy = true;
        try
        {
            await ExportCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Xuất bộ TSV vào <see cref="FolderPath"/>; lỗi thì ghi vào <see cref="ExportStatus"/> và trả null.</summary>
    private async Task<ExportResult?> ExportCoreAsync()
    {
        var folder = FolderPath.Trim().Trim('"');
        if (folder.Length == 0)
        {
            ExportStatus = "Chưa chọn thư mục.";
            return null;
        }
        ExportStatus = "Đang xuất...";
        try
        {
            Directory.CreateDirectory(folder);
            var result = await Task.Run(() => BackupExporter.Export(_database, folder, DateTime.Now));
            _database.SetMeta(MeRonDatabase.MetaKeys.LastExportFolder, folder);
            LastExport = result.Folder;
            AppLog.For("Export").LogInformation("Xuất {Folder}: {Counts}", result.Folder,
                string.Join(", ", result.RowCounts.Select(kv => $"{kv.Key}={kv.Value}")));
            ExportStatus = $"Đã xuất {VnFormat.Number(result.RowCounts.Values.Sum())} dòng ({VnFormat.Number(result.RowCounts["Orders"])} đơn hàng) " +
                           $"vào {result.Folder}";
            return result;
        }
        catch (Exception ex)
        {
            AppLog.For("Export").LogError(ex, "Xuất lỗi: {Folder}", folder);
            ExportStatus = "Xuất lỗi: " + ex.Message;
            return null;
        }
    }

    // ---- Google Drive: đăng nhập + tải bộ backup lên ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(IsSignedOut))]
    private string _googleEmail = string.Empty;

    [ObservableProperty]
    private bool _isGoogleConfigured;

    [ObservableProperty]
    private string _driveFolder = string.Empty;

    [ObservableProperty]
    private string _driveStatus = string.Empty;

    /// <summary>Link folder vừa tải lên (mở trên trình duyệt).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDriveLink))]
    private string _driveLink = string.Empty;

    public bool IsSignedIn => GoogleEmail.Length > 0;

    public bool IsSignedOut => IsGoogleConfigured && GoogleEmail.Length == 0;

    public bool HasDriveLink => DriveLink.Length > 0;

    public void RefreshGoogle()
    {
        IsGoogleConfigured = GoogleServices.Auth is not null;
        GoogleEmail = GoogleServices.Auth?.Email ?? string.Empty;
        OnPropertyChanged(nameof(IsSignedOut));
        DriveFolder = _database.GetMeta(MeRonDatabase.MetaKeys.DriveUploadFolder) ?? string.Empty;
        if (!IsGoogleConfigured)
        {
            DriveStatus = $"Chưa cấu hình Google: đặt file client_secret_….json (OAuth client loại Desktop app) vào {GoogleServices.ConfigFolder} " +
                          "hoặc thư mục Assets cạnh MeRon.exe, rồi mở lại app.";
        }
    }

    /// <summary>Mở trình duyệt để đăng nhập Google; chờ tối đa 5 phút.</summary>
    public async Task SignInAsync()
    {
        if (GoogleServices.Auth is not { } auth)
        {
            return;
        }
        IsBusy = true;
        DriveStatus = "Đang chờ đăng nhập trên trình duyệt... (chọn tài khoản Google rồi bấm Cho phép / Continue)";
        try
        {
            GoogleEmail = await auth.SignInAsync(OpenUrl, TimeSpan.FromMinutes(5));
            AppLog.For("Google").LogInformation("Đăng nhập Google: {Email}; quyền: {Scopes}", GoogleEmail, auth.LastGrantedScopes);
            DriveStatus = $"Đã đăng nhập Google: {GoogleEmail}";
        }
        catch (Exception ex)
        {
            AppLog.For("Google").LogError(ex, "Đăng nhập Google lỗi; quyền Google cấp: {Scopes}", auth.LastGrantedScopes ?? "(chưa tới bước cấp quyền)");
            DriveStatus = ex is GoogleAuthException ? ex.Message : "Đăng nhập Google lỗi: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsSignedOut));
        }
    }

    public async Task SignOutAsync()
    {
        if (GoogleServices.Auth is not { } auth)
        {
            return;
        }
        await auth.SignOutAsync();
        AppLog.For("Google").LogInformation("Đăng xuất Google: {Email}", GoogleEmail);
        GoogleEmail = string.Empty;
        DriveLink = string.Empty;
        DriveStatus = "Đã đăng xuất Google.";
        OnPropertyChanged(nameof(IsSignedOut));
    }

    /// <summary>Xuất bộ TSV ra thư mục trên máy (như nút Xuất TSV) rồi tải lên Google Drive.</summary>
    public async Task UploadToDriveAsync()
    {
        if (GoogleServices.Auth is not { } auth)
        {
            return;
        }
        string? parentId = null;
        var folderText = DriveFolder.Trim();
        if (folderText.Length > 0)
        {
            parentId = GoogleDriveBackupSource.ParseFolderId(folderText) ?? (DriveFolderId().IsMatch(folderText) ? folderText : null);
            if (parentId is null)
            {
                DriveStatus = "Folder Google Drive không đúng: dán link dạng https://drive.google.com/drive/folders/…, hoặc để trống.";
                return;
            }
        }
        _database.SetMeta(MeRonDatabase.MetaKeys.DriveUploadFolder, folderText);

        IsBusy = true;
        DriveLink = string.Empty;
        try
        {
            DriveStatus = "Đang xuất bộ backup...";
            if (await ExportCoreAsync() is not { } export)
            {
                DriveStatus = "Không xuất được bộ backup - xem lỗi ở khung Xuất bên trên.";
                return;
            }
            var uploader = new DriveUploader(GoogleServices.Http, auth.GetAccessTokenAsync);
            var result = await uploader.UploadBackupAsync(export.Folder, parentId, new Progress<string>(s => DriveStatus = s));
            AppLog.For("Google").LogInformation("Tải lên Drive {Name}: {Count} file, folder {Id}", result.FolderName, result.FileCount, result.FolderId);
            DriveLink = result.WebLink ?? GoogleDriveBackupSource.FolderUrl(result.FolderId);
            DriveStatus = $"Đã tải lên Google Drive: {result.FileCount} file trong thư mục {result.FolderName}" +
                          (parentId is null ? $" (folder \"{DriveUploader.OwnFolderName}\")." : ".");
        }
        catch (Exception ex)
        {
            AppLog.For("Google").LogError(ex, "Tải lên Drive lỗi");
            DriveStatus = ex is GoogleAuthException ? ex.Message : "Tải lên Google Drive lỗi: " + ex.Message;
            if (ex is GoogleAuthException { NeedsSignIn: true })
            {
                await auth.SignOutAsync(); // đăng nhập lại để cấp đủ quyền
            }
            GoogleEmail = GoogleServices.Auth?.Email ?? string.Empty; // phiên bị thu hồi → về trạng thái chưa đăng nhập
            OnPropertyChanged(nameof(IsSignedOut));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public static void OpenUrl(string url) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    [System.Text.RegularExpressions.GeneratedRegex(@"^[\w-]{10,}$")]
    private static partial System.Text.RegularExpressions.Regex DriveFolderId();

    public string WipeQuestion1 =>
        "Xoá sạch toàn bộ dữ liệu trên máy này: đơn hàng, khách hàng, món ăn, danh mục, địa chỉ, phiếu nhập.\n\n" +
        "Bản dữ liệu hiện tại được tự động lưu vào thư mục Backups trước khi xoá.";

    public const string WipeQuestion2 = "Chắc chắn xoá TOÀN BỘ dữ liệu? Đây là lần xác nhận cuối.";

    public void Wipe()
    {
        try
        {
            var copy = _database.WipeAll();
            AppLog.For("Export").LogWarning("Xoá toàn bộ dữ liệu; bản sao: {Copy}", copy ?? "(trống, không cần)");
            WipeStatus = copy is null
                ? "Đã xoá (dữ liệu vốn đã trống)."
                : $"Đã xoá toàn bộ dữ liệu. Bản trước khi xoá: {copy}";
        }
        catch (Exception ex)
        {
            AppLog.For("Export").LogError(ex, "Xoá toàn bộ lỗi");
            WipeStatus = "Xoá lỗi, dữ liệu giữ nguyên: " + ex.Message;
        }
        RefreshCounts();
    }
}
