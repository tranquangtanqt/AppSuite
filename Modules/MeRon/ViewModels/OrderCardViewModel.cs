using MeRon.Core;
using MeRon.Core.Models;

namespace MeRon.ViewModels;

/// <summary>1 dòng món trên thẻ đơn: tên, đơn giá, xSố lượng, thành tiền.</summary>
public sealed record OrderCardLine(string Name, string Price, string Quantity, string LineTotal);

/// <summary>
/// Thẻ đơn hàng (danh sách Đặt hàng + màn Chi tiết) như order_card / order_detail_card của điện thoại: tên khách + ngày
/// giao, các dòng món, Tạm tính / Giảm giá (chỉ khi có giảm giá), Tổng, ghi chú; nền theo trạng thái.
/// </summary>
public sealed partial class OrderCardViewModel(Order order) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public Order Order { get; } = order;

    /// <summary>Trang Đặt hàng, chế độ Chọn nhiều: đơn đang được tick.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isSelected;

    /// <summary>Đang ở chế độ Chọn nhiều - thẻ hiện ô tick.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _showCheck;

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

    /// <summary>Ghi chú không kèm dấu [Cần xác nhận…] (dấu hiện riêng ở <see cref="ConfirmText"/>).</summary>
    public string Note { get; } = Core.Orders.OrderConfirm.Parse(order.Note).Rest;

    public bool HasNote => Note.Length > 0;

    /// <summary>Lý do cần xác nhận (chưa rõ khách, chưa có giá, tự đánh dấu); rỗng = không cần.</summary>
    public IReadOnlyList<string> ConfirmReasons { get; } = Core.Orders.OrderConfirm.Reasons(order);

    public bool NeedsConfirm => ConfirmReasons.Count > 0;

    public string ConfirmText => "Cần xác nhận: " + string.Join(" · ", ConfirmReasons);

    /// <summary>Đơn đang ghi vào khách chung "Chưa rõ khách" → menu chuột phải có Chọn khách đúng.</summary>
    public bool IsUnknownCustomer => Core.Orders.OrderConfirm.IsUnknownCustomer(Order.UserName);

    /// <summary>Tên cho trình đọc màn hình / UI Automation.</summary>
    public string AccessibleName => $"Đơn {Id} - {CustomerName} - {DeliveryText} - {StatusText} - {TotalText}" + (NeedsConfirm ? " - " + ConfirmText : string.Empty);
}

/// <summary>Lựa chọn khách hàng trong ô tìm khách (lọc đơn, form đơn).</summary>
public sealed record CustomerOption(long Id, string Name, string? Phone, string? Address)
{
    /// <summary>Gợi ý cuối danh sách ở form đơn: chưa có khách tên này → mở hộp thoại Thêm khách hàng (<see cref="Name"/> = chữ đang gõ).</summary>
    public bool IsNew { get; init; }

    public string Display => IsNew ? $"＋ Thêm khách mới \"{Name}\"" : string.IsNullOrWhiteSpace(Phone) ? Name : $"{Name}  ·  {Phone}";

    public static CustomerOption New(string name) => new(0, name.Trim(), null, null) { IsNew = true };

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
