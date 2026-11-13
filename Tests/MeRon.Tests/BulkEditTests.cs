using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Core.Models;
using MeRon.Core.Orders;

namespace MeRon.Tests;

/// <summary>Sửa hàng loạt đơn đã lưu (trang Đặt hàng, Chọn nhiều - 2026-11-13).</summary>
public class BulkEditTests
{
    private static readonly Product Gio = new(15, 1, "Giò", null, 30000, null);
    private static readonly Product Cha = new(16, 1, "Chả quế", null, 40000, null);

    private static Order Make(long id, OrderStatus status = OrderStatus.Shipping, string delivery = "2026-11-11T08:30:00.000", string? payment = null,
        long discount = 0, params OrderItem[] items)
    {
        items = items.Length > 0 ? items : [new OrderItem(id * 10, id, 15, "Giò", 30000, 2, 60000)];
        var sub = items.Sum(i => i.LineTotal);
        return new Order(id, 1, "Mỹ Ly", status, delivery, payment, discount, sub, sub - discount, string.Empty, items);
    }

    [Fact]
    public void Delivery_day_keeps_the_time_skips_same_day_and_warns_paid_orders()
    {
        var plan = BulkEdit.DeliveryDay([Make(1), Make(2, OrderStatus.Completed, payment: "2026-11-11T09:00:00.000"), Make(3, delivery: "2026-11-12 07:00:00")],
            new DateOnly(2026, 11, 12));

        Assert.Equal([1L, 2L], plan.Changes.Select(c => c.Before.Id));
        Assert.Equal(new DateTime(2026, 11, 12, 8, 30, 0), plan.Changes[0].After!.Delivery);
        Assert.Equal([false, true], plan.Changes.Select(c => c.Warn));
        Assert.Equal(1, plan.WarnCount);
        Assert.Equal("3: đã giao ngày này", plan.Skipped.Select(s => $"{s.Order.Id}: {s.Reason}").Single());
        Assert.Equal("Ngày giao 11/11/2026 → 12/11/2026", plan.Changes[0].Description);
    }

    [Fact]
    public void Payment_day_pays_unpaid_orders_and_moves_the_date_of_paid_ones()
    {
        var plan = BulkEdit.Payment(
            [Make(1), Make(2, OrderStatus.Completed, payment: "2026-11-11T09:15:00.000"), Make(3, OrderStatus.Cancelled),
             Make(4, OrderStatus.Completed, payment: "2026-11-10T10:00:00.000")],
            new DateOnly(2026, 11, 10), new TimeOnly(18, 0));

        var paid = plan.Changes.Single(c => c.Before.Id == 1).After!;
        Assert.Equal((OrderStatus.Completed, new DateTime(2026, 11, 10, 18, 0, 0)), (paid.Status, paid.Payment));
        Assert.Equal(new DateTime(2026, 11, 10, 9, 15, 0), plan.Changes.Single(c => c.Before.Id == 2).After!.Payment); // giữ giờ cũ
        Assert.Equal(["3: đơn huỷ", "4: đã thanh toán ngày này"], plan.Skipped.Select(s => $"{s.Order.Id}: {s.Reason}"));
    }

    [Fact]
    public void Unpay_cancel_restore_and_delete()
    {
        Order[] orders = [Make(1), Make(2, OrderStatus.Completed, payment: "2026-11-11T09:00:00.000"), Make(3, OrderStatus.Cancelled)];

        var unpay = BulkEdit.Unpay(orders).Changes.Single();
        Assert.Equal((2L, OrderStatus.Shipping, (string?)null), (unpay.Before.Id, unpay.After!.Status, unpay.After.PaymentDatetime));

        var cancel = BulkEdit.Cancel(orders);
        Assert.Equal([1L, 2L], cancel.Changes.Select(c => c.Before.Id));
        Assert.True(cancel.Changes.All(c => c.After!.Status == OrderStatus.Cancelled));

        // Huỷ đơn đã thanh toán rồi khôi phục: về đúng Đã thanh toán (ngày thanh toán còn giữ).
        var restored = BulkEdit.Restore(cancel.Changes.Select(c => c.After!)).Changes.Select(c => c.After!.Status);
        Assert.Equal([OrderStatus.Shipping, OrderStatus.Completed], restored);
        Assert.Equal(2, BulkEdit.Restore(orders).Skipped.Count);

        var delete = BulkEdit.Delete(orders);
        Assert.Equal(3, delete.Changes.Count);
        Assert.All(delete.Changes, c => Assert.Null(c.After));
    }

