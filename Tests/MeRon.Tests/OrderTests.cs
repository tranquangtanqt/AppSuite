using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Core.Models;
using MeRon.Core.Orders;

namespace MeRon.Tests;

public class OrderCalculatorTests
{
    private static readonly Dictionary<long, Product> Products = new()
    {
        [1] = new Product(1, 1, "Bánh bao không nhân", null, 5000, null),
        [2] = new Product(2, 1, "Bánh bao thịt trứng", null, 12000, null), // đơn cũ đặt giá 10.000
    };

    [Fact]
    public void Line_whose_product_price_changed_is_locked_at_the_snapshot_price()
    {
        var old = new OrderLineDraft(2, 2, OriginalProductId: 2, SnapshotPrice: 10000);
        var unchanged = new OrderLineDraft(1, 1, OriginalProductId: 1, SnapshotPrice: 5000);
        var switched = old with { ProductId = 1 }; // đổi sang món khác → không còn khoá, theo giá mới
        var free = new OrderLineDraft(null, 3, IsFree: true, FreeName: "Bánh flan", FreePrice: 7000, SnapshotPrice: 6000);

        Assert.True(OrderCalculator.IsLocked(old, Products));
        Assert.Equal(20000, OrderCalculator.LineTotal(old, Products));
        Assert.False(OrderCalculator.IsLocked(unchanged, Products));
        Assert.False(OrderCalculator.IsLocked(switched, Products));
        Assert.Equal(5000, OrderCalculator.UnitPrice(switched, Products));
        Assert.False(OrderCalculator.IsLocked(free, Products));
        Assert.Equal(21000, OrderCalculator.LineTotal(free, Products));
        Assert.Equal(46000, OrderCalculator.SubTotal([old, unchanged, free], Products));
        Assert.Equal(6, OrderCalculator.TotalQuantity([old, unchanged, free]));
    }

    [Fact]
    public void Total_never_negative()
    {
        Assert.Equal(24000, OrderCalculator.Total(25000, 1000));
        Assert.Equal(0, OrderCalculator.Total(5000, 9000));
    }

    [Fact]
    public void Validation_matches_phone_messages_and_points_at_the_line()
    {
        var ok = new OrderLineDraft(1, 1);
        OrderDraft Draft(params OrderLineDraft[] lines) =>
            new(0, 1, OrderStatus.Shipping, DateTime.Today, false, null, 0, null, lines);

        Assert.Contains("khách hàng", Assert.Throws<OrderValidationException>(() => OrderCalculator.Validate(Draft(ok) with { UserId = null })).Message);
        Assert.Throws<OrderValidationException>(() => OrderCalculator.Validate(Draft()));
        var noName = Assert.Throws<OrderValidationException>(() => OrderCalculator.Validate(Draft(ok, new OrderLineDraft(null, 1, true, " ", 5000))));
        Assert.Equal("Vui lòng nhập tên cho món tự do", noName.Message);
        Assert.Equal(1, noName.LineIndex);
        Assert.Equal(OrderLineField.FreeName, noName.Field);
        var noPrice = Assert.Throws<OrderValidationException>(() => OrderCalculator.Validate(Draft(new OrderLineDraft(null, 1, true, "Flan", 0))));
        Assert.Equal(OrderLineField.FreePrice, noPrice.Field);
        OrderCalculator.Validate(Draft(ok));
    }

    [Fact]
    public void Status_on_save_follows_prepaid_but_keeps_cancelled()
    {
        var draft = new OrderDraft(5, 1, OrderStatus.Shipping, DateTime.Today, true, null, 0, null, []);
        Assert.Equal(OrderStatus.Completed, OrderCalculator.StatusOnSave(draft));
        Assert.Equal(OrderStatus.Shipping, OrderCalculator.StatusOnSave(draft with { IsPrepaid = false, OriginalStatus = OrderStatus.Completed }));
        Assert.Equal(OrderStatus.Cancelled, OrderCalculator.StatusOnSave(draft with { OriginalStatus = OrderStatus.Cancelled }));
    }

