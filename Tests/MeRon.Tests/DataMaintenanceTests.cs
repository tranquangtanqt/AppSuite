using MeRon.Core.Data;

namespace MeRon.Tests;

public class DataMaintenanceTests
{
    /// <summary>
    /// Khách 1 (tạo 2023): đơn 2024-03 (2 dòng) + 2026-05. Khách 2 (tạo 2023): chỉ đơn 2024-11 (Huỷ) và 2025-01 (không có
    /// ngày giao → theo ngày tạo, dạng "yyyy-MM-dd HH:mm:ss"). Khách 3 (tạo 2026): chưa có đơn. Món 1 bán 2024 + 2026, món 2
    /// chỉ bán 2024, món 3 (tạo 2026) chưa bán. Phiếu nhập 2024-06, 2025-12, 2026-01.
    /// </summary>
    private static MeRonDatabase Seed(TempFolder temp)
    {
        var db = temp.NewDatabase();
        using var connection = db.Open();
        void Sql(string text)
        {
            using var command = connection.CreateCommand();
            command.CommandText = text;
            command.ExecuteNonQuery();
        }
        Sql("INSERT INTO Categories(id, name, createdAt) VALUES (1, 'Bánh', '2023-01-01T00:00:00'), (2, 'Trống', '2023-01-01T00:00:00')");
        Sql("INSERT INTO Users(id, name, createdAt) VALUES (1, 'An', '2023-01-01T08:00:00.123456'), (2, 'Bình', '2023-05-01 08:00:00'), (3, 'Chi', '2026-02-01T08:00:00')");
        Sql("INSERT INTO Products(id, categoryId, name, price, createdAt) VALUES (1, 1, 'Bánh bao', 10000, '2023-01-01T00:00:00'), " +
            "(2, 1, 'Bánh flan', 5000, '2023-01-01T00:00:00'), (3, 1, 'Chè', 7000, '2026-03-01T00:00:00')");
        Sql("INSERT INTO Orders(id, userId, status, deliveryDatetime, total, createdAt) VALUES " +
            "(1, 1, 2, '2024-03-10T09:00:00.000000', 25000, '2024-03-09T00:00:00'), " +
            "(2, 2, 3, '2024-11-02 10:00:00', 99000, '2024-11-01T00:00:00'), " +       // Huỷ
            "(3, 2, 1, NULL, 15000, '2025-01-05 07:00:00'), " +                         // không có ngày giao
            "(4, 1, 2, '2026-05-01T12:00:00', 10000, '2026-04-30T00:00:00')");
        Sql("INSERT INTO OrderItems(id, orderId, productId, snapshotName, snapshotPrice, quantity, lineTotal) VALUES " +
            "(1, 1, 1, 'Bánh bao', 10000, 2, 20000), (2, 1, 2, 'Bánh flan', 5000, 1, 5000), (3, 2, 2, 'Bánh flan', 5000, 1, 5000), " +
            "(4, 3, 1, 'Bánh bao', 10000, 1, 10000), (5, 4, 1, 'Bánh bao', 10000, 1, 10000)");
        Sql("INSERT INTO Purchases(id, date, total, createdAt) VALUES (1, '2024-06-01', 50000, '2024-06-01'), " +
            "(2, '2025-12-31T23:00:00', 70000, '2025-12-31'), (3, '2026-01-02', 30000, '2026-01-02')");
        Sql("INSERT INTO PurchaseItems(id, purchaseId, name, price) VALUES (1, 1, 'Bột', 50000), (2, 2, 'Trứng', 40000), (3, 2, 'Sữa', 30000), (4, 3, 'Đường', 30000)");
        return db;
    }

    [Fact]
    public void Overview_counts_tables_dates_years_and_unused_data()
    {
        using var temp = new TempFolder();
        var overview = new DataMaintenance(Seed(temp)).Overview();

        Assert.True(overview.FileBytes > 0);
        Assert.Equal(8, overview.Tables.Count);
        Assert.Equal(4, overview.Tables.Single(t => t.Name == "Orders").Rows);
        Assert.Equal(5, overview.Tables.Single(t => t.Name == "OrderItems").Rows);
        Assert.Equal((new DateOnly(2024, 3, 10), new DateOnly(2026, 5, 1)), (overview.FirstOrderDay, overview.LastOrderDay));
        Assert.Equal((new DateOnly(2024, 6, 1), new DateOnly(2026, 1, 2)), (overview.FirstPurchaseDay, overview.LastPurchaseDay));

        Assert.Equal([2026, 2025, 2024], overview.Years.Select(y => y.Year)); // mới nhất trước
        var y2024 = overview.Years.Single(y => y.Year == 2024);
        Assert.Equal((2L, 1L, 25000L, 1L, 50000L), (y2024.Orders, y2024.CancelledOrders, y2024.Sales, y2024.Purchases, y2024.PurchaseAmount)); // Huỷ không tính tiền
        var y2025 = overview.Years.Single(y => y.Year == 2025);
        Assert.Equal((1L, 15000L, 1L, 70000L), (y2025.Orders, y2025.Sales, y2025.Purchases, y2025.PurchaseAmount)); // đơn không ngày giao → ngày tạo

        Assert.Equal(1, overview.CustomersWithoutOrders); // Chi
        Assert.Equal(1, overview.ProductsNeverSold);      // Chè
        Assert.Equal(1, overview.EmptyCategories);        // Trống
    }

