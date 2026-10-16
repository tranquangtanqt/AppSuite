using System.Collections.ObjectModel;
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

/// <summary>Mốc dọn nhanh (combobox): xoá dữ liệu có ngày trước mốc.</summary>
public enum CleanupPreset
{
    BeforeThisYear,
    OlderThanOneYear,
    OlderThanTwoYears,
    CustomDate,
}

/// <summary>
/// Trang Thông tin dữ liệu: xem toàn bộ dữ liệu đang có (số dòng, khoảng ngày, theo năm → tháng, dữ liệu thừa) rồi dọn
/// dữ liệu cũ (đơn hàng + phiếu nhập trước 1 mốc, tuỳ chọn khách / món không còn dùng). Trước khi xoá luôn xuất 1 bộ backup
/// TSV (lỗi thì không xoá) + bản sao .db; xoá xong nhắc xuất sang điện thoại.
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

    [ObservableProperty]
    private bool _canCleanup;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _cleanupStatus = string.Empty;

    /// <summary>Có thay đổi chưa xuất sau khi dọn → khung nhắc xuất sang điện thoại.</summary>
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

    partial void OnPresetChanged(CleanupPreset value) => RefreshPlan();

    partial void OnCutoffDateChanged(DateTimeOffset? value) => RefreshPlan();

    partial void OnDeleteCustomersChanged(bool value) => RefreshPlan();

    partial void OnDeleteProductsChanged(bool value) => RefreshPlan();

    public void Refresh()
    {
        try
        {
            var overview = _maintenance.Overview();
            FileText = $"File dữ liệu: {FileSize(overview.FileBytes)} - {_database.Path}";
            var unsynced = _database.GetUnsyncedEdits();
            var import = _database.GetMeta(MeRonDatabase.MetaKeys.LastImportBackupName);
            SyncText = (import is null ? "Chưa nhập bộ backup nào." : $"Bộ backup nhập gần nhất: {import}.")
                       + (unsynced.Any ? $" Có {VnFormat.Number(unsynced.Count)} dòng thay đổi trên máy tính chưa xuất." : " Không có thay đổi chưa xuất.");
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
        }
        catch (Exception ex)
        {
            AppLog.For("DataInfo").LogError(ex, "Đọc thông tin dữ liệu lỗi");
            FileText = "Không đọc được dữ liệu: " + ex.Message;
        }
        RefreshPlan();
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
                ? $"Không có dữ liệu nào trước {before}."
                : $"Sẽ xoá dữ liệu trước {before}: {VnFormat.Number(plan.Orders)} đơn hàng ({VnFormat.Number(plan.OrderItems)} dòng món, " +
                  $"bán {VnFormat.Money(plan.OrderAmount)}), {VnFormat.Number(plan.Purchases)} phiếu nhập ({VnFormat.Number(plan.PurchaseItems)} khoản, " +
                  $"{VnFormat.Money(plan.PurchaseAmount)})" +
                  (DeleteCustomers ? $", {VnFormat.Number(plan.Customers)} khách hàng không còn đơn" : string.Empty) +
                  (DeleteProducts ? $", {VnFormat.Number(plan.Products)} món không còn bán" : string.Empty) + ".";
        }
        catch (Exception ex)
        {
            CurrentPlan = null;
            CanCleanup = false;
            PlanText = "Không tính được: " + ex.Message;
        }
    }

    /// <summary>Thư mục chứa bộ backup tự xuất trước khi dọn: thư mục con "Truoc khi don" trong thư mục Xuất - không nằm
    /// ngay trong thư mục Xuất nên Nhập dữ liệu (chọn thư mục cha = bản mới nhất) không lấy nhầm bản trước khi dọn.</summary>
    public string BackupParent => Path.Combine(
        _database.GetMeta(MeRonDatabase.MetaKeys.LastExportFolder)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeRon"),
        "Truoc khi don");

    /// <summary>Xuất bộ backup TSV (lỗi = dừng, không xoá gì) → xoá trong 1 transaction (có bản sao .db) → đọc lại.</summary>
    public async Task CleanupAsync()
    {
        var options = Options;
        IsBusy = true;
        CleanupStatus = "Đang xuất bộ backup trước khi xoá...";
        try
        {
            var backup = await Task.Run(() => BackupExporter.Export(_database, BackupParent, _now()));
            CleanupStatus = "Đang xoá...";
            var done = await Task.Run(() => _maintenance.Cleanup(options));
            AppLog.For("DataInfo").LogWarning("Dọn dữ liệu trước {Before}: {Plan}; backup {Folder}", options.Before, done, backup.Folder);
            CleanupStatus = $"Đã xoá {VnFormat.Number(done.Orders)} đơn hàng, {VnFormat.Number(done.Purchases)} phiếu nhập" +
                            (options.DeleteCustomersWithoutOrders ? $", {VnFormat.Number(done.Customers)} khách hàng" : string.Empty) +
                            (options.DeleteProductsNeverSold ? $", {VnFormat.Number(done.Products)} món" : string.Empty) +
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

    private static string Span(string what, DateOnly? first, DateOnly? last) =>
        first is null ? $"{what}: chưa có." : $"{what}: từ {DbDate.Show(first.Value.ToDateTime(TimeOnly.MinValue))} đến {DbDate.Show(last!.Value.ToDateTime(TimeOnly.MinValue))}";

    private static string FileSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} byte",
    };
}