    [Fact]
    public void Item_names_group_by_product_and_written_name_and_flag_old_catalog_names()
    {
        var products = new Dictionary<long, Product> { [15] = Gio with { Name = "Giò lụa" } };
        Order[] orders =
        [
            Make(1),
            Make(2, items: [new OrderItem(21, 2, 15, "giò ", 30000, 1, 30000), new OrderItem(22, 2, null, "cá khoay", 50000, 1, 50000)]),
        ];

        var names = BulkEdit.ItemNames(orders, products);

        Assert.Equal(["Giò - 2 đơn · Danh mục đã đổi tên: Giò lụa", "cá khoay (món tự do) - 1 đơn"], names.Select(n => n.Label));
    }

    [Fact]
    public void Rename_keeps_price_replace_recalculates_unpaid_but_keeps_paid_money()
    {
        var unpaid = Make(1, discount: 5000, items: [new OrderItem(10, 1, 15, "Giò", 30000, 2, 60000), new OrderItem(11, 1, null, "flan", 10000, 1, 10000)]);
        var paid = Make(2, OrderStatus.Completed, payment: "2026-11-11T09:00:00.000");
        var gio = BulkEdit.ItemNames([unpaid, paid], new Dictionary<long, Product>()).Single(n => n.ProductId == 15);

        var rename = BulkEdit.RenameItem([unpaid, paid], gio, " Giò lụa ");
        Assert.All(rename.Changes, c => Assert.Equal(("Giò lụa", c.Before.Total), (c.After!.Items[0].SnapshotName!, c.After.Total)));

        var replace = BulkEdit.ReplaceItem([unpaid, paid, Make(3, items: [new OrderItem(30, 3, null, "flan", 10000, 1, 10000)])], gio, Cha);
        var a = replace.Changes[0].After!;
        Assert.Equal((16L, "Chả quế", 40000L, 80000L), (a.Items[0].ProductId!.Value, a.Items[0].SnapshotName!, a.Items[0].SnapshotPrice, a.Items[0].LineTotal));
        Assert.Equal((90000L, 85000L), (a.SubTotal, a.Total)); // 80.000 + 10.000, giảm 5.000
        Assert.Equal(unpaid.Items[1], a.Items[1]); // dòng khác giữ nguyên

        var p = replace.Changes[1];
        Assert.True(p.Warn);
        Assert.Equal((16L, "Chả quế", 30000L, 60000L), (p.After!.Items[0].ProductId!.Value, p.After.Items[0].SnapshotName!, p.After.Items[0].SnapshotPrice, p.After.Total));
        Assert.Contains("giữ giá", p.Description);
        Assert.Equal("3: không có món này", replace.Skipped.Select(s => $"{s.Order.Id}: {s.Reason}").Single());
    }

    [Fact]
    public void Rename_product_renames_the_catalog_and_every_line_of_it_on_selected_orders()
    {
        var products = new Dictionary<long, Product> { [15] = Gio, [16] = Cha };
        var oldName = Make(1, items: [new OrderItem(10, 1, 15, "Gio cu", 30000, 1, 30000), new OrderItem(11, 1, 15, "Giò", 25000, 1, 25000)]);
        var paid = Make(2, OrderStatus.Completed, payment: "2026-11-11T09:00:00.000");
        var noGio = Make(3, items: [new OrderItem(30, 3, 16, "Chả quế", 40000, 1, 40000)]);
        var gio = BulkEdit.ItemNames([oldName, paid], products).First(n => n.ProductId == 15);

        var (plan, hint) = BulkEdit.RenameProduct([oldName, paid, noGio], gio, " Giò lụa ", products);

        Assert.Null(hint);
        Assert.Equal(new BulkProductRename(15, "Giò", "Giò lụa"), plan!.Rename);
        Assert.Equal([1L, 2L], plan.Changes.Select(c => c.Before.Id));
        var a = plan.Changes[0].After!;
        Assert.Equal(["Giò lụa", "Giò lụa"], a.Items.Select(i => i.SnapshotName)); // mọi dòng của món, kể cả tên cũ khác
        Assert.Equal([30000L, 25000L], a.Items.Select(i => i.SnapshotPrice)); // giữ giá
        Assert.Equal(oldName.Total, a.Total);
        Assert.True(plan.Changes[1].Warn);
        Assert.Equal("3: không có món này", plan.Skipped.Select(s => $"{s.Order.Id}: {s.Reason}").Single());

        // Đơn đã ghi đúng tên mới: bỏ qua, Danh mục vẫn đổi.
        var (already, _) = BulkEdit.RenameProduct([a], gio, "Giò lụa", products);
        Assert.Empty(already!.Changes);
        Assert.NotNull(already.Rename);

        Assert.Equal("Tên mới giống tên trong Danh mục.", BulkEdit.RenameProduct([oldName], gio, "Giò", products).Hint);
        Assert.Contains("Thay bằng món khác", BulkEdit.RenameProduct([oldName], gio, "chả QUẾ", products).Hint);
        Assert.Null(BulkEdit.RenameProduct([oldName], gio, " ", products).Plan);
        var free = new BulkItemName(null, "flan", 1, null);
        Assert.Null(BulkEdit.RenameProduct([oldName], free, "Flan", products).Plan);
    }

