using MeRon.Core.Models;

namespace MeRon.Core.Orders;

/// <summary>1 đơn sẽ đổi: <see cref="Before"/> như đang có, <see cref="After"/> sau khi đổi (null = xoá đơn).
/// <see cref="Warn"/> = đơn đã thanh toán - hộp xem trước tô cam.</summary>
public sealed record BulkChange(Order Before, Order? After, string Description, bool Warn);

/// <summary>Đơn đã chọn mà không đổi + lý do (đã đúng ngày, đơn huỷ, không có món này...).</summary>
public sealed record BulkSkip(Order Order, string Reason);

/// <summary>Đổi tên 1 món trong Danh mục cùng lô sửa (Hoàn tác trả tên cũ nếu tên chưa bị đổi tiếp).</summary>
public sealed record BulkProductRename(long ProductId, string OldName, string NewName);

/// <summary>Kết quả xét 1 thao tác hàng loạt trước khi ghi - nội dung hộp xem trước. <see cref="Rename"/> = kèm đổi tên món
/// trong Danh mục (áp dụng được cả khi không đơn nào cần đổi).</summary>
public sealed record BulkPlan(string Title, IReadOnlyList<BulkChange> Changes, IReadOnlyList<BulkSkip> Skipped, BulkProductRename? Rename = null)
{
    public int WarnCount => Changes.Count(c => c.Warn);
}

/// <summary>1 món có trong các đơn đã chọn (ô Đổi tên món): món trong danh sách (<see cref="ProductId"/>) hoặc món tự do,
/// theo tên ghi trên đơn. <see cref="CatalogName"/> = tên hiện tại trong Danh mục món (khác tên trên đơn → đơn ghi tên cũ).</summary>
public sealed record BulkItemName(long? ProductId, string Name, int OrderCount, string? CatalogName)
{
    public bool IsFree => ProductId is null;

    public bool OutdatedName => CatalogName is not null && CatalogName != Name;

    public string Label => $"{Name}{(IsFree ? " (món tự do)" : string.Empty)} - {OrderCount} đơn"
                           + (OutdatedName ? $" · Danh mục đã đổi tên: {CatalogName}" : string.Empty);
}

/// <summary>
/// Sửa hàng loạt các đơn đã lưu (trang Đặt hàng, chế độ Chọn nhiều - người dùng chốt 2026-11-13): đổi ngày giao, ngày thanh toán
/// (đơn đã thanh toán đổi ngày, đơn chưa thanh toán chuyển sang Đã thanh toán), trả về Đã lên đơn, huỷ / khôi phục, xoá, đổi
/// tên món (món tự do: tên trên đơn; món trong Danh mục: đổi trong Danh mục, giữ giá) / thay bằng món khác (đơn đã thanh toán giữ giá, chỉ đổi món + tên), cập nhật tên theo Danh mục.
/// Chỉ tính ra trạng thái mới của từng đơn (<see cref="BulkPlan"/>), không ghi database.
/// </summary>
public static class BulkEdit
{
    private static string Day(DateTime? value) => value is null ? "(trống)" : DbDate.Show(value);

    private static BulkPlan Plan(string title, IEnumerable<Order> orders, Func<Order, (Order? After, string Text)?> change, Func<Order, string> skip,
        bool deleting = false)
    {
        var changes = new List<BulkChange>();
        var skipped = new List<BulkSkip>();
        foreach (var order in orders)
        {
            if (change(order) is { } c)
            {
                changes.Add(new BulkChange(order, deleting ? null : c.After, c.Text, order.Status == OrderStatus.Completed));
            }
            else
            {
                skipped.Add(new BulkSkip(order, skip(order)));
            }
        }
        return new BulkPlan(title, changes, skipped);
    }

    /// <summary>Đổi ngày giao, giữ giờ giao cũ (như menu chuột phải Giao hôm nay / ngày mai).</summary>
    public static BulkPlan DeliveryDay(IEnumerable<Order> orders, DateOnly day) =>
        Plan($"Đổi ngày giao sang {day:dd/MM/yyyy}", orders, o =>
        {
            if (o.Delivery is { } d && DateOnly.FromDateTime(d) == day)
            {
                return null;
            }
            var time = o.Delivery is { } old ? TimeOnly.FromDateTime(old) : TimeOnly.MinValue;
            return (o with { DeliveryDatetime = DbDate.Format(day.ToDateTime(time)) }, $"Ngày giao {Day(o.Delivery)} → {day:dd/MM/yyyy}");
        }, _ => "đã giao ngày này");

