using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
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
        var folder = FolderPath.Trim().Trim('"');
        if (folder.Length == 0)
        {
            ExportStatus = "Chưa chọn thư mục.";
            return;
        }
        IsBusy = true;
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
        }
        catch (Exception ex)
        {
            AppLog.For("Export").LogError(ex, "Xuất lỗi: {Folder}", folder);
            ExportStatus = "Xuất lỗi: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

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
