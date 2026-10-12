using MeRon.Core;
using MeRon.Core.Models;

namespace MeRon.ViewModels;

/// <summary>1 dòng món trên thẻ đơn: tên, đơn giá, xSố lượng, thành tiền.</summary>
public sealed record OrderCardLine(string Name, string Price, string Quantity, string LineTotal);

/// <summary>
/// Thẻ đơn hàng (danh sách Đặt hàng + màn Chi tiết) như order_card / order_detail_card của điện thoại: tên khách + ngày
/// giao, các dòng món, Tạm tính / Giảm giá (chỉ khi có giảm giá), Tổng, ghi chú; nền theo trạng thái.
/// </summary>
public sealed class OrderCardViewModel(Order order)
{
    public Order Order { get; } = order;

    public long Id => Order.Id;

    public OrderStatus Status => Order.Status;

    public string CustomerName => string.IsNullOrWhiteSpace(Order.UserName) ? "(không có khách)" : Order.UserName!;

    public string DeliveryText => DbDate.Show(Order.Delivery);

    public string StatusText => OrderStatusText.Label(Order.Status);

    public IReadOnlyList<OrderCardLine> Lines { get; } = order.Items
        .Select(i => new OrderCardLine(i.SnapshotName ?? string.Empty, VnFormat.Money(i.SnapshotPrice), "x" + i.Quantity,
            VnFormat.Money(i.LineTotal)))
        .ToList();

    public bool HasDiscount => Order.DiscountValue > 0;

    public string SubTotalText => "Tạm tính: " + VnFormat.Money(Order.SubTotal);

    public string DiscountText => "Giảm giá: -" + VnFormat.Money(Order.DiscountValue);

    public string TotalText => VnFormat.Money(Order.Total);

    public bool HasNote => !string.IsNullOrWhiteSpace(Order.Note);

    public string Note => Order.Note?.Trim() ?? string.Empty;

    /// <summary>Tên cho trình đọc màn hình / UI Automation.</summary>
    public string AccessibleName => $"Đơn {Id} - {CustomerName} - {DeliveryText} - {StatusText} - {TotalText}";
}

/// <summary>Lựa chọn khách hàng trong ô tìm khách (lọc đơn, form đơn).</summary>
public sealed record CustomerOption(long Id, string Name, string? Phone, string? Address)
{
    public string Display => string.IsNullOrWhiteSpace(Phone) ? Name : $"{Name}  ·  {Phone}";

    public override string ToString() => Name;

    public static IReadOnlyList<CustomerOption> From(IEnumerable<Customer> customers) =>
        customers.Select(c => new CustomerOption(c.Id, c.Name ?? $"#{c.Id}", c.Phone, c.Address))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}

/// <summary>Lựa chọn món trong ô Món ăn của form đơn.</summary>
public sealed record ProductOption(long Id, string Name, long Price)
{
    public string Display => $"{Name}  ·  {VnFormat.Money(Price)}";

    public override string ToString() => Name;
}