    [Fact]
    public void Months_of_a_year_has_twelve_rows()
    {
        using var temp = new TempFolder();
        var months = new DataMaintenance(Seed(temp)).Months(2024);
        Assert.Equal(Enumerable.Range(1, 12), months.Select(m => m.Month));
        Assert.Equal(1, months[2].Orders);           // tháng 3
        Assert.Equal(1, months[10].CancelledOrders); // tháng 11
        Assert.Equal(50000, months[5].PurchaseAmount); // tháng 6
        Assert.Equal(0, months[0].Orders + months[0].Purchases);
    }

    [Fact]
    public void Plan_counts_what_would_be_deleted_without_deleting()
    {
        using var temp = new TempFolder();
        var db = Seed(temp);
        var maintenance = new DataMaintenance(db);

        var plan = maintenance.Plan(new CleanupOptions(new DateOnly(2026, 1, 1), true, true));
        Assert.Equal((3L, 4L, 40000L), (plan.Orders, plan.OrderItems, plan.OrderAmount)); // đơn 1, 2 (Huỷ - không tính tiền), 3
        Assert.Equal((2L, 3L, 120000L), (plan.Purchases, plan.PurchaseItems, plan.PurchaseAmount));
        Assert.Equal(1, plan.Customers); // Bình hết đơn; An còn đơn 2026; Chi tạo sau mốc → giữ
        Assert.Equal(1, plan.Products);  // Bánh flan chỉ bán trước mốc; Chè tạo sau mốc → giữ
        Assert.Equal(4, db.Count("Orders"));

        var nothing = maintenance.Plan(new CleanupOptions(new DateOnly(2020, 1, 1), true, true));
        Assert.True(nothing.IsEmpty);
        var noExtras = maintenance.Plan(new CleanupOptions(new DateOnly(2026, 1, 1), false, false));
        Assert.Equal((0L, 0L), (noExtras.Customers, noExtras.Products));
    }

    [Fact]
    public void Cleanup_deletes_old_rows_keeps_newer_and_counts_unsynced_edits()
    {
        using var temp = new TempFolder();
        var db = Seed(temp);
        db.ResetUnsyncedEdits();

        var done = new DataMaintenance(db).Cleanup(new CleanupOptions(new DateOnly(2026, 1, 1), true, true));

        Assert.Equal(3, done.Orders);
        Assert.Equal(1, db.Count("Orders"));
        Assert.Equal(1, db.Count("OrderItems"));
        Assert.Equal(1, db.Count("Purchases"));
        Assert.Equal(1, db.Count("PurchaseItems"));
        Assert.Equal(2, db.Count("Users"));    // An, Chi
        Assert.Equal(2, db.Count("Products")); // Bánh bao, Chè
        Assert.Equal(2, db.Count("Categories")); // danh mục không bị đụng
        Assert.True(db.GetUnsyncedEdits().Count >= 3 + 4 + 2 + 3 + 1 + 1); // các dòng bị xoá → nhắc xuất sang điện thoại
        Assert.Single(Directory.GetFiles(db.BackupsFolder, "me_ron_truoc-don_*.db")); // bản sao an toàn trước khi xoá

        var overview = new DataMaintenance(db).Overview();
        Assert.Equal([2026], overview.Years.Select(y => y.Year));
    }

    [Fact]
    public void Cleanup_without_extras_keeps_customers_and_products()
    {
        using var temp = new TempFolder();
        var db = Seed(temp);
        new DataMaintenance(db).Cleanup(new CleanupOptions(new DateOnly(2026, 1, 1), false, false));
        Assert.Equal(3, db.Count("Users"));
        Assert.Equal(3, db.Count("Products"));
        Assert.Equal(1, db.Count("Orders"));
    }

    [Fact]
    public void Empty_database_overview()
    {
        using var temp = new TempFolder();
        var overview = new DataMaintenance(temp.NewDatabase()).Overview();
        Assert.Empty(overview.Years);
        Assert.Null(overview.FirstOrderDay);
        Assert.All(overview.Tables, t => Assert.Equal(0, t.Rows));
    }
}
