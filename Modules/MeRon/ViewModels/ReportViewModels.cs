using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Reports;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>1 dòng bảng trong báo cáo (món / khoản hàng): tên, số lượng, thành tiền.</summary>
/// <param name="Share">0–100: thanh tỷ lệ so với dòng lớn nhất (0 = không vẽ).</param>
public sealed record ReportLine(string Name, string Quantity, string Amount, double Share = 0)
{
    public bool HasShare => Share > 0;
}

/// <summary>Ô số liệu theo trạng thái (Đã lên đơn / Đã thanh toán / Huỷ).</summary>
/// <remarks><c>CountValue</c> / <c>AmountValue</c>: số đơn / tiền riêng (chữ lớn); <c>Share</c>: % số đơn trong kỳ.</remarks>
public sealed record StatusTile(OrderStatus Status, string Label, string Count, string Amount,
    string CountValue = "", string AmountValue = "", double Share = 0, string ShareText = "");

/// <summary>1 khách trong báo cáo: bấm để mở Chi tiết đơn của riêng khách đó.</summary>
public sealed record CustomerReportRow(int Rank, CustomerSummary Summary)
{
    public string Name => Summary.Name;

    public string Orders => $"{VnFormat.Number(Summary.OrderCount)} đơn";

    public string Amount => VnFormat.Money(Summary.Amount);

    public string RankText => Rank.ToString();

    public string AccessibleName => $"{Rank}. {Name} - {Orders} - {Amount}";
}

/// <summary>Sắp xếp "Chi tiết đơn hàng" của báo cáo Theo đơn hàng (điện thoại: _CustomerSortOption).</summary>
public enum CustomerSort
{
    AmountDescending,
    AmountAscending,
    DeliveryAscending,
    DeliveryDescending,
}

/// <summary>Khung chung: khoảng ngày (mặc định hôm nay), tải lại khi đổi, báo lỗi.</summary>
public abstract partial class ReportViewModel : ObservableObject
{
    protected ReportViewModel()
    {
        Range.Changed += (_, _) => Load();
    }

    public DateRangeFilter Range { get; } = new();

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    public void Load()
    {
        try
        {
            Refresh();
            Error = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.For("Reports").LogError(ex, "Tải báo cáo lỗi");
            Error = "Không đọc được dữ liệu: " + ex.Message;
        }
    }

    protected abstract void Refresh();

    protected static ReportLine Line(ProductSummary p) => new(p.Name, VnFormat.Number(p.Quantity), VnFormat.Money(p.Amount));
}

/// <summary>Thống kê theo đơn hàng: trạng thái + khách + khoảng ngày; tổng, theo trạng thái, theo món, theo khách.</summary>
public sealed partial class ReportOrderViewModel : ReportViewModel
{
    private bool _suspend;
    private IReadOnlyList<CustomerSummary> _customers = [];

    public IReadOnlyList<CustomerOption> Customers { get; private set; } = [];

    /// <summary>null = Tất cả. Mặc định Đã lên đơn như điện thoại.</summary>
    [ObservableProperty]
    private OrderStatus? _status = OrderStatus.Shipping;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    private CustomerOption? _customer;

    public bool HasCustomer => Customer is not null;

    [ObservableProperty]
    private CustomerSort _sort;

    [ObservableProperty]
    private string _orderCount = "0";

    [ObservableProperty]
    private string _amount = VnFormat.Money(0);

    [ObservableProperty]
    private string _totalQuantity = string.Empty;

    // Giao diện 2026-10-15: ô KPI Trung bình / đơn + Tổng số món
    [ObservableProperty]
    private string _averageAmount = VnFormat.Money(0);

    [ObservableProperty]
    private string _quantityValue = "0";

    public ObservableCollection<StatusTile> StatusTiles { get; } = [];

    public ObservableCollection<ReportLine> Products { get; } = [];

    public ObservableCollection<CustomerReportRow> CustomerRows { get; } = [];

