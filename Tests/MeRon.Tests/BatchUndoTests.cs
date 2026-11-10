using MeRon.Core.Models;
using MeRon.Core.Orders;

namespace MeRon.Tests;

/// <summary>Hoàn tác lô Nhập đơn hàng loạt chỉ xoá đơn còn nguyên như lúc lưu (2026-11-10).</summary>
public class BatchUndoTests
{
    private static Order Saved(long id, long userId = 1, long quantity = 1) =>
        new(id, userId, "Mỹ Ly", OrderStatus.Shipping, "2026-11-10", null, 0, 30000 * quantity, 30000 * quantity, string.Empty,
            [new OrderItem(id * 10, id, 15, "Giò", 30000, quantity, 30000 * quantity)]);

    [Fact]
    public void Deletes_only_orders_unchanged_since_saving()
    {
        var batch = new[]
        {
            new BatchSavedOrder(Saved(1), "706 | 1 giò"),
            new BatchSavedOrder(Saved(2), "W1 | 1 giò"),
            new BatchSavedOrder(Saved(3), "205 | 1 giò"),
            new BatchSavedOrder(Saved(4), "1101 | 1 giò"),
            new BatchSavedOrder(Saved(5), "12a09 | 1 giò"),
        };
        var now = new Dictionary<long, Order>
        {
            // Lưu lại đơn không đổi gì: dòng món có mã mới - vẫn là còn nguyên.
            [1] = Saved(1) with { Items = [new OrderItem(99, 1, 15, "Giò", 30000, 1, 30000)] },
            [2] = Saved(2, quantity: 2),                                                   // đã sửa số lượng
            [3] = Saved(3) with { Status = OrderStatus.Completed, PaymentDatetime = "2026-11-10" }, // đã thanh toán
            [5] = Saved(5, userId: 9),                                                     // sau Nhập dữ liệu: mã 5 là đơn khác
        };

        var plan = BatchUndo.Plan(batch, id => now.GetValueOrDefault(id));

        Assert.Equal([1L], plan.Delete.Select(d => d.Saved.Id));
        Assert.Equal(
            ["2: đã sửa", "3: đã chuyển sang Đã thanh toán", "4: không còn trong dữ liệu", "5: đã đổi khách / không còn là đơn này"],
            plan.Keep.Select(k => $"{k.Order.Saved.Id}: {k.Reason}"));
    }

    [Fact]
    public void Note_null_and_empty_count_as_the_same()
    {
        Assert.True(BatchUndo.Unchanged(Saved(1), Saved(1) with { Note = null }));
        Assert.False(BatchUndo.Unchanged(Saved(1), Saved(1) with { Note = "giao sớm" }));
        Assert.False(BatchUndo.Unchanged(Saved(1), Saved(1) with { DeliveryDatetime = "2026-11-11" }));
    }
}