    /// <summary>Ngày thanh toán: đơn đã thanh toán → đổi ngày (giữ giờ cũ); đơn chưa thanh toán → Đã thanh toán ngày đó, giờ =
    /// <paramref name="time"/>; đơn huỷ bỏ qua.</summary>
    public static BulkPlan Payment(IEnumerable<Order> orders, DateOnly day, TimeOnly time) =>
        Plan($"Ngày thanh toán {day:dd/MM/yyyy}", orders, o => o.Status switch
        {
            OrderStatus.Cancelled => null,
            OrderStatus.Completed when o.Payment is { } p && DateOnly.FromDateTime(p) == day => null,
            OrderStatus.Completed => (o with { PaymentDatetime = DbDate.Format(day.ToDateTime(o.Payment is { } p ? TimeOnly.FromDateTime(p) : time)) },
                $"Ngày thanh toán {Day(o.Payment)} → {day:dd/MM/yyyy}"),
            _ => (o with { Status = OrderStatus.Completed, PaymentDatetime = DbDate.Format(day.ToDateTime(time)) },
                $"Đã lên đơn → Đã thanh toán ngày {day:dd/MM/yyyy}"),
        }, o => o.Status == OrderStatus.Cancelled ? "đơn huỷ" : "đã thanh toán ngày này");

    /// <summary>Trả về Đã lên đơn (lỡ tay thanh toán): bỏ ngày thanh toán.</summary>
    public static BulkPlan Unpay(IEnumerable<Order> orders) =>
        Plan("Trả về Đã lên đơn (chưa thanh toán)", orders, o => o.Status == OrderStatus.Completed
            ? (o with { Status = OrderStatus.Shipping, PaymentDatetime = null }, $"Đã thanh toán ({Day(o.Payment)}) → Đã lên đơn")
            : null, o => o.Status == OrderStatus.Cancelled ? "đơn huỷ" : "chưa thanh toán");

    /// <summary>Huỷ: giữ lại để tra cứu, không tính vào báo cáo (giữ ngày thanh toán để Khôi phục trả đúng trạng thái).</summary>
    public static BulkPlan Cancel(IEnumerable<Order> orders) =>
        Plan("Huỷ đơn", orders, o => o.Status == OrderStatus.Cancelled ? null
            : (o with { Status = OrderStatus.Cancelled }, $"{OrderStatusText.Label(o.Status)} → Huỷ"), _ => "đã huỷ");

    /// <summary>Khôi phục đơn huỷ: có ngày thanh toán thì về Đã thanh toán, không thì Đã lên đơn.</summary>
    public static BulkPlan Restore(IEnumerable<Order> orders) =>
        Plan("Khôi phục đơn huỷ", orders, o =>
        {
            if (o.Status != OrderStatus.Cancelled)
            {
                return null;
            }
            var status = o.Payment is not null ? OrderStatus.Completed : OrderStatus.Shipping;
            return (o with { Status = status }, $"Huỷ → {OrderStatusText.Label(status)}");
        }, _ => "không phải đơn huỷ");

    public static BulkPlan Delete(IEnumerable<Order> orders) =>
        Plan("Xoá đơn", orders, o => (null, "Xoá hẳn khỏi dữ liệu"), _ => string.Empty, deleting: true);

    // ---------- Món ----------

    private static string Key(string? name) => TextSearch.Fold(name).Trim();

    private static bool Same(OrderItem item, BulkItemName target) => item.ProductId == target.ProductId && Key(item.SnapshotName) == Key(target.Name);

    /// <summary>Các món có trong các đơn (gộp theo món + tên ghi trên đơn), nhiều đơn trước.</summary>
    public static IReadOnlyList<BulkItemName> ItemNames(IEnumerable<Order> orders, IReadOnlyDictionary<long, Product> products) =>
        orders.SelectMany(o => o.Items.Select(i => (Order: o.Id, Item: i)))
            .GroupBy(x => (x.Item.ProductId, Key(x.Item.SnapshotName)))
            .Select(g => new BulkItemName(g.Key.ProductId, g.First().Item.SnapshotName?.Trim() ?? string.Empty, g.Select(x => x.Order).Distinct().Count(),
                g.Key.ProductId is { } id && products.TryGetValue(id, out var p) ? p.Name?.Trim() : null))
            .OrderByDescending(n => n.OrderCount).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Sửa các dòng món khớp <paramref name="target"/> của 1 đơn; tính lại Tạm tính / Tổng (giữ giảm giá). Null = đơn không có món này.</summary>
    private static Order? EditItems(Order order, BulkItemName target, Func<OrderItem, OrderItem> edit)
    {
        if (!order.Items.Any(i => Same(i, target)))
        {
            return null;
        }
        var items = order.Items.Select(i => Same(i, target) ? edit(i) : i).ToList();
        var subTotal = items.Sum(i => i.LineTotal);
        return order with { Items = items, SubTotal = subTotal, Total = Math.Max(0, subTotal - order.DiscountValue) };
    }

    /// <summary>Đổi tên món ghi trên đơn, giữ giá + số lượng - dùng cho món tự do (gõ sai); món trong Danh mục dùng <see cref="RenameProduct"/>.</summary>
    public static BulkPlan RenameItem(IEnumerable<Order> orders, BulkItemName target, string newName)
    {
        var name = newName.Trim();
        return Plan($"Đổi tên món \"{target.Name}\" → \"{name}\"", orders,
            o => EditItems(o, target, i => i with { SnapshotName = name }) is { } after ? (after, $"\"{target.Name}\" → \"{name}\" (giữ giá)") : null,
            _ => "không có món này");
    }