    partial void OnStatusChanged(OrderStatus? value) => Reload();

    partial void OnCustomerChanged(CustomerOption? value) => Reload();

    partial void OnSortChanged(CustomerSort value) => ShowCustomers();

    private void Reload()
    {
        if (!_suspend)
        {
            Load();
        }
    }

    public void Reset()
    {
        _suspend = true;
        Range.Reset();
        Status = OrderStatus.Shipping;
        Customer = null;
        Sort = CustomerSort.AmountDescending;
        _suspend = false;
    }

    public OrderFilter Filter => new(Status, Range.From, Range.To, Customer?.Id);

    protected override void Refresh()
    {
        Customers = CustomerOption.From(AppServices.MasterData.GetCustomers());
        var report = AppServices.Reports.OrderReport(Filter);
        OrderCount = VnFormat.Number(report.OrderCount);
        Amount = VnFormat.Money(report.Amount);

        AverageAmount = VnFormat.Money(report.OrderCount > 0 ? report.Amount / report.OrderCount : 0);

        StatusTiles.Clear();
        var allCount = report.ByStatus.Sum(x => x.Count);
        foreach (var status in new[] { OrderStatus.Shipping, OrderStatus.Completed, OrderStatus.Cancelled })
        {
            var s = report.ByStatus.FirstOrDefault(x => x.Status == status);
            var count = s?.Count ?? 0;
            var share = allCount > 0 ? 100.0 * count / allCount : 0;
            StatusTiles.Add(new StatusTile(status, OrderStatusText.Label(status), $"Số đơn: {VnFormat.Number(count)}",
                $"Tiền: {VnFormat.Money(s?.Amount ?? 0)}", VnFormat.Number(count), VnFormat.Money(s?.Amount ?? 0), share,
                $"{share:0}% số đơn"));
        }

        Products.Clear();
        var maxQuantity = report.Products.Count > 0 ? report.Products.Max(p => p.Quantity) : 0;
        foreach (var p in report.Products)
        {
            Products.Add(Line(p) with { Share = maxQuantity > 0 ? 100.0 * p.Quantity / maxQuantity : 0 });
        }
        QuantityValue = VnFormat.Number(report.Products.Sum(p => p.Quantity));
        TotalQuantity = $"Tổng số món: {QuantityValue}";

        _customers = report.Customers;
        ShowCustomers();
        HasData = report.ByStatus.Count > 0;
    }

    /// <summary>Sắp ngay trên dữ liệu đã có. Theo ngày giao: tăng dần lấy đơn sớm nhất, giảm dần lấy đơn mới nhất của khách (như điện thoại).</summary>
    private void ShowCustomers()
    {
        IEnumerable<CustomerSummary> sorted = Sort switch
        {
            CustomerSort.AmountAscending => _customers.OrderBy(c => c.Amount),
            CustomerSort.DeliveryAscending => _customers.OrderBy(c => c.FirstDelivery ?? DateTime.MaxValue),
            CustomerSort.DeliveryDescending => _customers.OrderByDescending(c => c.LastDelivery ?? DateTime.MinValue),
            _ => _customers.OrderByDescending(c => c.Amount),
        };
        CustomerRows.Clear();
        var rank = 0;
        foreach (var c in sorted)
        {
            CustomerRows.Add(new CustomerReportRow(++rank, c));
        }
    }

    /// <summary>Chi tiết đơn của 1 khách trong đúng khoảng ngày + trạng thái đang lọc (không đụng bộ lọc trang Đặt hàng).</summary>
    public OrderDetailArgs DetailArgs(CustomerReportRow row) => new(new OrderFilter(Status, Range.From, Range.To, row.Summary.UserId));

    public static string SortLabel(CustomerSort sort) => sort switch
    {
        CustomerSort.AmountAscending => "Tăng dần theo thành tiền",
        CustomerSort.DeliveryAscending => "Tăng dần theo ngày giao đơn",
        CustomerSort.DeliveryDescending => "Giảm dần theo ngày giao đơn",
        _ => "Giảm dần theo thành tiền",
    };
}

