using MeRon.Core.Models;

namespace MeRon.Core.Orders;

/// <summary>
/// Dòng gợi ý "Lần trước" ở form thêm đơn (2026-10-19): chọn khách xong thấy ngay đơn gần nhất của khách -
/// "Lần trước (12/10): 2 Bánh bao xá xíu, 1 Chè · 34.000 đ" - Ctrl+L chép các món đó vào đơn đang nhập.
/// </summary>
public static class LastOrderHint
{
    /// <summary>Hiện tối đa bấy nhiêu món, còn lại ghi "+N món".</summary>
    public const int MaxItems = 4;

    /// <param name="products">Tên món theo danh mục hiện tại (món không còn / món tự do lấy tên lúc đặt).</param>
    public static string Text(Order order, IReadOnlyDictionary<long, Product> products, DateTime today)
    {
        var items = order.Items.Select(i => $"{VnFormat.Number(i.Quantity)} {Name(i, products)}").ToList();
        var list = items.Count == 0 ? "(không có món)"
            : string.Join(", ", items.Take(MaxItems)) + (items.Count > MaxItems ? $", +{items.Count - MaxItems} món" : string.Empty);
        return $"Lần trước ({Day(order.Delivery, today)}): {list} · {VnFormat.Money(order.Total)}";
    }

    /// <summary>dd/MM (cùng năm), dd/MM/yy (năm khác), "không có ngày giao".</summary>
    public static string Day(DateTime? day, DateTime today) => day is not { } d ? "không có ngày giao"
        : d.Year == today.Year ? d.ToString("dd/MM") : d.ToString("dd/MM/yy");

    private static string Name(OrderItem item, IReadOnlyDictionary<long, Product> products) =>
        (item.ProductId is { } id && products.TryGetValue(id, out var p) ? p.Name : item.SnapshotName)?.Trim() is { Length: > 0 } name
            ? name
            : "(món không tên)";
}
