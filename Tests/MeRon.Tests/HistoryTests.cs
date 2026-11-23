using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Core.History;
using MeRon.Core.Import;
using MeRon.Core.Models;
using MeRon.Core.Orders;

namespace MeRon.Tests;

/// <summary>Lịch sử chỉnh sửa đơn + khôi phục (trang Lịch sử chỉnh sửa - 2026-11-16).</summary>
public class HistoryTests
{
    private static (OrderRepository Orders, OrderHistory History, Dictionary<long, Product> Products) Setup(TempFolder temp)
    {
        var db = temp.NewDatabase();
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Path)).Resolve()), db);
        var history = new OrderHistory(Path.Combine(db.Folder, OrderHistory.FileName));
        var errors = new List<Exception>();
        var orders = new OrderRepository(db, history) { HistoryError = errors.Add };
        return (orders, history, new MasterDataRepository(db).GetProducts().ToDictionary(p => p.Id));
    }

    private static RestorePlan Plan(OrderRepository orders, HistoryEntry entry) =>
        RestorePlan.For(entry, orders.Get(entry.OrderId), orders.CustomerExists);

    [Fact]
    public void Cancel_then_delete_are_recorded_and_restore_brings_the_order_back_step_by_step()
    {
        using var temp = new TempFolder();
        var (orders, history, _) = Setup(temp);
        var original = orders.Get(3)!;

        orders.SetStatus(3, OrderStatus.Cancelled);
        orders.Delete(3);

        var entries = history.Query();
        Assert.Equal(["Xoá đơn", "Huỷ đơn"], entries.Select(e => e.Action));
        Assert.Equal([HistoryKind.Delete, HistoryKind.Edit], entries.Select(e => e.Kind));
        Assert.Null(orders.Get(3));

        // Khôi phục dòng Xoá: đơn quay lại đúng mã, đúng trạng thái lúc trước khi xoá (Huỷ), đủ dòng món (cả món tự do).
        var undelete = Plan(orders, entries[0]);
        Assert.Null(undelete.Blocked);
        Assert.Empty(undelete.Warnings);
        Assert.False(undelete.AsNew);
        Assert.Equal(1, orders.Restore([undelete]));
        var back = orders.Get(3)!;
        Assert.Equal(OrderStatus.Cancelled, back.Status);
        Assert.True(OrderHistory.SameContent(back, original with { Status = OrderStatus.Cancelled }));
        Assert.Equal(original.Items.Select(i => i.Id), back.Items.Select(i => i.Id)); // mã dòng món còn trống → giữ mã cũ

        // Khôi phục dòng Huỷ: về Đã lên đơn như ban đầu.
        Assert.Equal(1, orders.Restore([Plan(orders, entries[1])]));
        Assert.True(OrderHistory.SameContent(orders.Get(3), original));

        // Mỗi lần khôi phục cũng là 1 dòng lịch sử (khôi phục nhầm thì khôi phục lại được).
        Assert.Equal(["Khôi phục", "Khôi phục", "Xoá đơn", "Huỷ đơn"], history.Query().Select(e => e.Action));
        Assert.Equal(HistoryKind.Create, history.Query()[1].Kind);

        // Đã giống hệt bản trước → không có gì để khôi phục.
        Assert.NotNull(Plan(orders, entries[1]).Blocked);
    }

    [Fact]
    public void Create_and_edit_from_the_form_record_readable_changes()
    {
        using var temp = new TempFolder();
        var (orders, history, products) = Setup(temp);
        var day = new DateTime(2026, 7, 1, 8, 0, 0);

        var id = orders.Save(new OrderDraft(0, 2, OrderStatus.Shipping, day, false, null, 0, null,
            [new OrderLineDraft(2, 3), new OrderLineDraft(null, 1, true, "Bánh flan", 8000)]), products);
        orders.Save(new OrderDraft(id, 2, OrderStatus.Shipping, day.AddDays(1), true, day.AddDays(1), 2000, "giao trưa",
            [new OrderLineDraft(2, 5), new OrderLineDraft(1, 1)]), products);

        var entries = history.Query();
        Assert.Equal(["Sửa đơn", "Thêm đơn"], entries.Select(e => e.Action));
        Assert.StartsWith("Đơn mới: 3 Bánh bao thịt trứng, 1 Bánh flan · 44.000 đ · Đã lên đơn", HistoryText.Changes(entries[1].Before, entries[1].After).Single());

        var changes = HistoryText.Changes(entries[0].Before, entries[0].After);
        Assert.Contains("Trạng thái: Đã lên đơn → Đã thanh toán", changes);
        Assert.Contains("Ngày giao: 01/07/2026 → 02/07/2026", changes);
        Assert.Contains("Ngày thanh toán: (trống) → 02/07/2026", changes);
        Assert.Contains("Bánh bao thịt trứng: 3 → 5", changes);
        Assert.Contains("− Bánh flan × 1", changes);
        Assert.Contains(changes, c => c.StartsWith("+ ") && c.EndsWith(" × 1"));
        Assert.Contains("Giảm giá: 0 đ → 2.000 đ", changes);
        Assert.Contains("Ghi chú: (trống) → \"giao trưa\"", changes);
        Assert.Contains(changes, c => c.StartsWith("Tổng: 44.000 đ → "));

        // Dòng Thêm không khôi phục được (không có bản trước).
        Assert.NotNull(Plan(orders, entries[1]).Blocked);
    }

    [Fact]
    public void Paying_several_orders_is_one_batch_and_the_batch_restores_together()
    {
        using var temp = new TempFolder();
        var (orders, history, products) = Setup(temp);
        var id = orders.Save(new OrderDraft(0, 1, OrderStatus.Shipping, new DateTime(2026, 6, 28), false, null, 0, null, [new OrderLineDraft(2, 1)]), products);

        Assert.Equal(2, orders.PayShipping([3, id, 1])); // đơn 1 đã thanh toán từ trước → không đổi, không ghi

        var paid = history.Query().Where(e => e.Action == "Thanh toán").ToList();
        Assert.Equal(2, paid.Count);
        Assert.NotNull(paid[0].Batch);
        Assert.Equal(paid[0].Batch, paid[1].Batch);
        Assert.Equal(2, history.Batch(paid[0].Batch!).Count);

        Assert.Equal(2, orders.Restore(history.Batch(paid[0].Batch!).Select(e => Plan(orders, e)).ToList()));
        Assert.All(new[] { orders.Get(3)!, orders.Get(id)! }, o => Assert.Equal((OrderStatus.Shipping, (string?)null), (o.Status, o.PaymentDatetime)));
    }

    [Fact]
    public void Bulk_edit_and_undo_are_recorded_with_the_given_action()
    {
        using var temp = new TempFolder();
        var (orders, history, _) = Setup(temp);
        var plan = BulkEdit.DeliveryDay([orders.Get(3)!], new DateOnly(2026, 7, 5));

        orders.ApplyBulk(plan.Changes, null, "Sửa hàng loạt: " + plan.Title);
        orders.RevertBulk(plan.Changes);

        Assert.Equal(["Hoàn tác sửa hàng loạt", "Sửa hàng loạt: " + plan.Title], history.Query().Select(e => e.Action));
        Assert.Contains(HistoryText.Changes(history.Query()[1].Before, history.Query()[1].After), c => c.StartsWith("Ngày giao:"));
    }

    private static Order Make(long id, long? userId = 1, OrderStatus status = OrderStatus.Shipping, long quantity = 2) =>
        new(id, userId, "Mỹ Ly", status, "2026-11-11T08:30:00.000000", null, 0, 30000 * quantity, 30000 * quantity, string.Empty,
            [new OrderItem(id * 10, id, 15, "Giò", 30000, quantity, 30000 * quantity)]);

    private static HistoryEntry Entry(Order? before, Order? after) => new(1, new DateTime(2026, 11, 16, 9, 0, 0), "Sửa đơn", null, (before ?? after)!.Id, before, after);

    [Fact]
    public void Restore_plan_warns_about_later_changes_reused_ids_and_missing_customers()
    {
        var before = Make(5, quantity: 2);
        var after = Make(5, quantity: 3);

        Assert.Empty(RestorePlan.For(Entry(before, after), after, _ => true).Warnings);
        Assert.Contains("sửa tiếp", RestorePlan.For(Entry(before, after), Make(5, quantity: 4), _ => true).Warnings.Single());
        Assert.Contains("bị xoá sau", RestorePlan.For(Entry(before, after), null, _ => true).Warnings.Single());
        Assert.Contains("không còn trong danh sách", RestorePlan.For(Entry(before, after), after, _ => false).Warnings.Single());

        // Đơn đã xoá mà mã đó giờ là đơn khác → thêm thành đơn mới; sẽ đổi = cả đơn (so với "không có").
        var reused = RestorePlan.For(Entry(before, null), Make(5, userId: 9), _ => true);
        Assert.True(reused.AsNew);
        Assert.StartsWith("Đơn mới:", reused.Changes.Single());

        Assert.NotNull(RestorePlan.For(Entry(null, after), after, _ => true).Blocked); // thêm mới
        Assert.NotNull(RestorePlan.For(Entry(before, after), before with { UserName = "tên mới" }, _ => true).Blocked); // đã như cũ (tên khách không tính)
    }

    [Fact]
    public void Restore_as_new_gets_a_new_id()
    {
        using var temp = new TempFolder();
        var (orders, _, _) = Setup(temp);
        var deleted = orders.Get(3)! with { Note = "đơn cũ đã xoá" };

        var plan = RestorePlan.For(Entry(deleted, null), orders.Get(3), orders.CustomerExists);
        Assert.True(plan.AsNew);
        orders.Restore([plan]);

        var all = orders.Query(new OrderFilter(null, null, null));
        Assert.Equal(3, all.Count);
        Assert.Single(all, o => o.Note == "đơn cũ đã xoá" && o.Id > 3);
        Assert.NotEqual("đơn cũ đã xoá", orders.Get(3)!.Note);
    }

    [Fact]
    public void Unchanged_pairs_are_skipped_and_cleanup_keeps_recent_days()
    {
        using var temp = new TempFolder();
        var history = new OrderHistory(Path.Combine(temp.Path, OrderHistory.FileName));
        var now = new DateTime(2026, 11, 16, 10, 0, 0);

        Assert.Equal(0, history.Record("Sửa đơn", [(Make(1), Make(1) with { UserName = "đổi tên khách" })], now));
        history.Record("Xoá đơn", [(Make(1), null)], now.AddDays(-100));
        history.Record("Sửa đơn", [(Make(2), Make(2, quantity: 3))], now.AddDays(-10));

        Assert.Equal(0, history.Cleanup(0, now)); // 0 = giữ hết
        Assert.Equal(1, history.Cleanup(90, now));
        Assert.Equal([2L], history.Query().Select(e => e.OrderId));
        Assert.Single(history.Query(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6)));
        Assert.Empty(history.Query(new DateOnly(2026, 11, 7), null));
    }

    [Fact]
    public void Keep_days_setting_defaults_to_90_and_round_trips()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, MenuSettings.FileName);
        Assert.Equal(90, MenuSettings.Load(path).HistoryKeepDays);

        File.WriteAllText(path, "{\"HiddenMenu\":[]}"); // file cũ chưa có mục này
        Assert.Equal(90, MenuSettings.Load(path).HistoryKeepDays);

        var settings = new MenuSettings { HistoryKeepDays = 0 };
        settings.Save(path);
        Assert.Equal(0, MenuSettings.Load(path).HistoryKeepDays);
        Assert.Equal(3650, new MenuSettings { HistoryKeepDays = 99999 }.HistoryKeepDays);
    }

    [Fact]
    public void Paging_splits_long_history_and_clamps_page_after_list_shrinks()
    {
        var p = Paging.Of(5000, 1, 100);
        Assert.Equal((50, 100, 100), (p.Pages, p.Skip, p.Take));
        Assert.True(p.HasPrevious && p.HasNext);
        Assert.Equal("Trang 2/50 · dòng 101–200", p.Text);

        var last = Paging.Of(250, 2, 100);
        Assert.Equal((200, 50), (last.Skip, last.Take));
        Assert.False(last.HasNext);

        // Lọc / khôi phục làm danh sách ngắn lại: trang đang xem kẹp về trang cuối còn có.
        Assert.Equal(1, Paging.Of(150, 49, 100).Page);
        Assert.Equal(0, Paging.Of(5000, -3, 100).Page);

        // 1 trang (hoặc rỗng): không có chữ → ẩn thanh chuyển trang.
        Assert.Equal(string.Empty, Paging.Of(100, 0, 100).Text);
        var empty = Paging.Of(0, 5, 100);
        Assert.Equal((0, 1, 0), (empty.Page, empty.Pages, empty.Take));
        Assert.Equal(string.Empty, empty.Text);
    }
}
