using System.Globalization;
using MeRon.Core.Models;

namespace MeRon.Core.History;

/// <summary>Chữ mô tả 1 lần thay đổi đơn cho trang Lịch sử chỉnh sửa và hộp hỏi khôi phục.</summary>
public static class HistoryText
{
    /// <summary>Tóm tắt đơn: "2 Bánh bao, 1 Xôi · 125.000 đ · Đã lên đơn".</summary>
    public static string Summary(Order order) =>
        string.Join(", ", order.Items.Select(i => $"{i.Quantity} {i.SnapshotName}"))
        + (order.Items.Count == 0 ? "(không có món)" : string.Empty)
        + $" · {VnFormat.Money(order.Total)} · {OrderStatusText.Label(order.Status)}";

    /// <summary>
    /// Những gì khác nhau từ <paramref name="before"/> sang <paramref name="after"/>, mỗi ý 1 dòng: "Trạng thái: Đã lên đơn →
    /// Đã thanh toán", "Bánh bao: 3 → 5", "+ Xôi × 2", "− Trà × 1", "Tổng: 75.000 đ → 125.000 đ"... Thêm / xoá đơn thì 1 dòng
    /// tóm tắt đơn.
    /// </summary>
    public static IReadOnlyList<string> Changes(Order? before, Order? after)
    {
        if (before is null || after is null)
        {
            return after is not null ? ["Đơn mới: " + Summary(after)]
                : before is not null ? ["Đơn bị xoá: " + Summary(before)]
                : [];
        }

        var lines = new List<string>();
        if (before.UserId != after.UserId)
        {
            lines.Add($"Khách: {Name(before.UserName)} → {Name(after.UserName)}");
        }
        if (before.Status != after.Status)
        {
            lines.Add($"Trạng thái: {OrderStatusText.Label(before.Status)} → {OrderStatusText.Label(after.Status)}");
        }
        if (before.DeliveryDatetime != after.DeliveryDatetime)
        {
            lines.Add($"Ngày giao: {When(before.Delivery, after.Delivery)} → {When(after.Delivery, before.Delivery)}");
        }
        if (before.PaymentDatetime != after.PaymentDatetime && (before.Payment is not null || after.Payment is not null))
        {
            lines.Add($"Ngày thanh toán: {When(before.Payment, after.Payment)} → {When(after.Payment, before.Payment)}");
        }
        lines.AddRange(ItemChanges(before.Items, after.Items));
        if (before.DiscountValue != after.DiscountValue)
        {
            lines.Add($"Giảm giá: {VnFormat.Money(before.DiscountValue)} → {VnFormat.Money(after.DiscountValue)}");
        }
        if ((before.Note ?? string.Empty).Trim() != (after.Note ?? string.Empty).Trim())
        {
            lines.Add($"Ghi chú: {Quote(before.Note)} → {Quote(after.Note)}");
        }
        if (before.Total != after.Total)
        {
            lines.Add($"Tổng: {VnFormat.Money(before.Total)} → {VnFormat.Money(after.Total)}");
        }
        return lines;
    }

    /// <summary>Món so theo mã món (món tự do theo tên): đổi số lượng / giá / tên, thêm (+), bớt (−).</summary>
    private static IEnumerable<string> ItemChanges(IReadOnlyList<OrderItem> before, IReadOnlyList<OrderItem> after)
    {
        var old = Group(before);
        var now = Group(after);
        foreach (var (key, a) in old)
        {
            if (!now.TryGetValue(key, out var b))
            {
                yield return $"− {a.Name} × {a.Quantity}";
                continue;
            }
            if (a.Name != b.Name)
            {
                yield return $"Tên món: {a.Name} → {b.Name}";
            }
            if (a.Quantity != b.Quantity)
            {
                yield return $"{b.Name}: {a.Quantity} → {b.Quantity}";
            }
            if (a.Price != b.Price)
            {
                yield return $"{b.Name}: giá {VnFormat.Money(a.Price)} → {VnFormat.Money(b.Price)}";
            }
        }
        foreach (var (key, b) in now.Where(n => !old.ContainsKey(n.Key)))
        {
            yield return $"+ {b.Name} × {b.Quantity}";
        }
    }

