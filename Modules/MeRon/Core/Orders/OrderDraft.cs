using MeRon.Core.Data;
using MeRon.Core.Models;

namespace MeRon.Core.Orders;

/// <summary>
/// 1 dòng món đang nhập trên form. 2 loại:
/// - món trong danh mục: <see cref="ProductId"/> + số lượng, đơn giá lấy theo giá hiện tại của món;
/// - món tự do (<see cref="IsFree"/>): tự nhập tên + đơn giá, không gắn với món nào.
/// <see cref="OriginalProductId"/> / <see cref="SnapshotPrice"/>: món + đơn giá đã lưu của dòng (khi sửa đơn) - dùng để
/// khoá dòng đã đổi giá (xem <see cref="OrderCalculator.IsLocked"/>).
/// </summary>
public sealed record OrderLineDraft(
    long? ProductId,
    long Quantity,
    bool IsFree = false,
    string? FreeName = null,
    long? FreePrice = null,
    long? OriginalProductId = null,
    long? SnapshotPrice = null);

/// <summary>Toàn bộ form đơn hàng. <see cref="Id"/> 0 = đơn mới.</summary>
public sealed record OrderDraft(
    long Id,
    long? UserId,
    OrderStatus OriginalStatus,
    DateTime Delivery,
    bool IsPrepaid,
    DateTime? Payment,
    long Discount,
    string? Note,
    IReadOnlyList<OrderLineDraft> Lines);

/// <summary>Bản nháp đơn mới chép từ 1 đơn cũ + các điều cần báo người dùng (giá đổi, món không còn).</summary>
public sealed record OrderCopy(OrderDraft Draft, IReadOnlyList<string> Notices);

/// <summary>
/// Nhân bản đơn (khách quen đặt lặp lại): chép khách, các món + số lượng, món tự do, giảm giá, ghi chú. Đơn mới luôn là
/// Đã lên đơn, chưa thanh toán; ngày giờ giao giữ nguyên như đơn gốc (đơn gốc không có ngày giao → <c>fallbackDay</c>);
/// món thường tính giá hiện tại (không khoá giá cũ); món không còn trong danh sách chép thành món tự do với tên + đơn giá
/// lúc đặt.
/// </summary>
public static class OrderCopier
{
    public static OrderCopy CopyAsNew(Order source, DateOnly fallbackDay, IReadOnlyDictionary<long, Product> products)
    {
        var notices = new List<string>();
        var lines = new List<OrderLineDraft>();
        foreach (var item in source.Items)
        {
            if (item.ProductId is { } productId && products.TryGetValue(productId, out var product))
            {
                lines.Add(new OrderLineDraft(productId, item.Quantity));
                var price = product.Price ?? 0;
                if (price != item.SnapshotPrice)
                {
                    notices.Add($"{product.Name}: {VnFormat.Money(item.SnapshotPrice)} → {VnFormat.Money(price)} (giá hiện tại)");
                }
            }
            else
            {
                lines.Add(new OrderLineDraft(null, item.Quantity, true, item.SnapshotName, item.SnapshotPrice));
                if (item.ProductId is not null)
                {
                    notices.Add($"Món \"{item.SnapshotName}\" không còn trong danh sách - chép thành món tự do");
                }
            }
        }

        var delivery = source.Delivery ?? fallbackDay.ToDateTime(TimeOnly.MinValue);
        var draft = new OrderDraft(0, source.UserId, OrderStatus.Shipping, delivery, false, null,
            source.DiscountValue, source.Note, lines);
        return new OrderCopy(draft, notices);
    }
}

/// <summary>Lỗi ở 1 dòng món tự do - form đưa con trỏ về đúng ô (như điện thoại).</summary>
public enum OrderLineField
{
    Product,
    FreeName,
    FreePrice,
}

public sealed class OrderValidationException(string message, int? lineIndex = null, OrderLineField? field = null)
    : Exception(message)
{
    public int? LineIndex { get; } = lineIndex;

    public OrderLineField? Field { get; } = field;
}

