using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>1 dòng bảng theo năm / tháng. Dòng năm bấm được (mở / đóng 12 tháng ngay bên dưới).</summary>
public sealed partial class PeriodRow(PeriodStats stats) : ObservableObject
{
    public PeriodStats Stats { get; } = stats;
    public bool IsYear => Stats.Month == 0;
    public bool IsMonth => !IsYear;
    public bool IsEmpty => Stats.Orders == 0 && Stats.Purchases == 0;
    public string Label => IsYear ? $"Năm {Stats.Year}" : $"Tháng {Stats.Month:00}/{Stats.Year}";
    public string OrdersText => Stats.CancelledOrders > 0 ? $"{VnFormat.Number(Stats.Orders)} ({VnFormat.Number(Stats.CancelledOrders)} huỷ)" : VnFormat.Number(Stats.Orders);
    public string SalesText => VnFormat.Money(Stats.Sales);
    public string PurchasesText => VnFormat.Number(Stats.Purchases);
    public string PurchaseAmountText => VnFormat.Money(Stats.PurchaseAmount);
    public string AccessibleName => $"{Label}: {OrdersText} đơn, bán {SalesText}, {PurchasesText} phiếu nhập, mua {PurchaseAmountText}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    private bool _isExpanded;

    /// <summary>▸ / ▾ trước dòng năm.</summary>
    public string ExpandGlyph => IsExpanded ? "" : "";
}

public sealed record TableRowView(string Title, string Rows);

/// <summary>1 đường dẫn liên quan (thư mục / file / link Google Drive) - nút Mở mở Explorer hoặc trình duyệt.</summary>
public sealed record PathRow(string Label, string Path, string Detail)
{
    public bool CanOpen => DataInfoViewModel.CanOpen(Path);
    public string AccessibleName => $"Mở {Label}";
}

/// <summary>1 lần dọn có thể khôi phục (thư mục "Truoc khi don\yyyyMMdd_HHmmss" có don-dep.json).</summary>
public sealed record CleanupRow(string Folder, CleanupRecord Record, long Missing)
{
    public string Title => $"{Record.Time:dd/MM/yyyy HH:mm} - dọn dữ liệu trước {DbDate.Show(Record.Before.ToDateTime(TimeOnly.MinValue))}";

    public string Detail =>
        $"{VnFormat.Number(Record.CountOf("Orders"))} đơn ({VnFormat.Number(Record.CountOf("OrderItems"))} dòng món), " +
        $"{VnFormat.Number(Record.CountOf("Purchases"))} phiếu nhập, {VnFormat.Number(Record.CountOf("Users"))} khách, " +
        $"{VnFormat.Number(Record.CountOf("Products"))} món · " +
        (Missing == 0 ? "đã có lại đủ" : $"còn thiếu {VnFormat.Number(Missing)} dòng");

    public bool CanRestore => Missing > 0;
    public string AccessibleName => $"Khôi phục {Title}";
}

/// <summary>Mốc dọn nhanh (combobox): xoá dữ liệu có ngày trước mốc.</summary>
public enum CleanupPreset
{
    BeforeThisYear,
    OlderThanOneYear,
    OlderThanTwoYears,
    CustomDate,
}

/// <summary>
/// Trang Thông tin dữ liệu: xem toàn bộ dữ liệu đang có (số dòng, khoảng ngày, theo năm → tháng, dữ liệu thừa, đường dẫn
/// liên quan), dọn dữ liệu cũ (đơn hàng trừ đơn chưa thanh toán + phiếu nhập trước 1 mốc, tuỳ chọn khách / món không còn
/// dùng) và khôi phục phần đã dọn. Trước khi xoá luôn xuất 1 bộ backup TSV (lỗi thì không xoá) + bản sao .db.
/// </summary>
public sealed partial class DataInfoViewModel : ObservableObject
{
    private readonly MeRonDatabase _database = AppServices.Database;
    private readonly DataMaintenance _maintenance = new(AppServices.Database);
    private readonly Func<DateTime> _now;

    public DataInfoViewModel() : this(() => DateTime.Now)
    {
    }

    internal DataInfoViewModel(Func<DateTime> now)
    {
        _now = now;
        _cutoffDate = new DateTimeOffset(new DateTime(now().Year, 1, 1));
    }