    /// <summary>Đổi tên món trong Danh mục (món thực sự đổi tên): tên mới ghi vào Danh mục + mọi dòng món này trên các đơn đã
    /// chọn, giữ giá. Không cho đặt tên riêng trên đơn cho món trong Danh mục - form đơn lấy tên Danh mục, Lưu lại sẽ mất (2026-11-13).
    /// Null = tên trống / giống tên hiện tại / trùng món khác (kèm lời nhắc).</summary>
    public static (BulkPlan? Plan, string? Hint) RenameProduct(IEnumerable<Order> orders, BulkItemName target, string newName,
        IReadOnlyDictionary<long, Product> products)
    {
        if (target.ProductId is not { } id)
        {
            return (null, "Món tự do không có trong Danh mục.");
        }
        var name = newName.Trim();
        var old = target.CatalogName ?? target.Name;
        if (name.Length == 0)
        {
            return (null, "Gõ tên mới.");
        }
        if (name == old)
        {
            return (null, "Tên mới giống tên trong Danh mục.");
        }
        if (products.Values.FirstOrDefault(p => p.Id != id && Key(p.Name) == Key(name)) is { } same)
        {
            return (null, $"Danh mục đã có món \"{same.Name?.Trim()}\" - muốn gộp thì dùng Thay bằng món khác.");
        }
        var plan = Plan($"Đổi tên món trong Danh mục \"{old}\" → \"{name}\"", orders, o =>
        {
            var renamed = o.Items.Where(i => i.ProductId == id && i.SnapshotName != name).Select(i => i.SnapshotName).Distinct().ToList();
            if (renamed.Count == 0)
            {
                return null;
            }
            var after = o with { Items = o.Items.Select(i => i.ProductId == id ? i with { SnapshotName = name } : i).ToList() };
            return (after, string.Join("; ", renamed.Select(n => $"\"{n}\" → \"{name}\"")) + " (giữ giá)");
        }, o => o.Items.Any(i => i.ProductId == id) ? "đã ghi tên này" : "không có món này");
        return (plan with { Rename = new BulkProductRename(id, old, name) }, null);
    }

    /// <summary>Thay bằng món khác trong danh sách: đơn chưa thanh toán / huỷ lấy giá món mới (tính lại tiền); đơn đã thanh toán
    /// giữ đơn giá + tiền đã thu, chỉ đổi món + tên.</summary>
    public static BulkPlan ReplaceItem(IEnumerable<Order> orders, BulkItemName target, Product product)
    {
        var name = product.Name?.Trim() ?? string.Empty;
        return Plan($"Thay \"{target.Name}\" bằng \"{name}\"", orders, o =>
        {
            var keepPrice = o.Status == OrderStatus.Completed;
            var after = EditItems(o, target, i =>
            {
                var price = keepPrice ? i.SnapshotPrice : product.Price ?? i.SnapshotPrice;
                return i with { ProductId = product.Id, SnapshotName = name, SnapshotPrice = price, LineTotal = price * i.Quantity };
            });
            if (after is null)
            {
                return null;
            }
            var text = $"\"{target.Name}\" → \"{name}\"" + (keepPrice ? " (đã thanh toán: giữ giá)"
                : after.Total != o.Total ? $", tổng {VnFormat.Money(o.Total)} → {VnFormat.Money(after.Total)}" : string.Empty);
            return (after, text);
        }, _ => "không có món này");
    }

    /// <summary>Đơn ghi tên món cũ (Danh mục đã đổi tên) → ghi theo tên trong Danh mục, giữ giá.</summary>
    public static BulkPlan SyncCatalogNames(IEnumerable<Order> orders, IReadOnlyDictionary<long, Product> products) =>
        Plan("Cập nhật tên món theo Danh mục", orders, o =>
        {
            string? Current(OrderItem i) => i.ProductId is { } id && products.TryGetValue(id, out var p) && p.Name?.Trim() is { Length: > 0 } n && n != i.SnapshotName ? n : null;
            var renamed = o.Items.Where(i => Current(i) is not null).ToList();
            if (renamed.Count == 0)
            {
                return null;
            }
            var after = o with { Items = o.Items.Select(i => Current(i) is { } n ? i with { SnapshotName = n } : i).ToList() };
            return (after, string.Join("; ", renamed.Select(i => $"\"{i.SnapshotName}\" → \"{Current(i)}\"")));
        }, _ => "tên món đã đúng Danh mục");

    // ---------- Hoàn tác ----------

    /// <summary>Đơn còn y như lúc vừa sửa (cả mã dòng món)? Đơn bị sửa / xoá sau đó thì Hoàn tác bỏ qua, không đè.</summary>
    public static bool Untouched(BulkChange change, Order? current) =>
        change.After is null
            ? current is null
            : current is not null && BatchUndo.Unchanged(change.After, current)
              && current.Items.Select(i => i.Id).SequenceEqual(change.After.Items.Select(i => i.Id));
}
