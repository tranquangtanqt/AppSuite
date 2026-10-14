using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.Core.Reports;

namespace MeRon.Tests;

public class LapsedCustomerTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static readonly Dictionary<long, Customer> Customers = new()
    {
        [1] = new Customer(1, "908 Chị Huyền", null, "0901 234 567", null),
        [2] = new Customer(2, "Anh Bình", "Ngõ 5", " ", null),
        [3] = new Customer(3, "12a12", null, null, null),
        [4] = new Customer(4, "Chị Lan", null, null, null),
    };

    private static CustomerOrderPoint O(long user, long id, int month, int day, long total = 10000) => new(user, id, new DateOnly(2026, month, day), total);

    /// <summary>
    /// Khách 1: đặt mỗi 7 ngày (1/8 → 29/8), nghỉ 46 ngày → lâu không đặt, quá thói quen.
    /// Khách 2: 2 đơn 1/7 và 1/9, nghỉ 43 ngày → lâu không đặt; 2 ngày đặt chưa đủ để có thói quen.
    /// Khách 3: 1 đơn → chưa đủ 2 đơn. Khách 4: đơn gần nhất 10/10 → mới đặt. Khách 9: không còn trong danh sách khách.
    /// </summary>
    private static IReadOnlyList<CustomerOrderPoint> Orders =>
    [
        O(1, 10, 8, 1), O(1, 11, 8, 8), O(1, 12, 8, 15), O(1, 13, 8, 22), O(1, 14, 8, 29), O(1, 15, 8, 29, 5000),
        O(2, 20, 7, 1, 100000), O(2, 21, 9, 1, 150000),
        O(3, 30, 5, 1),
        O(4, 40, 6, 1), O(4, 41, 10, 10),
        O(9, 90, 1, 1), O(9, 91, 1, 2),
    ];

    private static readonly CustomerProductQuantity[] Products =
    [
        new(1, "Bánh bao xá xíu ", 12), new(1, "Bánh flan", 3), new(1, "Chè", 3), new(1, "Sữa chua", 1),
        new(2, "Bánh chưng", 2),
    ];

    [Fact]
    public void Finds_customers_with_enough_orders_and_no_recent_order()
    {
        var list = LapsedCustomers.Build(Orders, Products, Customers, Today, 30, 2);

        Assert.Equal([1L, 2L], list.Select(c => c.UserId)); // lâu nhất trước: 46 ngày, 43 ngày
        var huyen = list[0];
        Assert.Equal((6L, 55000L), (huyen.OrderCount, huyen.Amount));
        Assert.Equal((new DateOnly(2026, 8, 29), 46, 15L), (huyen.LastDay, huyen.DaysSince, huyen.LastOrderId));
        Assert.Equal(7, huyen.AverageGapDays); // 5 ngày giao khác nhau (2 đơn cùng 29/8), 28 ngày
        Assert.True(huyen.IsOverHabit);
        Assert.Equal(["Bánh bao xá xíu", "Bánh flan", "Chè"], huyen.TopProducts);
        Assert.Equal("0901 234 567", huyen.Phone);

        var binh = list[1];
        Assert.Null(binh.AverageGapDays); // 2 ngày đặt: chưa đủ để biết thói quen
        Assert.False(binh.IsOverHabit);
        Assert.Null(binh.Phone); // SĐT toàn khoảng trắng
        Assert.Equal("Ngõ 5", binh.Address);
    }

    [Fact]
    public void Thresholds_change_who_is_listed()
    {
        Assert.Equal([1L, 2L, 3L], LapsedCustomers.Build(Orders, Products, Customers, Today, 30, 1).Select(c => c.UserId).Order());
        Assert.Equal([1L], LapsedCustomers.Build(Orders, Products, Customers, Today, 45, 2).Select(c => c.UserId));
        Assert.Equal([1L, 2L, 4L], LapsedCustomers.Build(Orders, Products, Customers, Today, 0, 2).Select(c => c.UserId).Order());
        Assert.Empty(LapsedCustomers.Build(Orders, Products, Customers, Today, 30, 7));
        // đơn giao trong tương lai: vẫn đang đặt
        Assert.DoesNotContain(LapsedCustomers.Build([.. Orders, O(2, 22, 10, 20)], Products, Customers, Today, 30, 2), c => c.UserId == 2);
    }

    [Fact]
    public void Sorts_by_amount_or_by_how_far_past_the_habit()
    {
        var list = LapsedCustomers.Build(Orders, Products, Customers, Today, 0, 1);

        Assert.Equal(2, LapsedCustomers.Sort(list, LapsedSort.Amount)[0].UserId);
        var overHabit = LapsedCustomers.Sort(list, LapsedSort.OverHabit);
        Assert.Equal(1, overHabit[0].UserId); // 46 / 7
        Assert.Equal([3L, 2L, 4L], overHabit.Skip(1).Select(c => c.UserId)); // chưa có thói quen → cuối, lâu hơn trước
    }

    [Fact]
    public void Zalo_message_drops_room_codes_and_names_the_favourite_product()
    {
        var list = LapsedCustomers.Build(Orders, Products, Customers, Today, 0, 1).ToDictionary(c => c.UserId);

        Assert.Equal("Dạ em chào Chị Huyền ạ! Lâu rồi chưa thấy mình đặt Bánh bao xá xíu. Tuần này mình có cần đặt không ạ? Em làm sẵn cho mình nha.",
            LapsedCustomers.ZaloMessage(list[1]));
        Assert.StartsWith("Dạ em chào anh/chị ạ! Lâu rồi chưa thấy mình đặt bánh.", LapsedCustomers.ZaloMessage(list[3]));
        var caps = list[2] with { Name = "KHÁNH QUỲNH W3" };
        Assert.StartsWith("Dạ em chào Khánh Quỳnh ạ!", LapsedCustomers.ZaloMessage(caps));
        Assert.StartsWith("Dạ em chào Chị Lan ạ!", LapsedCustomers.ZaloMessage(caps with { Name = "CT3 Chị Lan w1" }));
    }

    [Fact]
    public void Repository_reads_orders_without_cancelled_ones()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Path)).Resolve()), db);
        var orders = new OrderRepository(db);
        var products = new MasterDataRepository(db).GetProducts().ToDictionary(p => p.Id);
        var cancelled = orders.Save(new OrderDraft(0, 2, OrderStatus.Shipping, new DateTime(2026, 10, 1), false, null, 0, null,
            [new OrderLineDraft(1, 10)]), products);
        orders.SetStatus(cancelled, OrderStatus.Cancelled);
        orders.Save(new OrderDraft(0, 1, OrderStatus.Shipping, new DateTime(2026, 7, 1), false, null, 0, null, [new OrderLineDraft(2, 3)]), products);
        var reports = new ReportRepository(db, orders);

        // Bộ mẫu: Chị An 1 đơn 27/6 (+ đơn 1/7 ở trên), Anh Bình 1 đơn 28/6 (+ đơn huỷ 1/10).
        var an = Assert.Single(reports.LapsedCustomers(Today, 30, 2));
        Assert.Equal(("Chị An", 2L, new DateOnly(2026, 7, 1)), (an.Name, an.OrderCount, an.LastDay));
        Assert.Equal("Bánh bao thịt trứng", an.TopProducts[0]); // 2 + 3 cái
        Assert.Equal("0901", an.Phone);

        // Đơn huỷ 1/10 không làm Anh Bình thành "mới đặt" và không tính vào số đơn.
        var binh = Assert.Single(reports.LapsedCustomers(Today, 30, 1), c => c.Name == "Anh Bình");
        Assert.Equal((1L, new DateOnly(2026, 6, 28)), (binh.OrderCount, binh.LastDay));
        Assert.Equal(["Bánh flan"], binh.TopProducts);
    }
}