    public ObservableCollection<TableRowView> Tables { get; } = [];

    /// <summary>Năm (mới nhất trước) + tháng của các năm đang mở, xen ngay dưới dòng năm.</summary>
    public ObservableCollection<PeriodRow> Periods { get; } = [];

    public ObservableCollection<PathRow> Paths { get; } = [];

    public ObservableCollection<CleanupRow> Cleanups { get; } = [];

    [ObservableProperty]
    private string _fileText = string.Empty;

    [ObservableProperty]
    private string _syncText = string.Empty;

    [ObservableProperty]
    private string _orderSpanText = string.Empty;

    [ObservableProperty]
    private string _purchaseSpanText = string.Empty;

    [ObservableProperty]
    private string _unusedText = string.Empty;

    [ObservableProperty]
    private bool _hasPeriods;

    [ObservableProperty]
    private bool _hasCleanups;

    // ---------- Dọn dữ liệu cũ ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomDate))]
    private CleanupPreset _preset = CleanupPreset.BeforeThisYear;

    public bool IsCustomDate => Preset == CleanupPreset.CustomDate;

    /// <summary>Ngày mốc khi chọn "Chọn ngày" (xoá dữ liệu TRƯỚC ngày này).</summary>
    [ObservableProperty]
    private DateTimeOffset? _cutoffDate;

    [ObservableProperty]
    private bool _deleteCustomers;

    [ObservableProperty]
    private bool _deleteProducts;

    [ObservableProperty]
    private string _planText = string.Empty;

    /// <summary>"Giữ lại N đơn chưa thanh toán..." (rỗng = không có).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeptUnpaid))]
    private string _keptUnpaidText = string.Empty;

    public bool HasKeptUnpaid => KeptUnpaidText.Length > 0;

    [ObservableProperty]
    private bool _canCleanup;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _cleanupStatus = string.Empty;

    [ObservableProperty]
    private string _restoreStatus = string.Empty;

    /// <summary>Có thay đổi chưa xuất sau khi dọn / khôi phục → khung nhắc xuất sang điện thoại.</summary>
    [ObservableProperty]
    private bool _remindExport;

    public CleanupPlan? CurrentPlan { get; private set; }

    public DateOnly Cutoff
    {
        get
        {
            var today = DateOnly.FromDateTime(_now());
            return Preset switch
            {
                CleanupPreset.OlderThanOneYear => today.AddYears(-1),
                CleanupPreset.OlderThanTwoYears => today.AddYears(-2),
                CleanupPreset.CustomDate => CutoffDate is { } d ? DateOnly.FromDateTime(d.Date) : new DateOnly(today.Year, 1, 1),
                _ => new DateOnly(today.Year, 1, 1),
            };
        }
    }

    public CleanupOptions Options => new(Cutoff, DeleteCustomers, DeleteProducts);

    /// <summary>Lọc trang Chi tiết: đơn "Đã lên đơn" giao trước mốc (= đơn được giữ lại khi dọn).</summary>
    public Core.Models.OrderFilter KeptUnpaidFilter => new(Core.Models.OrderStatus.Shipping, null, Cutoff.AddDays(-1));

    partial void OnPresetChanged(CleanupPreset value) => RefreshPlan();

    partial void OnCutoffDateChanged(DateTimeOffset? value) => RefreshPlan();

    partial void OnDeleteCustomersChanged(bool value) => RefreshPlan();

    partial void OnDeleteProductsChanged(bool value) => RefreshPlan();

    /// <summary>Thư mục Xuất (trang Xuất, nhớ lần trước; mặc định Documents\MeRon).</summary>
    public string ExportFolder => _database.GetMeta(MeRonDatabase.MetaKeys.LastExportFolder)
                                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeRon");

    /// <summary>Thư mục chứa bộ backup tự xuất trước khi dọn: thư mục con "Truoc khi don" trong thư mục Xuất - không nằm
    /// ngay trong thư mục Xuất nên Nhập dữ liệu (chọn thư mục cha = bản mới nhất) không lấy nhầm bản trước khi dọn.
    /// Không tự xoá bản nào - nhiều thì người dùng tự xoá.</summary>
    public string BackupParent => Path.Combine(ExportFolder, "Truoc khi don");

