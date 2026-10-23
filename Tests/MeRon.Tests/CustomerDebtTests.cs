using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.Core.Reports;

namespace MeRon.Tests;

/// <summary>Trang Công nợ: đơn chưa thanh toán gom theo khách (<see cref="CustomerDebts"/>), câu truy vấn và Thanh toán hết.</summary>
public class CustomerDebtTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);

    private static readonly Dictionary<long, Customer> Customers = new()
    {
        [1] = new Customer(1, "908 Chị Huyền", "W2", "0901 234 567", null),
        [2] = new Customer(2, "Anh Bình", null, " ", null),
    };

    private static UnpaidOrderPoint O(long? user, long id, int? month, int day, long total) =>
        new(user, id, month is { } m ? new DateOnly(2026, m, day) : null, total);

    [Fact]
    public void Groups_by_customer_with_total_and_oldest_age()
    {
        var debts = CustomerDebts.Build(
        [
            O(1, 10, 10, 20, 30000), O(1, 11, 9, 1, 15000), O(1, 12, 10, 23, 5000),
            O(2, 20, 10, 22, 100000),
            O(null, 30, 10, 1, 7000),
            O(9, 90, null, 1, 2000), // khách không còn trong danh sách, đơn không có ngày giao
        ], Customers, Today);

        // Mặc định nợ nhiều nhất trước.
        Assert.Equal(["Anh Bình", "908 Chị Huyền", CustomerDebts.NoCustomerName, "Khách #9"], debts.Select(d => d.Name));

        var huyen = debts[1];
        Assert.Equal(50000, huyen.Amount);
        Assert.Equal([11L, 10L, 12L], huyen.OrderIds); // cũ trước
        Assert.Equal((new DateOnly(2026, 9, 1), 52, new DateOnly(2026, 10, 23)), (huyen.OldestDay, huyen.OldestDays, huyen.NewestDay));
        Assert.True(huyen.IsOverdue);
        Assert.Equal(("0901 234 567", "W2"), (huyen.Phone, huyen.Address));

        var binh = debts[0];
        Assert.Equal((1, 1, false), (binh.OrderCount, binh.OldestDays, binh.IsOverdue));
        Assert.Null(binh.Phone); // SĐT chỉ có dấu cách

        Assert.Null(debts[2].UserId);
        Assert.Equal((null, 0), (debts[3].OldestDay, debts[3].OldestDays));
    }

    [Fact]
    public void Sort_oldest_first_and_future_delivery_counts_as_zero_days()
    {
        var debts = CustomerDebts.Build([O(1, 1, 9, 1, 1000), O(2, 2, 10, 25, 900000)], Customers, Today);
        Assert.Equal(0, debts.Single(d => d.UserId == 2).OldestDays); // giao 25/10 > hôm nay
        Assert.Equal(["908 Chị Huyền", "Anh Bình"], CustomerDebts.Sort(debts, DebtSort.Oldest).Select(d => d.Name));
        Assert.Equal(["Anh Bình", "908 Chị Huyền"], CustomerDebts.Sort(debts, DebtSort.Amount).Select(d => d.Name));
    }

    [Fact]
    public void Reminder_message_uses_friendly_name_count_and_total()
    {
        var debts = CustomerDebts.Build([O(1, 10, 9, 1, 15000), O(1, 11, 10, 20, 30000), O(2, 20, 10, 22, 100000)], Customers, Today);
        Assert.Equal("Dạ em chào Chị Huyền ạ! Em gửi mình 2 đơn chưa thanh toán (từ ngày 01/09), tổng 45.000 đ. " +
                     "Mình xem giúp em và chuyển khoản khi tiện nha, em cảm ơn ạ!", CustomerDebts.ReminderMessage(debts.Single(d => d.UserId == 1)));
        Assert.StartsWith("Dạ em chào Anh Bình ạ! Em gửi mình đơn chưa thanh toán ngày 22/10, tổng 100.000 đ.",
            CustomerDebts.ReminderMessage(debts.Single(d => d.UserId == 2)));
    }

    [Fact]
    public void Repository_reads_unpaid_orders_until_today_and_pays_them()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Path)).Resolve()), db);
        var orders = new OrderRepository(db);
        var products = new MasterDataRepository(db).GetProducts().ToDictionary(p => p.Id);
        var reports = new ReportRepository(db, orders);

        // Bộ mẫu: đơn 1 Chị An 27/6 Đã thanh toán; đơn 3 Anh Bình 28/6 Đã lên đơn.
        var an1 = orders.Save(new OrderDraft(0, 1, OrderStatus.Shipping, new DateTime(2026, 10, 1, 7, 0, 0), false, null, 0, null, [new OrderLineDraft(1, 2)]), products);
        var anFuture = orders.Save(new OrderDraft(0, 1, OrderStatus.Shipping, new DateTime(2026, 10, 30), false, null, 0, null, [new OrderLineDraft(1, 1)]), products);
        var cancelled = orders.Save(new OrderDraft(0, 2, OrderStatus.Shipping, new DateTime(2026, 10, 2), false, null, 0, null, [new OrderLineDraft(1, 9)]), products);
        orders.SetStatus(cancelled, OrderStatus.Cancelled);

        var debts = reports.CustomerDebts(Today, includeFuture: false);
        var an = Assert.Single(debts, d => d.Name == "Chị An");
        Assert.Equal([an1], an.OrderIds); // không có đơn đã thanh toán, đơn giao 30/10
        Assert.Equal(22, an.OldestDays);
        var binh = Assert.Single(debts, d => d.Name == "Anh Bình");
        Assert.Equal([3L], binh.OrderIds); // không có đơn huỷ
        Assert.Equal(117, binh.OldestDays); // 28/6 → 23/10

        Assert.Equal([an1, anFuture], reports.CustomerDebts(Today, includeFuture: true).Single(d => d.Name == "Chị An").OrderIds);

        // Thanh toán hết: chỉ đơn còn Đã lên đơn; đơn đã thanh toán / huỷ trong danh sách bị bỏ qua.
        var paidOrder1 = orders.Get(1)!.PaymentDatetime;
        var paidAt = new DateTime(2026, 10, 23, 9, 30, 0);
        Assert.Equal(1, orders.PayShipping([an1, 1, cancelled], paidAt));
        Assert.Equal(OrderStatus.Completed, orders.Get(an1)!.Status);
        Assert.Equal(paidAt, orders.Get(an1)!.Payment); // ghi ngày thanh toán
        Assert.Equal(paidOrder1, orders.Get(1)!.PaymentDatetime); // đơn đã thanh toán từ trước: giữ ngày cũ
        Assert.Equal(OrderStatus.Cancelled, orders.Get(cancelled)!.Status);
        Assert.Null(orders.Get(cancelled)!.PaymentDatetime);
        Assert.DoesNotContain(reports.CustomerDebts(Today, false), d => d.Name == "Chị An");
    }
}
