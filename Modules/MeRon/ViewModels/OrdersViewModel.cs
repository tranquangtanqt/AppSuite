using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>Tham số mở form đơn: <see cref="OrderId"/> null = đơn mới (điền sẵn khách / ngày giao theo bộ lọc đang chọn);
/// <see cref="CopyFromId"/> = đơn mới chép từ đơn đó (Sao chép đơn), giữ ngày giao của đơn gốc;
/// kèm <see cref="InitialDelivery"/> thì giao ngày đó (Đặt lại như lần trước ở trang Khách lâu không đặt).</summary>
public sealed record OrderFormArgs(long? OrderId, long? InitialUserId = null, DateOnly? InitialDelivery = null, long? CopyFromId = null);

/// <summary>Tham số màn Chi tiết: bộ lọc của danh sách đơn (hoặc của báo cáo, Đợt 3).</summary>
public sealed record OrderDetailArgs(OrderFilter Filter);

/// <summary>
/// Trang Đặt hàng: bộ lọc trạng thái / khách / khoảng ngày giao + sắp xếp, danh sách thẻ đơn. Như điện thoại: vào trang từ
/// menu thì bộ lọc về mặc định (Đã lên đơn, hôm nay); quay lại từ form / Chi tiết thì giữ nguyên.
/// Khác điện thoại: đổi ngày cũng tìm ngay (không cần bấm kính lúp), tải hết đơn khớp bộ lọc (không "Xem thêm").
/// </summary>
public sealed partial class OrdersViewModel : ObservableObject
{
    private bool _suspendReload;

    public OrdersViewModel()
    {
        Reset();
    }

    public IReadOnlyList<CustomerOption> Customers { get; private set; } = [];

    public ObservableCollection<OrderCardViewModel> Cards { get; } = [];

    /// <summary>null = Tất cả.</summary>
    [ObservableProperty]
    private OrderStatus? _status;

    [ObservableProperty]
    private DateTimeOffset? _fromDate;

    [ObservableProperty]
    private DateTimeOffset? _toDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    private CustomerOption? _customer;

    public bool HasCustomer => Customer is not null;

    [ObservableProperty]
    private OrderSort _sort;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    public bool IsEmpty => Cards.Count == 0;

    partial void OnStatusChanged(OrderStatus? value) => AutoReload();

    partial void OnFromDateChanged(DateTimeOffset? value) => AutoReload();

    partial void OnToDateChanged(DateTimeOffset? value) => AutoReload();

    partial void OnCustomerChanged(CustomerOption? value) => AutoReload();

    partial void OnSortChanged(OrderSort value) => AutoReload();

    private void AutoReload()
    {
        if (!_suspendReload)
        {
            Load();
        }
    }

    /// <summary>Bộ lọc mặc định (vào trang từ menu).</summary>
    public void Reset()
    {
        _suspendReload = true;
        var today = OrderFilter.Today();
        Status = today.Status;
        FromDate = ToOffset(today.From);
        ToDate = ToOffset(today.To);
        Customer = null;
        Sort = OrderSort.Default;
        _suspendReload = false;
    }

    public OrderFilter Filter => new(Status, DayOf(FromDate), DayOf(ToDate), Customer?.Id, Sort);