    public void Refresh()
    {
        try
        {
            var overview = _maintenance.Overview();
            FileText = $"File dữ liệu: {FileSize(overview.FileBytes)}";
            var unsynced = _database.GetUnsyncedEdits();
            SyncText = unsynced.Any ? $"Có {VnFormat.Number(unsynced.Count)} dòng thay đổi trên máy tính chưa xuất." : "Không có thay đổi chưa xuất.";
            RemindExport = unsynced.Any && RemindExport;

            Tables.Clear();
            foreach (var table in overview.Tables)
            {
                Tables.Add(new TableRowView(table.Title, VnFormat.Number(table.Rows)));
            }
            OrderSpanText = Span("Đơn hàng (theo ngày giao)", overview.FirstOrderDay, overview.LastOrderDay);
            PurchaseSpanText = Span("Phiếu nhập", overview.FirstPurchaseDay, overview.LastPurchaseDay);
            UnusedText = $"Khách hàng chưa có đơn nào: {VnFormat.Number(overview.CustomersWithoutOrders)} · Món chưa từng bán: " +
                         $"{VnFormat.Number(overview.ProductsNeverSold)} · Danh mục không có món: {VnFormat.Number(overview.EmptyCategories)}";

            var expanded = Periods.Where(p => p.IsYear && p.IsExpanded).Select(p => p.Stats.Year).ToHashSet();
            Periods.Clear();
            foreach (var year in overview.Years)
            {
                var row = new PeriodRow(year);
                Periods.Add(row);
                if (expanded.Contains(year.Year))
                {
                    Expand(row);
                }
            }
            HasPeriods = Periods.Count > 0;
            RefreshCleanups();
            RefreshPaths();
        }
        catch (Exception ex)
        {
            AppLog.For("DataInfo").LogError(ex, "Đọc thông tin dữ liệu lỗi");
            FileText = "Không đọc được dữ liệu: " + ex.Message;
        }
        RefreshPlan();
    }

    /// <summary>Các thư mục / file / link liên quan tới dữ liệu - mỗi dòng có nút Mở.</summary>
    private void RefreshPaths()
    {
        Paths.Clear();
        Paths.Add(new PathRow("File dữ liệu", _database.Path, "Dữ liệu bán hàng trên máy này (SQLite)."));
        var copies = Directory.Exists(_database.BackupsFolder) ? Directory.GetFiles(_database.BackupsFolder, "me_ron_*.db").Length : 0;
        Paths.Add(new PathRow("Bản sao an toàn (.db)", _database.BackupsFolder,
            $"{VnFormat.Number(copies)} bản, tự chép trước mỗi lần Nhập / Xoá toàn bộ / Dọn / Khôi phục (giữ 10 bản gần nhất mỗi loại)."));
        Paths.Add(new PathRow("Thư mục Xuất", ExportFolder, "Nơi trang Xuất ghi bộ backup TSV (đổi ở trang Xuất)."));
        Paths.Add(new PathRow("Bản trước khi dọn", BackupParent,
            $"{VnFormat.Number(Cleanups.Count)} lần dọn - bộ TSV xuất ngay trước mỗi lần dọn, không tự xoá (nhiều thì tự xoá bớt)."));

        var source = _database.GetMeta(MeRonDatabase.MetaKeys.LastImportSource);
        var name = _database.GetMeta(MeRonDatabase.MetaKeys.LastImportBackupName);
        var at = _database.GetMeta(MeRonDatabase.MetaKeys.LastImportAt);
        var when = DbDate.Parse(at) is { } time ? $" lúc {time:dd/MM/yyyy HH:mm}" : string.Empty;
        Paths.Add(source is null
            ? new PathRow("Nhập gần nhất", string.Empty, "Chưa nhập bộ backup nào.")
            : new PathRow("Nhập gần nhất", source, $"Bộ {name}{when}" + (IsUrl(source) ? " - từ Google Drive." : ".")));
        if (_database.GetMeta(MeRonDatabase.MetaKeys.LastPickedFolder) is { Length: > 0 } picked && picked != source)
        {
            Paths.Add(new PathRow("Ô thư mục ở trang Nhập", picked, "Thư mục / link đã nhập lần trước ở trang Nhập dữ liệu."));
        }

        var email = GoogleServices.Auth?.Email;
        var driveFolder = _database.GetMeta(MeRonDatabase.MetaKeys.DriveUploadFolder);
        Paths.Add(new PathRow("Google Drive (xuất lên)",
            string.IsNullOrWhiteSpace(driveFolder) ? string.Empty : driveFolder,
            (GoogleServices.Auth is null ? "Chưa cấu hình Google." : email is null ? "Chưa đăng nhập Google." : $"Tài khoản {email}.") +
            (string.IsNullOrWhiteSpace(driveFolder) ? " Folder: \"MeRon backup\" ở My Drive (mặc định)." : " Folder đã dán ở trang Xuất.")));
    }