    [Fact]
    public void Date_range_presets()
    {
        var today = new DateOnly(2026, 3, 1);
        Assert.Equal((new DateOnly(2026, 2, 28), new DateOnly(2026, 2, 28)), DateRanges.Resolve(DateRangePreset.Yesterday, today));
        Assert.Equal((new DateOnly(2026, 2, 23), today), DateRanges.Resolve(DateRangePreset.Last7Days, today));
        Assert.Equal((today, new DateOnly(2026, 3, 31)), DateRanges.Resolve(DateRangePreset.ThisMonth, today));
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), DateRanges.Resolve(DateRangePreset.LastMonth, today));
        Assert.Equal(((DateOnly?)null, (DateOnly?)null), DateRanges.Resolve(DateRangePreset.All, today));
    }

    [Fact]
    public void Parses_both_phone_date_formats()
    {
        Assert.Equal(new DateTime(2026, 6, 27, 14, 20, 53, 242), DbDate.Parse("2026-06-27T14:20:53.242992")!.Value.AddTicks(-9920));
        Assert.Equal(new DateTime(2026, 6, 27, 7, 20, 53), DbDate.Parse("2026-06-27 07:20:53"));
        Assert.Equal(new DateTime(2026, 6, 27), DbDate.Parse("2026-06-27"));
        Assert.Null(DbDate.Parse("abc"));
        Assert.Equal("2026-06-27T07:20:53.000000", DbDate.Format(new DateTime(2026, 6, 27, 7, 20, 53)));
    }
}

