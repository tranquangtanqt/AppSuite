namespace MeRon.Core.Models;

/// <summary>Trạng thái đơn - cùng giá trị với điện thoại (core/enums/order_status.dart).</summary>
public enum OrderStatus
{
    /// <summary>Đã lên đơn (mặc định khi tạo, chưa thanh toán).</summary>
    Shipping = 1,

    /// <summary>Đã thanh toán.</summary>
    Completed = 2,

    /// <summary>Huỷ - giữ lại để tra cứu, không tính vào báo cáo.</summary>
    Cancelled = 3,
}

public static class OrderStatusText
{
    public static string Label(OrderStatus status) => status switch
    {
        OrderStatus.Completed => "Đã thanh toán",
        OrderStatus.Cancelled => "Huỷ",
        _ => "Đã lên đơn",
    };

    /// <summary>Giá trị lạ trong dữ liệu → Đã lên đơn (như OrderStatusExtension.fromValue của điện thoại).</summary>
    public static OrderStatus FromValue(long? value) =>
        value is 2 ? OrderStatus.Completed : value is 3 ? OrderStatus.Cancelled : OrderStatus.Shipping;
}

/// <summary>1 dòng món của đơn. <see cref="ProductId"/> null = món tự do (chỉ có tên + đơn giá lúc đặt).</summary>
public sealed record OrderItem(long Id, long OrderId, long? ProductId, string? SnapshotName, long SnapshotPrice, long Quantity, long LineTotal);

/// <summary>Đơn hàng kèm tên khách và các dòng món. Ngày giờ giữ nguyên chuỗi trong database.</summary>
public sealed record Order(
    long Id,
    long? UserId,
    string? UserName,
    OrderStatus Status,
    string? DeliveryDatetime,
    string? PaymentDatetime,
    long DiscountValue,
    long SubTotal,
    long Total,
    string? Note,
    IReadOnlyList<OrderItem> Items)
{
    public DateTime? Delivery => DbDate.Parse(DeliveryDatetime);

    public DateTime? Payment => DbDate.Parse(PaymentDatetime);
}

/// <summary>Cách sắp xếp danh sách đơn (như OrderSortOption của điện thoại); <see cref="Default"/> = ngày giao mới nhất trước.</summary>
public enum OrderSort
{
    Default,
    TotalAscending,
    TotalDescending,
    DeliveryAscending,
    DeliveryDescending,
}

/// <summary>Bộ lọc danh sách đơn. Ngày tính theo NGÀY giao (cả ngày From và To).</summary>
public sealed record OrderFilter(OrderStatus? Status, DateOnly? From, DateOnly? To, long? UserId = null, OrderSort Sort = OrderSort.Default)
{
    /// <summary>Mặc định khi mở trang Đặt hàng (như điện thoại): Đã lên đơn, hôm nay, mọi khách.</summary>
    public static OrderFilter Today()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new OrderFilter(OrderStatus.Shipping, today, today);
    }
}