/// <summary>Thống kê theo món ăn: khoảng ngày + (tuỳ chọn) 1 món; tổng số món, từng món, các đơn liên quan.</summary>
public sealed partial class ReportProductViewModel : ReportViewModel
{
    /// <summary>Mục đầu = tất cả món (Id 0).</summary>
    public static readonly ProductOption AllProducts = new(0, "(Tất cả món)", 0);

    public IReadOnlyList<ProductOption> ProductOptions { get; private set; } = [AllProducts];

    [ObservableProperty]
    private ProductOption _product = AllProducts;

    [ObservableProperty]
    private string _totalQuantity = string.Empty;

    // Ô số liệu: tổng tiền + số món khác nhau
    [ObservableProperty]
    private string _totalAmount = VnFormat.Money(0);

    [ObservableProperty]
    private string _productCount = "0";

    [ObservableProperty]
    private string _ordersTitle = string.Empty;

    public ObservableCollection<ReportLine> Products { get; } = [];

    public ObservableCollection<OrderCardViewModel> Orders { get; } = [];

    private bool _suspend;

    partial void OnProductChanged(ProductOption value)
    {
        if (!_suspend && value is not null)
        {
            Load();
        }
    }

    public void Reset()
    {
        _suspend = true;
        Range.Reset();
        Product = AllProducts;
        _suspend = false;
    }

    /// <summary>Nạp lại danh sách món (vào trang). Giữ món đang lọc: ComboBox bỏ chọn khi đổi ItemsSource (có thể đẩy null
    /// về đây) nên chọn lại đúng phần tử của danh sách mới; món đã bị xoá → Tất cả món.</summary>
    public void LoadProducts()
    {
        var id = Product?.Id ?? 0;
        _suspend = true;
        ProductOptions = [AllProducts, .. AppServices.MasterData.GetProducts()
            .Select(p => new ProductOption(p.Id, p.Name ?? $"#{p.Id}", p.Price ?? 0))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
        OnPropertyChanged(nameof(ProductOptions));
        // Qua null trước: record so sánh theo giá trị, gán thẳng món "bằng" món cũ thì không báo đổi và ComboBox không chọn lại.
        Product = null!;
        Product = ProductOptions.FirstOrDefault(p => p.Id == id) ?? AllProducts;
        _suspend = false;
    }

    protected override void Refresh()
    {
        var report = AppServices.Reports.ProductReport(Range.From, Range.To, Product.Id == 0 ? null : Product.Id);
        TotalQuantity = VnFormat.Number(report.TotalQuantity);
        TotalAmount = VnFormat.Money(report.Products.Sum(p => p.Amount));
        ProductCount = VnFormat.Number(report.Products.Count);
        Products.Clear();
        var maxQuantity = report.Products.Count > 0 ? report.Products.Max(p => p.Quantity) : 0;
        foreach (var p in report.Products)
        {
            Products.Add(Line(p) with { Share = maxQuantity > 0 ? 100.0 * p.Quantity / maxQuantity : 0 });
        }
        Orders.Clear();
        foreach (var o in report.Orders)
        {
            Orders.Add(new OrderCardViewModel(o));
        }
        OrdersTitle = $"Đơn hàng liên quan ({VnFormat.Number(report.Orders.Count)})";
        HasData = report.Products.Count > 0;
    }
}

/// <summary>Thống kê tổng hợp: đơn hàng (thu) + mua hàng (chi) cùng khoảng ngày.</summary>
public sealed partial class ReportSummaryViewModel : ReportViewModel
{
    [ObservableProperty]
    private string _orderCount = string.Empty;

    [ObservableProperty]
    private string _orderAmount = string.Empty;

    [ObservableProperty]
    private string _purchaseCount = string.Empty;

    [ObservableProperty]
    private string _purchaseAmount = string.Empty;