public class OrderRepositoryTests
{
    private static (MeRonDatabase Db, OrderRepository Orders, Dictionary<long, Product> Products) Setup(TempFolder temp)
    {
        var db = temp.NewDatabase();
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Path)).Resolve()), db);
        var products = new MasterDataRepository(db).GetProducts().ToDictionary(p => p.Id);
        return (db, new OrderRepository(db), products);
    }

    private static readonly DateOnly June27 = new(2026, 6, 27);
    private static readonly DateOnly June28 = new(2026, 6, 28);

    [Fact]
    public void Filter_by_day_status_and_customer_with_mixed_date_formats()
    {
        using var temp = new TempFolder();
        var (_, orders, _) = Setup(temp);

        // đơn 1: 2026-06-27T14:20:53 (Đã thanh toán, khách 1); đơn 3: "2026-06-28 07:00:00" (Đã lên đơn, khách 2)
        Assert.Equal([1L], orders.Query(new OrderFilter(null, June27, June27)).Select(o => o.Id));
        // điện thoại so chuỗi "2026-06-28T00:00:00.000" nên bỏ sót đơn dạng có dấu cách đúng ngày Từ - ở đây phải có
        Assert.Equal([3L], orders.Query(new OrderFilter(null, June28, June28)).Select(o => o.Id));
        Assert.Equal([3L], orders.Query(new OrderFilter(OrderStatus.Shipping, June27, June28)).Select(o => o.Id));
        Assert.Equal([1L], orders.Query(new OrderFilter(null, null, null, UserId: 1)).Select(o => o.Id));

        var all = orders.Query(new OrderFilter(null, null, null));
        Assert.Equal([3L, 1L], all.Select(o => o.Id)); // mặc định ngày giao mới nhất trước
        Assert.Equal("Chị An", all[1].UserName);
        Assert.Equal(2, all[1].Items.Count);
        Assert.Single(all[0].Items, i => i.ProductId is null && i.SnapshotName == "Bánh flan"); // món tự do
    }

    [Fact]
    public void Sort_options()
    {
        using var temp = new TempFolder();
        var (_, orders, _) = Setup(temp);
        IEnumerable<long> Ids(OrderSort sort) => orders.Query(new OrderFilter(null, null, null, Sort: sort)).Select(o => o.Id);

        Assert.Equal([3L, 1L], Ids(OrderSort.TotalAscending));   // 12.000 < 24.000
        Assert.Equal([1L, 3L], Ids(OrderSort.TotalDescending));
        Assert.Equal([1L, 3L], Ids(OrderSort.DeliveryAscending));
        Assert.Equal([3L, 1L], Ids(OrderSort.DeliveryDescending));
    }

    [Fact]
    public void Create_order_with_catalog_and_free_items_computes_totals()
    {
        using var temp = new TempFolder();
        var (_, orders, products) = Setup(temp);
        var draft = new OrderDraft(0, 2, OrderStatus.Shipping, new DateTime(2026, 7, 1), false, null, 2000, "  giao trưa ",
            [new OrderLineDraft(2, 3), new OrderLineDraft(null, 1, true, " Bánh flan ", 8000)]);

        var id = orders.Save(draft, products);

        var saved = orders.Get(id)!;
        Assert.Equal(4, id); // tiếp sau id lớn nhất đã nhập (3)
        Assert.Equal(OrderStatus.Shipping, saved.Status);
        Assert.Equal(44000, saved.SubTotal);
        Assert.Equal(42000, saved.Total);
        Assert.Equal("giao trưa", saved.Note);
        Assert.Null(saved.PaymentDatetime);
        Assert.Equal("2026-07-01T00:00:00.000000", saved.DeliveryDatetime);
        Assert.Equal(("Bánh bao thịt trứng", 12000L, 36000L), (saved.Items[0].SnapshotName, saved.Items[0].SnapshotPrice, saved.Items[0].LineTotal));
        Assert.Equal(("Bánh flan", (long?)null), (saved.Items[1].SnapshotName, saved.Items[1].ProductId));
    }

    [Fact]
    public void Update_replaces_items_keeps_locked_snapshot_price_and_prepaid_sets_payment_date()
    {
        using var temp = new TempFolder();
        var (_, orders, products) = Setup(temp);
        // đơn 1: dòng 2 = món 2 đặt giá 10.000, nay món 2 giá 12.000 → khoá
        var draft = new OrderDraft(1, 1, OrderStatus.Completed, new DateTime(2026, 6, 27), true, new DateTime(2026, 6, 29), 0, null,
            [new OrderLineDraft(2, 2, OriginalProductId: 2, SnapshotPrice: 10000), new OrderLineDraft(1, 4)]);

        orders.Save(draft, products);

        var saved = orders.Get(1)!;
        Assert.Equal(2, saved.Items.Count);
        Assert.Equal(10000, saved.Items[0].SnapshotPrice);
        Assert.Equal(40000, saved.SubTotal);
        Assert.Equal(OrderStatus.Completed, saved.Status);
        Assert.Equal(new DateTime(2026, 6, 29), saved.Payment);
    }

    [Fact]
    public void Duplicate_same_day_ignores_cancelled_and_the_order_being_edited()
    {
        using var temp = new TempFolder();
        var (_, orders, _) = Setup(temp);

        Assert.True(orders.HasOtherOrderSameDay(1, June27, 0));
        Assert.False(orders.HasOtherOrderSameDay(1, June27, 1));
        Assert.False(orders.HasOtherOrderSameDay(1, June28, 0));
        orders.SetStatus(1, OrderStatus.Cancelled);
        Assert.False(orders.HasOtherOrderSameDay(1, June27, 0));
    }

    [Fact]
    public void Pay_all_refuses_when_any_already_paid_and_skips_cancelled()
    {
        using var temp = new TempFolder();
        var (_, orders, products) = Setup(temp);
        var all = orders.Query(new OrderFilter(null, null, null));

        Assert.Contains("đã thanh toán", Assert.Throws<MeRonValidationException>(() => orders.PayAll(all)).Message);

        var cancelled = orders.Save(new OrderDraft(0, 1, OrderStatus.Shipping, new DateTime(2026, 6, 28), false, null, 0, null,
            [new OrderLineDraft(1, 1)]), products);
        orders.SetStatus(cancelled, OrderStatus.Cancelled);
        var unpaid = orders.Query(new OrderFilter(null, June28, June28));
        Assert.Equal(1, orders.PayAll(unpaid));
        Assert.Equal(OrderStatus.Completed, orders.Get(3)!.Status);
        Assert.Equal(OrderStatus.Cancelled, orders.Get(cancelled)!.Status);
    }

    [Fact]
    public void Delete_removes_order_and_its_items()
    {
        using var temp = new TempFolder();
        var (db, orders, _) = Setup(temp);

        orders.Delete(1);

        Assert.Null(orders.Get(1));
        Assert.Equal(1, db.Count("OrderItems")); // còn dòng của đơn 3
    }
}

