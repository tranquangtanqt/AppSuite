using MeRon.Core.Data;
using MeRon.Core.Models;
using MeRon.Core.Orders;

namespace MeRon.Tests;

/// <summary>Đơn "Cần xác nhận" (2026-11-16): chưa rõ khách, chưa rõ giá, tự đánh dấu - dấu nằm ở đầu ô Ghi chú.</summary>
public class OrderConfirmTests
{
    private static Order O(string? note, string? user = "Chị An", long price = 15000, OrderStatus status = OrderStatus.Shipping) =>
        new(1, 1, user, status, "2026-11-16 00:00:00", null, 0, price, price, note,
            [new OrderItem(1, 1, null, "Món XX", price, 1, price)]);

    [Fact]
    public void Parses_and_composes_the_marker_in_the_note()
    {
        Assert.Equal(new ConfirmNote(false, "", "giao trước 8h"), OrderConfirm.Parse("  giao trước 8h "));
        Assert.Equal(new ConfirmNote(true, "Nhung nào? hỏi vợ", "giao trước 8h"), OrderConfirm.Parse("[Cần xác nhận: Nhung nào? hỏi vợ] giao trước 8h"));
        Assert.Equal(new ConfirmNote(true, "", ""), OrderConfirm.Parse("[Cần xác nhận]"));
        Assert.Equal(new ConfirmNote(false, "", "[Cần xác nhận chưa đóng"), OrderConfirm.Parse("[Cần xác nhận chưa đóng"));

        Assert.Equal("[Cần xác nhận: XX chưa rõ giá] giao sớm", OrderConfirm.Compose(true, " XX chưa rõ giá ", "giao sớm"));
        Assert.Equal("[Cần xác nhận]", OrderConfirm.Compose(true, "", ""));
        Assert.Equal("giao sớm", OrderConfirm.Compose(false, "bỏ", " giao sớm "));
        // "]" trong lý do không làm hỏng lúc đọc lại
        var note = OrderConfirm.Compose(true, "giá [chưa] rõ", "ghi chú");
        Assert.Equal(new ConfirmNote(true, "giá [chưa) rõ", "ghi chú"), OrderConfirm.Parse(note));
    }

    [Fact]
    public void Reasons_cover_marker_unknown_customer_and_price_up_to_1d()
    {
        Assert.Empty(OrderConfirm.Reasons(O("giao sớm")));
        Assert.Equal(["Nhung nào?"], OrderConfirm.Reasons(O("[Cần xác nhận: Nhung nào?] x")));
        Assert.Equal(["Cần xác nhận"], OrderConfirm.Reasons(O("[Cần xác nhận]")));
        Assert.Equal(["Chưa có giá: Món XX"], OrderConfirm.Reasons(O(null, price: 1)));
        Assert.Equal(["Chưa có giá: Món XX"], OrderConfirm.Reasons(O(null, price: 0)));
        Assert.Empty(OrderConfirm.Reasons(O(null, price: 2)));
        Assert.Equal(["Chưa rõ khách"], OrderConfirm.Reasons(O(null, "chưa rõ khách")));
        // Lý do đã nói chưa rõ khách → không lặp lại
        Assert.Equal(["Chưa rõ khách: Nhung"], OrderConfirm.Reasons(O("[Cần xác nhận: Chưa rõ khách: Nhung]", "Chưa rõ khách")));
        Assert.Empty(OrderConfirm.Reasons(O("[Cần xác nhận: x]", "Chưa rõ khách", 1, OrderStatus.Cancelled))); // đơn Huỷ không tính

        Assert.Equal("Nhung", OrderConfirm.CustomerHint("[Cần xác nhận: Chưa rõ khách: Nhung?] ghi chú"));
        Assert.Equal("", OrderConfirm.CustomerHint("[Cần xác nhận: hỏi giá]"));
    }