    private static Dictionary<string, (string Name, long Quantity, long Price)> Group(IReadOnlyList<OrderItem> items) =>
        items.GroupBy(i => i.ProductId is { } id ? "#" + id : "~" + TextSearch.Fold(i.SnapshotName).Trim())
            .ToDictionary(g => g.Key, g => (g.First().SnapshotName?.Trim() ?? "(món)", g.Sum(i => i.Quantity), g.First().SnapshotPrice));

    /// <summary>Ngày (dd/MM/yyyy); kèm giờ khi cùng ngày với bên kia (chỉ đổi giờ thì mới thấy khác).</summary>
    private static string When(DateTime? value, DateTime? other) => value is not { } v ? "(trống)"
        : other is { } o && o.Date == v.Date ? v.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
        : DbDate.Show(v);

    private static string Name(string? name) => string.IsNullOrWhiteSpace(name) ? "(không có khách)" : name.Trim();

    private static string Quote(string? note) => string.IsNullOrWhiteSpace(note) ? "(trống)" : $"\"{note.Trim()}\"";
}

/// <summary>Khôi phục 1 dòng lịch sử: đưa đơn về đúng như TRƯỚC lần thay đổi đó.</summary>
/// <param name="Current">Đơn đang có với mã đó (null = không còn).</param>
/// <param name="AsNew">Mã đơn đang bị đơn khác dùng → khôi phục thành đơn mới (mã mới).</param>
/// <param name="Blocked">Lý do không khôi phục được (null = được).</param>
public sealed record RestorePlan(HistoryEntry Entry, Order? Current, bool AsNew, string? Blocked, IReadOnlyList<string> Warnings)
{
    public Order? Target => Entry.Before;

    /// <summary>Những gì sẽ đổi so với đơn đang có.</summary>
    public IReadOnlyList<string> Changes => HistoryText.Changes(AsNew ? null : Current, Target);

    /// <summary>
    /// Lập kế hoạch: đơn thêm mới thì không khôi phục (muốn bỏ thì mở đơn rồi Xoá); đơn đang giống hệt lúc trước thì thôi.
    /// Cảnh báo khi đơn đã bị sửa / xoá tiếp sau lần đó, mã đơn đang là đơn khác (vd đã nhập lại backup), khách không còn.
    /// </summary>
    public static RestorePlan For(HistoryEntry entry, Order? current, Func<long, bool> customerExists)
    {
        if (entry.Before is not { } target)
        {
            return new(entry, current, false, "Đây là lần thêm đơn mới - muốn bỏ đơn thì mở đơn rồi Xoá đơn.", []);
        }
        if (OrderHistory.SameContent(current, target))
        {
            return new(entry, current, false, "Đơn đang giống hệt lúc trước lần thay đổi này - không có gì để khôi phục.", []);
        }

        var warnings = new List<string>();
        var asNew = false;
        if (entry.After is null && current is not null)
        {
            asNew = true;
            warnings.Add($"Mã đơn {entry.OrderId} đang là 1 đơn khác (có thể đã nhập lại backup, hoặc đơn đã được khôi phục rồi sửa tiếp) - đơn sẽ được thêm lại thành đơn mới.");
        }
        else if (entry.After is not null && current is null)
        {
            warnings.Add("Đơn đã bị xoá sau lần này - khôi phục sẽ thêm lại đơn.");
        }
        else if (entry.After is not null && !OrderHistory.SameContent(current, entry.After))
        {
            warnings.Add("Đơn đã được sửa tiếp sau lần này - khôi phục sẽ bỏ luôn những lần sửa sau đó.");
        }
        if (target.UserId is { } userId && !customerExists(userId))
        {
            warnings.Add($"Khách \"{target.UserName}\" không còn trong danh sách khách hàng - đơn khôi phục sẽ không có tên khách.");
        }
        return new(entry, current, asNew, null, warnings);
    }
}