public class OrderCopierTests
{
    private static readonly Dictionary<long, Product> Products = new()
    {
        [1] = new Product(1, 1, "Bánh bao không nhân", null, 5000, null),
        [2] = new Product(2, 1, "Bánh bao thịt trứng", null, 12000, null), // đơn gốc đặt giá 10.000
    };

    /// <summary>Đơn gốc đã thanh toán: món giữ giá, món đã đổi giá, món tự do, món không còn trong danh sách (id 9).</summary>
    private static readonly Order Paid = new(7, 42, "908 Chị Huyền", OrderStatus.Completed,
        "2026-06-27T14:20:53.242992", "2026-06-28T09:00:00.000", 1000, 49000, 48000, "Giao cổng sau",
        [
            new OrderItem(1, 7, 1, "Bánh bao không nhân", 5000, 2, 10000),
            new OrderItem(2, 7, 2, "Bánh bao thịt trứng", 10000, 1, 10000),
            new OrderItem(3, 7, null, "Bánh flan", 7000, 3, 21000),
            new OrderItem(4, 7, 9, "Chè bưởi", 8000, 1, 8000),
        ]);

    [Fact]
    public void Copy_is_a_new_unpaid_order_keeping_the_original_delivery()
    {
        var draft = OrderCopier.CopyAsNew(Paid, new DateOnly(2026, 10, 13), Products).Draft;

        Assert.Equal(0, draft.Id);
        Assert.Equal(42, draft.UserId);
        Assert.Equal(OrderStatus.Shipping, draft.OriginalStatus);
        Assert.Equal(OrderStatus.Shipping, OrderCalculator.StatusOnSave(draft));
        Assert.False(draft.IsPrepaid);
        Assert.Null(draft.Payment);
        Assert.Equal(new DateTime(2026, 6, 27, 14, 20, 53, 242).AddTicks(9920), draft.Delivery);
        Assert.Equal(1000, draft.Discount);
        Assert.Equal("Giao cổng sau", draft.Note);
    }

    [Fact]
    public void Copy_uses_current_prices_and_keeps_free_and_missing_items_as_free_lines()
    {
        var copy = OrderCopier.CopyAsNew(Paid, new DateOnly(2026, 10, 13), Products);
        var lines = copy.Draft.Lines;

        Assert.Equal(4, lines.Count);
        Assert.Equal(new OrderLineDraft(1, 2), lines[0]);
        Assert.Equal(new OrderLineDraft(2, 1), lines[1]);
        Assert.False(OrderCalculator.IsLocked(lines[1], Products)); // không khoá giá cũ
        Assert.Equal(12000, OrderCalculator.UnitPrice(lines[1], Products));
        Assert.Equal(new OrderLineDraft(null, 3, true, "Bánh flan", 7000), lines[2]);
        Assert.Equal(new OrderLineDraft(null, 1, true, "Chè bưởi", 8000), lines[3]);
        Assert.Equal(10000 + 12000 + 21000 + 8000, OrderCalculator.SubTotal(lines, Products));
        OrderCalculator.Validate(copy.Draft);

        Assert.Equal(2, copy.Notices.Count);
        Assert.Contains("Bánh bao thịt trứng", copy.Notices[0]);
        Assert.Contains("12.000", copy.Notices[0]);
        Assert.Contains("Chè bưởi", copy.Notices[1]);
    }

    [Fact]
    public void Copy_of_order_without_delivery_or_changes_is_delivered_on_the_fallback_day_with_no_notices()
    {
        var plain = Paid with { DeliveryDatetime = null, Items = [new OrderItem(1, 7, 1, "Bánh bao không nhân", 5000, 4, 20000)] };
        var copy = OrderCopier.CopyAsNew(plain, new DateOnly(2026, 10, 13), Products);

        Assert.Empty(copy.Notices);
        Assert.Equal(new DateTime(2026, 10, 13), copy.Draft.Delivery);
    }
}