    [Fact]
    public void Repository_lists_confirm_orders_and_fixes_customer()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        var master = new MasterDataRepository(db);
        var orders = new OrderRepository(db);
        var an = master.SaveCustomer(new Customer(0, "Chị An", null, null, null));
        var nhung = master.SaveCustomer(new Customer(0, "Nhung Lê", "W3", null, null));
        var unknown = master.EnsureCustomer(OrderConfirm.UnknownCustomerName);
        Assert.Equal(unknown, master.EnsureCustomer("chưa rõ khách")); // tạo 1 lần, dùng mãi
        var product = master.SaveProduct(new Product(0, null, "Bún riêu", null, 35000, null));
        var products = master.GetProducts().ToDictionary(p => p.Id);
        var day = new DateTime(2026, 11, 16);
        OrderDraft Draft(long user, string? note, params OrderLineDraft[] lines) => new(0, user, OrderStatus.Shipping, day, false, null, 0, note, lines);

        var normal = orders.Save(Draft(an, "giao sớm", new OrderLineDraft(product, 1)), products);
        var noPrice = orders.Save(Draft(an, null, new OrderLineDraft(null, 1, true, "Món XX", 1)), products);
        var marked = orders.Save(Draft(an, OrderConfirm.Compose(true, "hỏi lại số lượng", "giao sớm"), new OrderLineDraft(product, 2)), products);
        var who = orders.Save(Draft(unknown, OrderConfirm.Compose(true, "Chưa rõ khách: Nhung", "để cửa"), new OrderLineDraft(product, 1)), products);
        var cancelled = orders.Save(Draft(an, "[Cần xác nhận]", new OrderLineDraft(product, 1)), products);
        orders.SetStatus(cancelled, OrderStatus.Cancelled);

        Assert.Equal([noPrice, marked, who], orders.NeedsConfirm().Select(o => o.Id).Order().ToArray());
        Assert.DoesNotContain(normal, orders.NeedsConfirm().Select(o => o.Id));

        // Chọn khách đúng: đổi khách + bỏ dấu (lý do chỉ là chưa rõ khách), ghi chú còn lại giữ
        Assert.True(orders.SetCustomer(who, nhung));
        Assert.Equal(nhung, orders.Get(who)!.UserId);
        Assert.Equal("để cửa", orders.Get(who)!.Note);
        // Đã xác nhận: bỏ dấu, giữ ghi chú
        Assert.True(orders.ClearConfirm(marked));
        Assert.Equal("giao sớm", orders.Get(marked)!.Note);
        Assert.False(orders.ClearConfirm(normal)); // không có dấu → không đổi
        Assert.Equal([noPrice], orders.NeedsConfirm().Select(o => o.Id).ToArray()); // món 1đ: còn tới khi sửa giá

        // Lý do khác (không phải chưa rõ khách) → đổi khách vẫn giữ dấu
        var other = orders.Save(Draft(unknown, OrderConfirm.Compose(true, "hỏi giá", null), new OrderLineDraft(product, 1)), products);
        orders.SetCustomer(other, an);
        Assert.True(OrderConfirm.Parse(orders.Get(other)!.Note).Marked);

        // Sao chép đơn không chép dấu
        var copy = OrderCopier.CopyAsNew(orders.Get(other)!, DateOnly.FromDateTime(day), products);
        Assert.Equal("", copy.Draft.Note);
    }

    [Fact]
    public void Batch_line_saved_to_unknown_customer_keeps_written_name_as_reason()
    {
        var customers = new[] { new Customer(1, "Nhung Lê", "W3", null, null), new Customer(2, "Nhung Trần", "W1", null, null), new Customer(9, "Chưa rõ khách", null, null, null) };
        var products = new[] { new Product(10, 1, "Bánh bao", null, 15000, null) };
        var line = BatchOrderParser.Parse("Nhung? | 2 bánh bao", new DateOnly(2026, 11, 16)).Lines.Single();
        var free = BatchOrderMatcher.Match(line, customers, products, new Abbreviations(""));
        Assert.Null(free.Customer); // 2 Nhung → chưa ghép được
        Assert.Null(free.DraftNote);

        var pinned = BatchOrderMatcher.Match(line, customers, products, new Abbreviations(""), BatchPin.Empty.WithCustomer(9));
        Assert.Equal(9, pinned.Customer?.Id);
        Assert.True(pinned.CanSave, string.Join("; ", pinned.Problems));
        Assert.Equal("[Cần xác nhận: Chưa rõ khách: Nhung]", pinned.ToDraft(new DateOnly(2026, 11, 16)).Note);
    }
}