    [ObservableProperty]
    private string _difference = string.Empty;

    // Ô Chênh lệch: số tiền riêng (chữ lớn) + lỗ (âm) thì đổi màu
    [ObservableProperty]
    private string _differenceValue = string.Empty;

    [ObservableProperty]
    private bool _isLoss;

    public ObservableCollection<ReportLine> Products { get; } = [];

    public ObservableCollection<ReportLine> PurchaseItems { get; } = [];

    protected override void Refresh()
    {
        var r = AppServices.Reports.SummaryReport(Range.From, Range.To);
        OrderCount = VnFormat.Number(r.OrderCount);
        OrderAmount = VnFormat.Money(r.OrderAmount);
        PurchaseCount = VnFormat.Number(r.PurchaseCount);
        PurchaseAmount = VnFormat.Money(r.PurchaseAmount);
        var diff = r.OrderAmount - r.PurchaseAmount;
        DifferenceValue = $"{(diff < 0 ? "−" : string.Empty)}{VnFormat.Money(Math.Abs(diff))}";
        Difference = $"Chênh lệch (bán − mua): {DifferenceValue}";
        IsLoss = diff < 0;
        Products.Clear();
        var maxQuantity = r.Products.Count > 0 ? r.Products.Max(p => p.Quantity) : 0;
        foreach (var p in r.Products)
        {
            Products.Add(Line(p) with { Share = maxQuantity > 0 ? 100.0 * p.Quantity / maxQuantity : 0 });
        }
        PurchaseItems.Clear();
        var maxAmount = r.PurchaseItems.Count > 0 ? r.PurchaseItems.Max(i => i.Amount) : 0;
        foreach (var i in r.PurchaseItems)
        {
            PurchaseItems.Add(new ReportLine(i.Name, $"{VnFormat.Number(i.Count)} lần", VnFormat.Money(i.Amount),
                maxAmount > 0 ? 100.0 * i.Amount / maxAmount : 0));
        }
        HasData = r.OrderCount + r.PurchaseCount > 0;
    }
}

/// <summary>Khách hàng tiềm năng: xếp hạng theo tổng tiền đã đặt (tối đa 100 khách).</summary>
public sealed partial class ReportCustomerViewModel : ReportViewModel
{
    public ObservableCollection<CustomerReportRow> Rows { get; } = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    protected override void Refresh()
    {
        var top = AppServices.Reports.TopCustomers(Range.From, Range.To);
        Rows.Clear();
        var rank = 0;
        foreach (var c in top)
        {
            Rows.Add(new CustomerReportRow(++rank, c));
        }
        Summary = top.Count == 0
            ? "Không có đơn nào trong khoảng ngày này."
            : $"{VnFormat.Number(top.Count)} khách  ·  Tổng {VnFormat.Money(top.Sum(c => c.Amount))}";
        HasData = top.Count > 0;
    }

    /// <summary>Chi tiết đơn của khách trong khoảng ngày, mọi trạng thái (như điện thoại: status -1).</summary>
    public OrderDetailArgs DetailArgs(CustomerReportRow row) => new(new OrderFilter(null, Range.From, Range.To, row.Summary.UserId));
}

/// <summary>1 khách trên trang Khách lâu không đặt.</summary>
public sealed record LapsedRow(LapsedCustomer Customer)
{
    public string Name => Customer.Name;

    public string Contact => string.Join("  ·  ", new[] { Customer.Phone, Customer.Address }.Where(s => s is not null));

    public bool HasContact => Contact.Length > 0;

    public bool HasPhone => Customer.Phone is not null;

    public string LastText => $"Đặt lần cuối {Customer.LastDay:dd/MM/yyyy}  ·  {VnFormat.Number(Customer.DaysSince)} ngày trước";

    public string HabitText => Customer.AverageGapDays is { } gap
        ? $"Thường ~{VnFormat.Number((long)Math.Round(gap))} ngày đặt 1 lần"
        : "Chưa đủ 3 lần đặt để biết thói quen";