    private void RefreshCleanups()
    {
        Cleanups.Clear();
        foreach (var (folder, record) in DataMaintenance.ListCleanups(BackupParent))
        {
            Cleanups.Add(new CleanupRow(folder, record, _maintenance.Missing(record)));
        }
        HasCleanups = Cleanups.Count > 0;
    }

    /// <summary>Bấm dòng năm: mở / đóng 12 tháng ngay bên dưới.</summary>
    public void Toggle(PeriodRow row)
    {
        if (!row.IsYear)
        {
            return;
        }
        if (row.IsExpanded)
        {
            var index = Periods.IndexOf(row) + 1;
            while (index < Periods.Count && Periods[index].IsMonth)
            {
                Periods.RemoveAt(index);
            }
            row.IsExpanded = false;
        }
        else
        {
            Expand(row);
        }
    }

    private void Expand(PeriodRow row)
    {
        var index = Periods.IndexOf(row) + 1;
        foreach (var month in _maintenance.Months(row.Stats.Year).Reverse()) // mới nhất trước như dòng năm
        {
            Periods.Insert(index++, new PeriodRow(month));
        }
        row.IsExpanded = true;
    }

    public void RefreshPlan()
    {
        try
        {
            var plan = CurrentPlan = _maintenance.Plan(Options);
            CanCleanup = !plan.IsEmpty;
            var before = DbDate.Show(Cutoff.ToDateTime(TimeOnly.MinValue));
            PlanText = plan.IsEmpty
                ? $"Không có dữ liệu nào xoá được trước {before}."
                : $"Sẽ xoá dữ liệu trước {before}: {VnFormat.Number(plan.Orders)} đơn hàng ({VnFormat.Number(plan.OrderItems)} dòng món, " +
                  $"bán {VnFormat.Money(plan.OrderAmount)}), {VnFormat.Number(plan.Purchases)} phiếu nhập ({VnFormat.Number(plan.PurchaseItems)} khoản, " +
                  $"{VnFormat.Money(plan.PurchaseAmount)})" +
                  (DeleteCustomers ? $", {VnFormat.Number(plan.Customers)} khách hàng không còn đơn" : string.Empty) +
                  (DeleteProducts ? $", {VnFormat.Number(plan.Products)} món không còn bán" : string.Empty) + ".";
            KeptUnpaidText = plan.KeptUnpaid == 0
                ? string.Empty
                : $"Giữ lại {VnFormat.Number(plan.KeptUnpaid)} đơn chưa thanh toán trước {before} ({VnFormat.Money(plan.KeptUnpaidAmount)}) - " +
                  "đơn còn nợ không bao giờ bị xoá; thu tiền (Thanh toán) hoặc Huỷ ở trang Chi tiết rồi dọn lại.";
        }
        catch (Exception ex)
        {
            CurrentPlan = null;
            CanCleanup = false;
            KeptUnpaidText = string.Empty;
            PlanText = "Không tính được: " + ex.Message;
        }
    }

