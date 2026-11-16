using MeRon.Core.Models;

namespace MeRon.Core.Orders;

/// <summary>Ghi chú đơn tách thành dấu "Cần xác nhận" (+ lý do) và phần ghi chú còn lại.</summary>
public sealed record ConfirmNote(bool Marked, string Reason, string Rest);

/// <summary>
/// Đơn tạm, cần hỏi lại (2026-11-16): chưa rõ khách, chưa rõ giá, hoặc tự đánh dấu. Không thêm cột dữ liệu (giữ tương thích
/// app điện thoại) - dấu nằm ở đầu ô Ghi chú: <c>[Cần xác nhận: lý do] ghi chú cũ</c>. Ngoài dấu đó, đơn tự tính là cần xác
/// nhận khi có món đơn giá ≤ <see cref="UnknownPriceMax"/> (nhập tạm 1đ / 0đ) hoặc khách là khách chung
/// <see cref="UnknownCustomerName"/>. Đơn Huỷ không tính.
/// </summary>
public static class OrderConfirm
{
    /// <summary>Khách chung cho mọi đơn chưa rõ người (app tự tạo 1 lần, xem MasterDataRepository.EnsureCustomer).</summary>
    public const string UnknownCustomerName = "Chưa rõ khách";

    /// <summary>Đơn giá ≤ mốc này = chưa có giá (giá tạm).</summary>
    public const long UnknownPriceMax = 1;

    private const string Tag = "Cần xác nhận";

    /// <summary>Tiền tố lý do khi đơn ghi vào khách chung - đổi sang khách thật thì dấu tự bỏ.</summary>
    public const string UnknownCustomerReason = "Chưa rõ khách";

    public static ConfirmNote Parse(string? note)
    {
        var text = note?.Trim() ?? string.Empty;
        if (!text.StartsWith("[" + Tag, StringComparison.Ordinal))
        {
            return new ConfirmNote(false, string.Empty, text);
        }
        var close = text.IndexOf(']');
        if (close < 0)
        {
            return new ConfirmNote(false, string.Empty, text);
        }
        var inside = text[(Tag.Length + 1)..close].Trim();
        var reason = inside.StartsWith(':') ? inside[1..].Trim() : inside;
        return new ConfirmNote(true, reason, text[(close + 1)..].Trim());
    }

    /// <summary>Ghép lại ô Ghi chú để lưu: có dấu thì <c>[Cần xác nhận: lý do] phần còn lại</c>; không thì chỉ phần còn lại.</summary>
    public static string Compose(bool marked, string? reason, string? rest)
    {
        var body = rest?.Trim() ?? string.Empty;
        if (!marked)
        {
            return body;
        }
        // "]" trong lý do sẽ cắt sai lúc đọc lại → đổi thành ")".
        var why = (reason ?? string.Empty).Replace(']', ')').Replace('\r', ' ').Replace('\n', ' ').Trim();
        var head = why.Length == 0 ? $"[{Tag}]" : $"[{Tag}: {why}]";
        return body.Length == 0 ? head : head + " " + body;
    }

    /// <summary>Tên các món đơn giá ≤ 1đ (chưa có giá).</summary>
    public static IReadOnlyList<string> UnknownPriceItems(Order order) =>
        order.Items.Where(i => i.SnapshotPrice <= UnknownPriceMax).Select(i => i.SnapshotName?.Trim() is { Length: > 0 } n ? n : "(món không tên)")
            .Distinct().ToList();

    public static bool IsUnknownCustomer(string? name) =>
        string.Equals(name?.Trim(), UnknownCustomerName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Các lý do cần xác nhận của đơn (rỗng = không cần). Đơn Huỷ → rỗng.</summary>
    public static IReadOnlyList<string> Reasons(Order order)
    {
        if (order.Status == OrderStatus.Cancelled)
        {
            return [];
        }
        var reasons = new List<string>();
        var note = Parse(order.Note);
        if (note.Marked)
        {
            reasons.Add(note.Reason.Length > 0 ? note.Reason : "Cần xác nhận");
        }
        if (IsUnknownCustomer(order.UserName) && !reasons.Any(r => r.StartsWith(UnknownCustomerReason, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add(UnknownCustomerReason);
        }
        if (UnknownPriceItems(order) is { Count: > 0 } items)
        {
            reasons.Add("Chưa có giá: " + string.Join(", ", items));
        }
        return reasons;
    }

    public static bool Needs(Order order) => Reasons(order).Count > 0;

    /// <summary>Chữ khách đã ghi lúc đơn vào khách chung ("Chưa rõ khách: Nhung?" → "Nhung") - gợi ý ở Chọn khách đúng.</summary>
    public static string CustomerHint(string? note)
    {
        var reason = Parse(note).Reason;
        if (!IsOnlyUnknownCustomer(reason))
        {
            return string.Empty;
        }
        var colon = reason.IndexOf(':');
        return colon < 0 ? string.Empty : reason[(colon + 1)..].Replace("?", string.Empty).Trim();
    }

    /// <summary>Đổi từ khách chung sang khách thật: lý do chỉ là "Chưa rõ khách…" thì bỏ dấu; lý do khác thì giữ.</summary>
    public static bool IsOnlyUnknownCustomer(string reason) =>
        reason.StartsWith(UnknownCustomerReason, StringComparison.OrdinalIgnoreCase);
}