    public bool IsOverHabit => Customer.IsOverHabit;

    public string OverHabitText => Customer.OverdueRatio is { } ratio ? $"Nghỉ gấp {Math.Floor(ratio):0} lần thói quen" : string.Empty;

    public string OrdersText => $"{VnFormat.Number(Customer.OrderCount)} đơn  ·  {VnFormat.Money(Customer.Amount)}";

    public string ProductsText => Customer.TopProducts.Count > 0 ? "Hay đặt: " + string.Join(", ", Customer.TopProducts) : string.Empty;

    public string AccessibleName => $"{Name} - {LastText} - {OrdersText}";
}

/// <summary>
/// Khách lâu không đặt: khách đã đặt ít nhất N đơn mà đơn gần nhất cách hôm nay từ M ngày (xem <see cref="LapsedCustomers"/>).
/// Bộ lọc giữ suốt phiên (trang được cache); mỗi lần vào trang thì đọc lại dữ liệu.
/// </summary>
public sealed partial class LapsedCustomersViewModel : ObservableObject
{
    public static readonly int[] DayPresets = [14, 30, 60, 90];

    private IReadOnlyList<LapsedCustomer> _all = [];

    public ObservableCollection<LapsedRow> Rows { get; } = [];

    /// <summary>NumberBox dùng double; bỏ trống / nhỏ hơn 1 → về mặc định.</summary>
    [ObservableProperty]
    private double _minDays = LapsedCustomers.DefaultMinDays;

    [ObservableProperty]
    private double _minOrders = LapsedCustomers.DefaultMinOrders;

    [ObservableProperty]
    private LapsedSort _sort = LapsedSort.Longest;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    /// <summary>Kết quả thao tác (đã chép tin nhắn / SĐT).</summary>
    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    public static string SortLabel(LapsedSort sort) => sort switch
    {
        LapsedSort.Amount => "Tổng tiền đã mua nhiều nhất trước",
        LapsedSort.OverHabit => "Quá thói quen nhiều nhất trước",
        _ => "Lâu không đặt nhất trước",
    };

    partial void OnMinDaysChanged(double value)
    {
        if (double.IsNaN(value) || value < 1)
        {
            MinDays = LapsedCustomers.DefaultMinDays;
            return;
        }
        Load();
    }

    partial void OnMinOrdersChanged(double value)
    {
        if (double.IsNaN(value) || value < 1)
        {
            MinOrders = LapsedCustomers.DefaultMinOrders;
            return;
        }
        Load();
    }

    partial void OnSortChanged(LapsedSort value) => Apply();

    partial void OnSearchTextChanged(string value) => Apply();

    public void Load()
    {
        try
        {
            _all = AppServices.Reports.LapsedCustomers(DateOnly.FromDateTime(DateTime.Today), (int)Math.Round(MinDays), (int)Math.Round(MinOrders));
            Error = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.For("Reports").LogError(ex, "Tải khách lâu không đặt lỗi");
            Error = "Không đọc được dữ liệu: " + ex.Message;
            _all = [];
        }
        Apply();
    }

    private void Apply()
    {
        var shown = LapsedCustomers.Sort(
            _all.Where(c => TextSearch.Matches(c.Name, SearchText) || TextSearch.Matches(c.Phone, SearchText)), Sort);
        Rows.Clear();
        foreach (var c in shown)
        {
            Rows.Add(new LapsedRow(c));
        }
        var overHabit = shown.Count(c => c.IsOverHabit);
        Summary = _all.Count == 0
            ? "Không có khách nào lâu không đặt."
            : shown.Count == _all.Count
                ? $"{VnFormat.Number(_all.Count)} khách  ·  {VnFormat.Number(overHabit)} khách nghỉ quá thói quen"
                : $"{VnFormat.Number(shown.Count)} / {VnFormat.Number(_all.Count)} khách";
        HasData = shown.Count > 0;
    }
}
