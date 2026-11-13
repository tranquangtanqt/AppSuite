using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>Tham số mở form đơn: <see cref="OrderId"/> null = đơn mới (điền sẵn khách / ngày giao theo bộ lọc đang chọn);
/// <see cref="CopyFromId"/> = đơn mới chép từ đơn đó (Sao chép đơn), giữ ngày giao của đơn gốc;
/// kèm <see cref="InitialDelivery"/> thì giao ngày đó (Đặt lại như lần trước ở trang Khách lâu không đặt);
/// kèm <see cref="ForOtherCustomer"/> thì bỏ trống khách, giữ cả Đã thanh toán trước (Lưu và chép cho khách khác).</summary>
public sealed record OrderFormArgs(long? OrderId, long? InitialUserId = null, DateOnly? InitialDelivery = null, long? CopyFromId = null,
    bool ForOtherCustomer = false);

/// <summary>Tham số màn Chi tiết: bộ lọc của danh sách đơn (hoặc của báo cáo, Đợt 3).</summary>
/// <param name="Title">Dòng tiêu đề trên cùng phần được chụp ảnh (vd mở từ trang Công nợ: các đơn đang nợ của 1 khách); null = không có.</param>
public sealed record OrderDetailArgs(OrderFilter Filter, string? Title = null);

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

    partial void OnFromDateChanged(DateTimeOffset? value)
    {
        if (!FixRange(toChanged: false))
        {
            AutoReload();
        }
    }

    partial void OnToDateChanged(DateTimeOffset? value)
    {
        if (!FixRange(toChanged: true))
        {
            AutoReload();
        }
    }

    /// <summary>Từ ngày &gt; Đến ngày → kéo ô kia theo ô vừa đổi (<see cref="DateRanges.Fix"/>; lần đặt đó tự tải lại).</summary>
    private bool FixRange(bool toChanged)
    {
        var (from, to) = DateRanges.Fix(DayOf(FromDate), DayOf(ToDate), toChanged);
        if (from != DayOf(FromDate))
        {
            FromDate = ToOffset(from);
            return true;
        }
        if (to != DayOf(ToDate))
        {
            ToDate = ToOffset(to);
            return true;
        }
        return false;
    }

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
            foreach (var card in Cards)
            {
                card.PropertyChanged -= Card_PropertyChanged;
            }
            Cards.Clear();
            foreach (var order in orders)
            {
                // Chọn nhiều: tải lại (sau khi sửa, đổi bộ lọc) giữ tick của các đơn vẫn còn trong danh sách.
                var card = new OrderCardViewModel(order) { ShowCheck = SelectMode, IsSelected = SelectMode && _selectedIds.Contains(order.Id) };
                card.PropertyChanged += Card_PropertyChanged;
                Cards.Add(card);
            }
            _selectedIds.IntersectWith(orders.Select(o => o.Id));
            UpdateSelection();
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

    // ---------- Chọn nhiều: sửa hàng loạt đơn đã lưu (BulkEdit, 2026-11-13) ----------

    private readonly HashSet<long> _selectedIds = [];

    /// <summary>Thẻ bấm gần nhất (gốc của Shift + bấm chọn 1 dải).</summary>
    private int _anchor = -1;

    [ObservableProperty]
    private bool _selectMode;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _selectionText = string.Empty;

    /// <summary>Ô Tick hết: true = mọi đơn đang hiện, false = chưa chọn, null = 1 phần.</summary>
    [ObservableProperty]
    private bool? _allSelected = false;

    /// <summary>Báo kết quả lần sửa hàng loạt (InfoBar có nút Hoàn tác).</summary>
    [ObservableProperty]
    private string _bulkMessage = string.Empty;

    /// <summary>Lô vừa sửa - Hoàn tác trả các đơn còn y như lúc vừa sửa về như trước.</summary>
    private IReadOnlyList<BulkChange> _lastBulk = [];

    [ObservableProperty]
    private bool _canUndoBulk;

    partial void OnSelectModeChanged(bool value)
    {
        _selectedIds.Clear();
        _anchor = -1;
        foreach (var card in Cards)
        {
            card.ShowCheck = value;
            card.IsSelected = false;
        }
        UpdateSelection();
    }

    private void Card_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrderCardViewModel.IsSelected) && sender is OrderCardViewModel card)
        {
            if (card.IsSelected)
            {
                _selectedIds.Add(card.Id);
            }
            else
            {
                _selectedIds.Remove(card.Id);
            }
            UpdateSelection();
        }
    }

    /// <summary>Bấm thẻ ở chế độ Chọn nhiều: đổi tick; <paramref name="range"/> (Shift) = tick cả dải từ thẻ bấm trước tới thẻ này.</summary>
    public void Toggle(OrderCardViewModel card, bool range)
    {
        var index = Cards.IndexOf(card);
        if (range && _anchor >= 0 && _anchor < Cards.Count && index >= 0)
        {
            for (var i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++)
            {
                Cards[i].IsSelected = true;
            }
        }
        else
        {
            card.IsSelected = !card.IsSelected;
        }
        _anchor = index;
    }

    /// <summary>Ô Tick hết: đã chọn hết thì bỏ chọn hết, không thì chọn mọi đơn đang hiện (theo bộ lọc).</summary>
    public void ToggleAll()
    {
        var select = AllSelected != true;
        foreach (var card in Cards)
        {
            card.IsSelected = select;
        }
    }

    private void UpdateSelection()
    {
        var selected = Cards.Where(c => c.IsSelected).ToList();
        SelectedCount = selected.Count;
        AllSelected = selected.Count == 0 ? false : selected.Count == Cards.Count ? true : null;
        SelectionText = selected.Count == 0
            ? "Bấm vào thẻ để chọn đơn (Shift + bấm: chọn cả dải)"
            : $"Đã chọn {VnFormat.Number(selected.Count)} / {VnFormat.Number(Cards.Count)} đơn  ·  Tổng {VnFormat.Money(selected.Sum(c => c.Order.Total))}"
              + (selected.Any(c => c.Status == OrderStatus.Completed) ? $"  ·  {selected.Count(c => c.Status == OrderStatus.Completed)} đã thanh toán" : string.Empty);
    }

    /// <summary>Các đơn đang chọn, đọc lại từ database (có thể vừa sửa ở nơi khác) theo thứ tự trên danh sách.</summary>
    public IReadOnlyList<Order> SelectedOrders() =>
        Cards.Where(c => c.IsSelected).Select(c => AppServices.Orders.Get(c.Id)).OfType<Order>().ToList();

    /// <summary>Ghi lô đã xem trước; trả lỗi hoặc null. Nhớ lô để Hoàn tác.</summary>
    public string? ApplyBulk(BulkPlan plan)
    {
        var written = 0;
        var error = AppServices.Try(plan.Title, () => written = AppServices.Orders.ApplyBulk(plan.Changes));
        if (error is not null)
        {
            return error;
        }
        AppLog.For("Orders").LogInformation("Sửa hàng loạt: {Title} - {Count} đơn", plan.Title, written);
        _lastBulk = plan.Changes;
        CanUndoBulk = written > 0;
        BulkMessage = $"{plan.Title}: đã sửa {VnFormat.Number(written)} đơn." + (plan.Skipped.Count > 0 ? $" Bỏ qua {plan.Skipped.Count} đơn." : string.Empty);
        Load();
        return null;
    }

    /// <summary>Hoàn tác: (trả lại được, đã bị sửa / xoá sau đó - giữ nguyên).</summary>
    public (IReadOnlyList<BulkChange> Revert, IReadOnlyList<BulkChange> Keep) PlanUndoBulk()
    {
        var revert = new List<BulkChange>();
        var keep = new List<BulkChange>();
        foreach (var change in _lastBulk)
        {
            (BulkEdit.Untouched(change, AppServices.Orders.Get(change.Before.Id)) ? revert : keep).Add(change);
        }
        return (revert, keep);
    }

    public string? UndoBulk(IReadOnlyList<BulkChange> revert, int kept)
    {
        var count = 0;
        var error = AppServices.Try("Hoàn tác sửa hàng loạt", () => count = AppServices.Orders.RevertBulk(revert));
        if (error is not null)
        {
            return error;
        }
        AppLog.For("Orders").LogInformation("Hoàn tác sửa hàng loạt: {Count} đơn, giữ {Kept}", count, kept);
        _lastBulk = [];
        CanUndoBulk = false;
        BulkMessage = $"Đã hoàn tác {VnFormat.Number(count)} đơn." + (kept > 0 ? $" Giữ nguyên {kept} đơn đã sửa / xoá sau đó." : string.Empty);
        Load();
        return null;
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

    /// <summary>Tiêu đề trên ảnh chụp (<see cref="OrderDetailArgs.Title"/>).</summary>
    [ObservableProperty]
    private string _captureTitle = string.Empty;

    public void Load(OrderFilter filter, string? title = null)
    {
        CaptureTitle = title ?? string.Empty;
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
            Load(Filter, CaptureTitle);
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