    [Fact]
    public void Sync_catalog_names_only_touches_renamed_products()
    {
        var products = new Dictionary<long, Product> { [15] = Gio with { Name = "Giò lụa" }, [16] = Cha };
        var order = Make(1, items: [new OrderItem(10, 1, 15, "Giò", 30000, 1, 30000), new OrderItem(11, 1, 16, "Chả quế", 40000, 1, 40000)]);

        var plan = BulkEdit.SyncCatalogNames([order, Make(2, items: [new OrderItem(20, 2, 16, "Chả quế", 40000, 1, 40000)])], products);

        var change = plan.Changes.Single();
        Assert.Equal(["Giò lụa", "Chả quế"], change.After!.Items.Select(i => i.SnapshotName));
        Assert.Equal(70000, change.After.Total);
        Assert.Equal("2: tên món đã đúng Danh mục", plan.Skipped.Select(s => $"{s.Order.Id}: {s.Reason}").Single());
    }

    [Fact]
    public void Untouched_means_unchanged_since_the_bulk_edit()
    {
        var change = BulkEdit.Cancel([Make(1)]).Changes.Single();
        Assert.True(BulkEdit.Untouched(change, change.After));
        Assert.False(BulkEdit.Untouched(change, change.After! with { Status = OrderStatus.Shipping }));
        Assert.False(BulkEdit.Untouched(change, null));
        Assert.False(BulkEdit.Untouched(change, change.After! with { Items = [change.After.Items[0] with { Id = 99 }] })); // lưu lại đơn: dòng món mã mới

        var deleted = BulkEdit.Delete([Make(2)]).Changes.Single();
        Assert.True(BulkEdit.Untouched(deleted, null));
        Assert.False(BulkEdit.Untouched(deleted, Make(2)));
    }

    [Fact]
    public void Apply_and_revert_on_the_database_restore_every_order_exactly()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Path)).Resolve()), db);
        var repo = new OrderRepository(db);
        var products = new MasterDataRepository(db).GetProducts().ToDictionary(p => p.Id);
        var before = repo.Query(new OrderFilter(null, null, null));
        Assert.Equal(2, before.Count);

        // Đơn 1: đổi ngày giao; đơn 3: xoá; rồi thay món trên đơn 1 (lô 2).
        var moved = BulkEdit.DeliveryDay([before[1]], new DateOnly(2026, 7, 1)).Changes;
        var deleted = BulkEdit.Delete([before[0]]).Changes;
        Assert.Equal(2, repo.ApplyBulk([.. moved, .. deleted]));
        Assert.Null(repo.Get(before[0].Id));
        var afterMove = repo.Get(before[1].Id)!;
        Assert.Equal(new DateOnly(2026, 7, 1), DateOnly.FromDateTime(afterMove.Delivery!.Value));
        Assert.True(BulkEdit.Untouched(moved[0], afterMove));

        var target = BulkEdit.ItemNames([afterMove], products).First(n => n.ProductId is not null);
        var other = products.Values.First(p => p.Id != target.ProductId);
        var replaced = BulkEdit.ReplaceItem([afterMove], target, other).Changes;
        repo.ApplyBulk(replaced);
        var afterReplace = repo.Get(before[1].Id)!;
        Assert.True(BulkEdit.Untouched(replaced[0], afterReplace));
        Assert.Contains(afterReplace.Items, i => i.ProductId == other.Id);

        // Lô 3: đổi tên món trong Danh mục (đơn + Danh mục cùng giao dịch), rồi hoàn tác.
        var master = new MasterDataRepository(db);
        var renameTarget = BulkEdit.ItemNames([afterReplace], products).First(n => n.ProductId == other.Id);
        var renamed = BulkEdit.RenameProduct([afterReplace], renameTarget, "Tên mới thử", products).Plan!;
        repo.ApplyBulk(renamed.Changes, renamed.Rename);
        Assert.Equal("Tên mới thử", master.GetProducts().Single(p => p.Id == other.Id).Name);
        Assert.Contains(repo.Get(before[1].Id)!.Items, i => i.SnapshotName == "Tên mới thử");
        repo.RevertBulk(renamed.Changes, renamed.Rename);
        Assert.Equal(other.Name, master.GetProducts().Single(p => p.Id == other.Id).Name);

        repo.RevertBulk(replaced);
        repo.RevertBulk([.. moved, .. deleted]);
        var now = repo.Query(new OrderFilter(null, null, null));
        Assert.Equal(before.Select(o => o.Id), now.Select(o => o.Id));
        foreach (var (b, n) in before.Zip(now))
        {
            Assert.True(BatchUndo.Unchanged(b, n), $"đơn {b.Id} khác sau Hoàn tác");
            Assert.Equal(b.Items.Select(i => i.Id), n.Items.Select(i => i.Id));
        }
    }
}