    /// <summary>Xuất bộ backup TSV (lỗi = dừng, không xoá gì) → xoá trong 1 transaction (có bản sao .db, ghi don-dep.json vào
    /// bộ vừa xuất để khôi phục) → đọc lại.</summary>
    public async Task CleanupAsync()
    {
        var options = Options;
        IsBusy = true;
        CleanupStatus = "Đang xuất bộ backup trước khi xoá...";
        try
        {
            var now = _now();
            var backup = await Task.Run(() => BackupExporter.Export(_database, BackupParent, now));
            CleanupStatus = "Đang xoá...";
            var done = await Task.Run(() => _maintenance.Cleanup(options, backup.Folder, now));
            AppLog.For("DataInfo").LogWarning("Dọn dữ liệu trước {Before}: {Plan}; backup {Folder}", options.Before, done, backup.Folder);
            CleanupStatus = $"Đã xoá {VnFormat.Number(done.Orders)} đơn hàng, {VnFormat.Number(done.Purchases)} phiếu nhập" +
                            (options.DeleteCustomersWithoutOrders ? $", {VnFormat.Number(done.Customers)} khách hàng" : string.Empty) +
                            (options.DeleteProductsNeverSold ? $", {VnFormat.Number(done.Products)} món" : string.Empty) +
                            (done.KeptUnpaid > 0 ? $"; giữ lại {VnFormat.Number(done.KeptUnpaid)} đơn chưa thanh toán" : string.Empty) +
                            $". Bộ backup trước khi xoá: {backup.Folder}";
            RemindExport = true;
        }
        catch (Exception ex)
        {
            AppLog.For("DataInfo").LogError(ex, "Dọn dữ liệu lỗi");
            CleanupStatus = "Không xoá gì - lỗi: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
        Refresh();
    }

    /// <summary>Thêm lại các dòng của 1 lần dọn còn thiếu; dòng đang có giữ nguyên.</summary>
    public async Task RestoreAsync(CleanupRow row)
    {
        IsBusy = true;
        RestoreStatus = "Đang khôi phục...";
        try
        {
            var result = await Task.Run(() => _maintenance.Restore(row.Folder, row.Record));
            AppLog.For("DataInfo").LogWarning("Khôi phục lần dọn {Folder}: {Result}", row.Folder, result);
            var total = result.Added.Values.Sum();
            RestoreStatus = $"Đã thêm lại {VnFormat.Number(result.AddedOf("Orders"))} đơn hàng ({VnFormat.Number(result.AddedOf("OrderItems"))} dòng món), " +
                            $"{VnFormat.Number(result.AddedOf("Purchases"))} phiếu nhập, {VnFormat.Number(result.AddedOf("Users"))} khách, " +
                            $"{VnFormat.Number(result.AddedOf("Products"))} món." +
                            (result.AlreadyThere > 0 ? $" {VnFormat.Number(result.AlreadyThere)} dòng đã có sẵn - giữ nguyên." : string.Empty) +
                            (result.Merged > 0 ? $" {VnFormat.Number(result.Merged)} khách / món trùng tên với cái đang có - nối đơn vào cái đang có." : string.Empty) +
                            (result.NewIds > 0 ? $" {VnFormat.Number(result.NewIds)} dòng mang số mới (số cũ đã bị dòng khác dùng)." : string.Empty) +
                            (result.NotInBackup > 0 ? $" {VnFormat.Number(result.NotInBackup)} dòng không có trong bộ backup - bỏ qua." : string.Empty);
            RemindExport = total > 0 || RemindExport;
        }
        catch (Exception ex)
        {
            AppLog.For("DataInfo").LogError(ex, "Khôi phục lỗi");
            RestoreStatus = "Không khôi phục gì - lỗi: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
        Refresh();
    }

    // ---------- mở thư mục / link ----------

    internal static bool IsUrl(string path) => path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static bool CanOpen(string path) => path.Length > 0 && (IsUrl(path) || Directory.Exists(path) || File.Exists(path));

    /// <summary>Link → trình duyệt; thư mục → Explorer; file → Explorer chọn sẵn file.</summary>
    public static void Open(string path)
    {
        if (IsUrl(path) || Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(IsUrl(path) ? path : "explorer.exe", IsUrl(path) ? string.Empty : $"\"{path}\"") { UseShellExecute = true });
        }
        else if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
    }

    private static string Span(string what, DateOnly? first, DateOnly? last) =>
        first is null ? $"{what}: chưa có." : $"{what}: từ {DbDate.Show(first.Value.ToDateTime(TimeOnly.MinValue))} đến {DbDate.Show(last!.Value.ToDateTime(TimeOnly.MinValue))}";

    private static string FileSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} byte",
    };
}