    public void Load()
    {
        try
        {
            Customers = CustomerOption.From(AppServices.MasterData.GetCustomers());
            if (Customer is not null)
            {
                // khách có thể vừa đổi tên / bị xoá (nhập lại backup)
                _suspendReload = true;
                Customer = Customers.FirstOrDefault(c => c.Id == Customer.Id);
                _suspendReload = false;
            }
            var orders = AppServices.Orders.Query(Filter);
            Cards.Clear();
            foreach (var order in orders)
            {
                Cards.Add(new OrderCardViewModel(order));
            }
            Summary = orders.Count == 0
                ? "Không có đơn nào khớp bộ lọc."
                : $"{VnFormat.Number(orders.Count)} đơn  ·  Tổng {VnFormat.Money(orders.Sum(o => o.Total))}";
            Error = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.For("Orders").LogError(ex, "Tải đơn hàng lỗi");
            Error = "Không đọc được đơn hàng: " + ex.Message;
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Tham số cho đơn mới: điền sẵn khách đang lọc; Từ ngày = Đến ngày thì điền sẵn ngày giao đó (như điện thoại).</summary>
    public OrderFormArgs NewOrderArgs()
    {
        var from = DayOf(FromDate);
        var to = DayOf(ToDate);
        return new OrderFormArgs(null, Customer?.Id, from is not null && from == to ? from : null);
    }
    /// <summary>Đặt nhanh Từ ngày / Đến ngày (1 lần tải lại).</summary>
    public void ApplyRange(DateRangePreset preset)
    {
        var (from, to) = DateRanges.Resolve(preset, DateOnly.FromDateTime(DateTime.Today));
        _suspendReload = true;
        FromDate = ToOffset(from);
        ToDate = ToOffset(to);
        _suspendReload = false;
        Load();
    }

    public static string RangeLabel(DateRangePreset preset) => DateRanges.Label(preset);

    public static string SortLabel(OrderSort sort) => sort switch
    {
        OrderSort.TotalAscending => "Tăng dần theo thành tiền",
        OrderSort.TotalDescending => "Giảm dần theo thành tiền",
        OrderSort.DeliveryAscending => "Tăng dần theo ngày giao đơn",
        OrderSort.DeliveryDescending => "Giảm dần theo ngày giao đơn",
        _ => "Mặc định (ngày giao mới nhất trước)",
    };

    private static DateTimeOffset? ToOffset(DateOnly? day) =>
        day is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;

    private static DateOnly? DayOf(DateTimeOffset? value) => value is { } v ? DateOnly.FromDateTime(v.Date) : null;
}

/// <summary>
/// Màn Chi tiết: TOÀN BỘ đơn khớp bộ lọc, gộp theo từng khách (thứ tự xuất hiện), mỗi khách có "Tổng đơn"; nút "Thanh toán
/// toàn bộ" khi còn đơn Đã lên đơn; "Chụp ảnh" (View) để gửi khách.
/// </summary>
public sealed partial class OrderDetailViewModel : ObservableObject
{
    private IReadOnlyList<Order> _orders = [];

    public OrderFilter Filter { get; private set; } = OrderFilter.Today();

    public ObservableCollection<CustomerOrderGroup> Groups { get; } = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _canPayAll;

    [ObservableProperty]
    private string _message = string.Empty;

    public void Load(OrderFilter filter)
    {
        Filter = filter;
        _orders = AppServices.Orders.Query(filter);
        Groups.Clear();
        foreach (var group in _orders.GroupBy(o => o.UserId ?? 0))
        {
            Groups.Add(new CustomerOrderGroup(group.First().UserName, group.Select(o => new OrderCardViewModel(o)).ToList()));
        }
        CanPayAll = _orders.Any(o => o.Status == OrderStatus.Shipping);
        OnPropertyChanged(nameof(HasGroups));
        Summary = _orders.Count == 0
            ? "Không có đơn nào."
            : $"{VnFormat.Number(Groups.Count)} khách  ·  {VnFormat.Number(_orders.Count)} đơn  ·  Tổng {VnFormat.Money(_orders.Sum(o => o.Total))}";
    }

    public int ShippingCount => _orders.Count(o => o.Status == OrderStatus.Shipping);

    public bool HasGroups => Groups.Count > 0;

    /// <summary>Thanh toán toàn bộ; trả về lỗi hoặc null.</summary>
    public string? PayAll()
    {
        var count = 0;
        var error = AppServices.Try("Thanh toán toàn bộ", () => count = AppServices.Orders.PayAll(_orders));
        if (error is null)
        {
            AppLog.For("Orders").LogInformation("Thanh toán toàn bộ {Count} đơn", count);
            Load(Filter);
            Message = $"Đã chuyển {VnFormat.Number(count)} đơn sang Đã thanh toán.";
        }
        return error;
    }
}

public sealed class CustomerOrderGroup(string? customerName, IReadOnlyList<OrderCardViewModel> orders)
{
    public string CustomerName { get; } = string.IsNullOrWhiteSpace(customerName) ? "(không có khách)" : customerName!;

    public IReadOnlyList<OrderCardViewModel> Orders { get; } = orders;

    public string TotalText => "Tổng đơn: " + VnFormat.Money(Orders.Sum(o => o.Order.Total));
}