/// <summary>Tính tiền đơn theo đúng quy tắc của điện thoại (order_form_notifier.dart).</summary>
public static class OrderCalculator
{
    /// <summary>
    /// Dòng bị khoá: đang sửa đơn, dòng vẫn là món đã đặt, và giá món hiện tại khác đơn giá lúc đặt → giữ nguyên đơn giá
    /// cũ, không cho đổi món / số lượng / xoá dòng (muốn đổi phải xoá cả đơn và tạo lại). Món tự do không bao giờ bị khoá.
    /// </summary>
    public static bool IsLocked(OrderLineDraft line, IReadOnlyDictionary<long, Product> products) =>
        !line.IsFree
        && line.SnapshotPrice is { } snapshot
        && line.ProductId is { } productId
        && line.OriginalProductId == productId
        && products.TryGetValue(productId, out var product)
        && (product.Price ?? 0) != snapshot;

    public static long UnitPrice(OrderLineDraft line, IReadOnlyDictionary<long, Product> products)
    {
        if (line.IsFree)
        {
            return line.FreePrice ?? 0;
        }
        if (IsLocked(line, products))
        {
            return line.SnapshotPrice!.Value;
        }
        return line.ProductId is { } id && products.TryGetValue(id, out var product) ? product.Price ?? 0 : 0;
    }

    public static long LineTotal(OrderLineDraft line, IReadOnlyDictionary<long, Product> products) =>
        UnitPrice(line, products) * line.Quantity;

    public static long SubTotal(IEnumerable<OrderLineDraft> lines, IReadOnlyDictionary<long, Product> products) =>
        lines.Sum(l => LineTotal(l, products));

    /// <summary>Tổng = Tạm tính - Giảm giá, không âm.</summary>
    public static long Total(long subTotal, long discount) => Math.Max(0, subTotal - discount);

    /// <summary>Tổng số lượng món (cả món tự do) - nút ⓘ cạnh ô Giảm giá.</summary>
    public static long TotalQuantity(IEnumerable<OrderLineDraft> lines) => lines.Sum(l => l.Quantity);

    /// <summary>Kiểm tra trước khi lưu: phải chọn khách và có ít nhất 1 dòng (nút Lưu của điện thoại tắt khi thiếu);
    /// món tự do phải có tên và đơn giá &gt; 0; dòng thường phải chọn món.</summary>
    public static void Validate(OrderDraft draft)
    {
        if (draft.UserId is null)
        {
            throw new OrderValidationException("Chưa chọn khách hàng.");
        }
        if (draft.Lines.Count == 0)
        {
            throw new OrderValidationException("Đơn chưa có món nào - bấm Thêm món hoặc Thêm món tự do.");
        }
        if (draft.Discount < 0)
        {
            throw new OrderValidationException("Giảm giá không được âm.");
        }
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var line = draft.Lines[i];
            if (line.Quantity < 1)
            {
                throw new OrderValidationException("Số lượng phải từ 1 trở lên.", i, OrderLineField.Product);
            }
            if (line.IsFree)
            {
                if (string.IsNullOrWhiteSpace(line.FreeName))
                {
                    throw new OrderValidationException("Vui lòng nhập tên cho món tự do", i, OrderLineField.FreeName);
                }
                if ((line.FreePrice ?? 0) <= 0)
                {
                    throw new OrderValidationException("Vui lòng nhập đơn giá hợp lệ cho món tự do", i, OrderLineField.FreePrice);
                }
            }
            else if (line.ProductId is null)
            {
                throw new OrderValidationException("Chưa chọn món ăn.", i, OrderLineField.Product);
            }
        }
    }

    /// <summary>
    /// Trạng thái khi lưu: như điện thoại - bật "Đã thanh toán trước" = Đã thanh toán, tắt = Đã lên đơn. Khác điện thoại: đơn
    /// đã Huỷ giữ nguyên Huỷ (điện thoại lưu lại đơn huỷ là âm thầm bỏ huỷ).
    /// </summary>
    public static OrderStatus StatusOnSave(OrderDraft draft) =>
        draft.OriginalStatus == OrderStatus.Cancelled && draft.Id != 0
            ? OrderStatus.Cancelled
            : draft.IsPrepaid ? OrderStatus.Completed : OrderStatus.Shipping;
}
