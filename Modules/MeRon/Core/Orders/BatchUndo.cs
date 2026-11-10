using MeRon.Core.Models;

namespace MeRon.Core.Orders;

/// <summary>1 đơn của lô Nhập đơn hàng loạt vừa lưu: đơn như lúc lưu + dòng chữ gốc (trả về ô chữ khi Hoàn tác).</summary>
public sealed record BatchSavedOrder(Order Saved, string Raw);

/// <summary>Đơn Hoàn tác không xoá + lý do (đã sửa, đã thanh toán, đã huỷ, không còn).</summary>
public sealed record BatchKeptOrder(BatchSavedOrder Order, string Reason);

/// <summary>Kết quả xét lô trước khi Hoàn tác: <see cref="Delete"/> = đơn còn nguyên như lúc lưu (xoá + trả dòng về ô chữ);
/// <see cref="Keep"/> = đơn đã đổi sau khi lưu hoặc không còn - không xoá, không trả dòng.</summary>
public sealed record BatchUndoPlan(IReadOnlyList<BatchSavedOrder> Delete, IReadOnlyList<BatchKeptOrder> Keep);

/// <summary>
/// Hoàn tác lô vừa lưu chỉ xoá đơn chưa ai động tới: đơn đã sửa / thanh toán / huỷ sau khi lưu là dữ liệu người dùng tự làm,
/// xoá đi là mất. Cùng mã đơn mà khác nội dung (vd sau Nhập dữ liệu, mã đơn được đặt lại) cũng không xoá.
/// </summary>
public static class BatchUndo
{
    public static BatchUndoPlan Plan(IReadOnlyList<BatchSavedOrder> batch, Func<long, Order?> current)
    {
        var delete = new List<BatchSavedOrder>();
        var keep = new List<BatchKeptOrder>();
        foreach (var item in batch)
        {
            var now = current(item.Saved.Id);
            if (now is null)
            {
                keep.Add(new BatchKeptOrder(item, "không còn trong dữ liệu"));
            }
            else if (!Unchanged(item.Saved, now))
            {
                keep.Add(new BatchKeptOrder(item, now.Status != item.Saved.Status
                    ? $"đã chuyển sang {OrderStatusText.Label(now.Status)}"
                    : now.UserId != item.Saved.UserId ? "đã đổi khách / không còn là đơn này" : "đã sửa"));
            }
            else
            {
                delete.Add(item);
            }
        }
        return new BatchUndoPlan(delete, keep);
    }

    /// <summary>Đơn y như lúc lưu: khách, trạng thái, ngày giao / thanh toán, giảm giá, tiền, ghi chú, các dòng món (không
    /// so mã dòng món - lưu lại đơn thì dòng món được chèn lại mã mới).</summary>
    public static bool Unchanged(Order saved, Order now) =>
        saved.UserId == now.UserId && saved.Status == now.Status && saved.DeliveryDatetime == now.DeliveryDatetime
        && saved.PaymentDatetime == now.PaymentDatetime && saved.DiscountValue == now.DiscountValue && saved.SubTotal == now.SubTotal
        && saved.Total == now.Total && (saved.Note ?? string.Empty) == (now.Note ?? string.Empty)
        && saved.Items.Select(Line).SequenceEqual(now.Items.Select(Line));

    private static (long?, string?, long, long, long) Line(OrderItem i) => (i.ProductId, i.SnapshotName, i.SnapshotPrice, i.Quantity, i.LineTotal);
}
