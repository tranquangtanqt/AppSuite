using MeRon.Core.Data;
using MeRon.Core.Import;

namespace MeRon.Tests;

public class DataMaintenanceTests
{
    private static readonly CleanupOptions Before2026All = new(new DateOnly(2026, 1, 1), true, true);

    /// <summary>
    /// Khách 1 An (2023): đơn 1 2024-03 đã TT (2 dòng) + đơn 4 2026-05 đã TT. Khách 2 Bình (2023): đơn 2 2024-11 Huỷ + đơn 3
    /// CHƯA THANH TOÁN, không có ngày giao (ngày tạo 2025-01-05, dạng "yyyy-MM-dd HH:mm:ss"). Khách 3 Chi (2026): chưa có
    /// đơn. Khách 4 Dũng (2023): chỉ đơn 5 2024-08 đã TT. Món 1 bán ở đơn 1, 3, 4; món 2 chỉ ở đơn 1, 2, 5; món 3 (2026)
    /// chưa bán. Phiếu nhập 2024-06, 2025-12, 2026-01.
    /// </summary>
    private static MeRonDatabase Seed(TempFolder temp)
    {
        var db = temp.NewDatabase();
        Sql(db,
            "INSERT INTO Categories(id, name, createdAt) VALUES (1, 'Bánh', '2023-01-01T00:00:00'), (2, 'Trống', '2023-01-01T00:00:00')",
            "INSERT INTO Users(id, name, createdAt) VALUES (1, 'An', '2023-01-01T08:00:00.123456'), (2, 'Bình', '2023-05-01 08:00:00'), " +
            "(3, 'Chi', '2026-02-01T08:00:00'), (4, 'Dũng', '2023-06-01T08:00:00')",
            "INSERT INTO Products(id, categoryId, name, price, createdAt) VALUES (1, 1, 'Bánh bao', 10000, '2023-01-01T00:00:00'), " +
            "(2, 1, 'Bánh flan', 5000, '2023-01-01T00:00:00'), (3, 1, 'Chè', 7000, '2026-03-01T00:00:00')",
            "INSERT INTO Orders(id, userId, status, deliveryDatetime, total, createdAt) VALUES " +
            "(1, 1, 2, '2024-03-10T09:00:00.000000', 25000, '2024-03-09T00:00:00'), " +
            "(2, 2, 3, '2024-11-02 10:00:00', 99000, '2024-11-01T00:00:00'), " +
            "(3, 2, 1, NULL, 15000, '2025-01-05 07:00:00'), " +
            "(4, 1, 2, '2026-05-01T12:00:00', 10000, '2026-04-30T00:00:00'), " +
            "(5, 4, 2, '2024-08-01T12:00:00', 20000, '2024-07-30T00:00:00')",
            "INSERT INTO OrderItems(id, orderId, productId, snapshotName, snapshotPrice, quantity, lineTotal, createdAt) VALUES " +
            "(1, 1, 1, 'Bánh bao', 10000, 2, 20000, 'a1'), (2, 1, 2, 'Bánh flan', 5000, 1, 5000, 'a2'), (3, 2, 2, 'Bánh flan', 5000, 1, 5000, 'a3'), " +
            "(4, 3, 1, 'Bánh bao', 10000, 1, 10000, 'a4'), (5, 4, 1, 'Bánh bao', 10000, 1, 10000, 'a5'), (6, 5, 2, 'Bánh flan', 5000, 4, 20000, 'a6')",
            "INSERT INTO Purchases(id, date, total, createdAt) VALUES (1, '2024-06-01', 50000, '2024-06-01'), " +
            "(2, '2025-12-31T23:00:00', 70000, '2025-12-31'), (3, '2026-01-02', 30000, '2026-01-02')",
            "INSERT INTO PurchaseItems(id, purchaseId, name, price, createdAt) VALUES (1, 1, 'Bột', 50000, 'b1'), (2, 2, 'Trứng', 40000, 'b2'), " +
            "(3, 2, 'Sữa', 30000, 'b3'), (4, 3, 'Đường', 30000, 'b4')");
        return db;
    }

    private static void Sql(MeRonDatabase db, params string[] statements)
    {
        using var connection = db.Open();
        foreach (var text in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = text;
            command.ExecuteNonQuery();
        }
    }

    private static long Value(MeRonDatabase db, string sql)
    {
        using var connection = db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>Như trang: xuất TSV vào thư mục "Truoc khi don" rồi dọn, ghi don-dep.json vào bộ vừa xuất.</summary>
    private static (string Folder, CleanupRecord Record) CleanupWithBackup(TempFolder temp, MeRonDatabase db, CleanupOptions options)
    {
        var backup = BackupExporter.Export(db, temp.Sub("Truoc khi don"), new DateTime(2026, 10, 16, 13, 0, 0));
        new DataMaintenance(db).Cleanup(options, backup.Folder, new DateTime(2026, 10, 16, 13, 0, 0));
        var (folder, record) = Assert.Single(DataMaintenance.ListCleanups(temp.Sub("Truoc khi don")));
        Assert.Equal(backup.Folder, folder);
        return (folder, record);
    }

    [Fact]
    public void Overview_counts_tables_dates_years_and_unused_data()
    {
        using var temp = new TempFolder();
        var overview = new DataMaintenance(Seed(temp)).Overview();

        Assert.True(overview.FileBytes > 0);
        Assert.Equal(8, overview.Tables.Count);
        Assert.Equal(5, overview.Tables.Single(t => t.Name == "Orders").Rows);
        Assert.Equal(6, overview.Tables.Single(t => t.Name == "OrderItems").Rows);
        Assert.Equal((new DateOnly(2024, 3, 10), new DateOnly(2026, 5, 1)), (overview.FirstOrderDay, overview.LastOrderDay));
        Assert.Equal((new DateOnly(2024, 6, 1), new DateOnly(2026, 1, 2)), (overview.FirstPurchaseDay, overview.LastPurchaseDay));

        Assert.Equal([2026, 2025, 2024], overview.Years.Select(y => y.Year)); // mới nhất trước
        var y2024 = overview.Years.Single(y => y.Year == 2024);
        Assert.Equal((3L, 1L, 45000L, 1L, 50000L), (y2024.Orders, y2024.CancelledOrders, y2024.Sales, y2024.Purchases, y2024.PurchaseAmount)); // Huỷ không tính tiền
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
        Assert.Equal(1, months[2].Orders);             // tháng 3
        Assert.Equal(1, months[7].Orders);             // tháng 8
        Assert.Equal(1, months[10].CancelledOrders);   // tháng 11
        Assert.Equal(50000, months[5].PurchaseAmount); // tháng 6
        Assert.Equal(0, months[0].Orders + months[0].Purchases);
    }

    [Fact]
    public void Plan_keeps_unpaid_orders_and_their_customers()
    {
        using var temp = new TempFolder();
        var db = Seed(temp);
        var maintenance = new DataMaintenance(db);

        var plan = maintenance.Plan(Before2026All);
        Assert.Equal((3L, 4L, 45000L), (plan.Orders, plan.OrderItems, plan.OrderAmount)); // đơn 1, 2 (Huỷ - không tính tiền), 5
        Assert.Equal((1L, 15000L), (plan.KeptUnpaid, plan.KeptUnpaidAmount));             // đơn 3 chưa thanh toán: giữ
        Assert.Equal((2L, 3L, 120000L), (plan.Purchases, plan.PurchaseItems, plan.PurchaseAmount));
        Assert.Equal(1, plan.Customers); // Dũng; Bình còn đơn chưa TT → giữ; An còn đơn 2026; Chi tạo sau mốc
        Assert.Equal(1, plan.Products);  // Bánh flan; Bánh bao còn trong đơn chưa TT / 2026; Chè tạo sau mốc
        Assert.Equal(5, db.Count("Orders"));

        Assert.True(maintenance.Plan(new CleanupOptions(new DateOnly(2020, 1, 1), true, true)).IsEmpty);
        var noExtras = maintenance.Plan(new CleanupOptions(new DateOnly(2026, 1, 1), false, false));
        Assert.Equal((0L, 0L), (noExtras.Customers, noExtras.Products));
    }

    [Fact]
    public void Cleanup_deletes_old_rows_keeps_unpaid_and_newer_and_counts_unsynced_edits()
    {
        using var temp = new TempFolder();
        var db = Seed(temp);
        db.ResetUnsyncedEdits();

        var done = new DataMaintenance(db).Cleanup(Before2026All);

        Assert.Equal(3, done.Orders);
        Assert.Equal(2, db.Count("Orders"));      // 3 (chưa TT), 4
        Assert.Equal(1, Value(db, "SELECT COUNT(*) FROM Orders WHERE id = 3"));
        Assert.Equal(2, db.Count("OrderItems"));
        Assert.Equal(1, db.Count("Purchases"));
        Assert.Equal(1, db.Count("PurchaseItems"));
        Assert.Equal(3, db.Count("Users"));       // An, Bình (còn nợ), Chi
        Assert.Equal(2, db.Count("Products"));    // Bánh bao, Chè
        Assert.Equal(2, db.Count("Categories"));  // danh mục không bị đụng
        Assert.True(db.GetUnsyncedEdits().Count >= 3 + 4 + 2 + 3 + 1 + 1); // các dòng bị xoá → nhắc xuất sang điện thoại
        Assert.Single(Directory.GetFiles(db.BackupsFolder, "me_ron_truoc-don_*.db")); // bản sao an toàn trước khi xoá
        Assert.Equal([2026, 2025], new DataMaintenance(db).Overview().Years.Select(y => y.Year)); // 2025 còn đơn chưa TT
    }

    [Fact]
    public void Cleanup_writes_record_and_restore_brings_everything_back_once()
    {
        using var temp = new TempFolder();
        var db = Seed(temp);
        var (folder, record) = CleanupWithBackup(temp, db, Before2026All);
        Assert.Equal(new DateOnly(2026, 1, 1), record.Before);
        Assert.Equal((3, 4, 2, 3, 1, 1), (record.CountOf("Orders"), record.CountOf("OrderItems"), record.CountOf("Purchases"),
            record.CountOf("PurchaseItems"), record.CountOf("Users"), record.CountOf("Products")));
        var maintenance = new DataMaintenance(db);
        Assert.Equal(14, maintenance.Missing(record));

        // Thêm 1 đơn mới sau khi dọn: khôi phục phải giữ nguyên nó.
        Sql(db, "INSERT INTO Orders(userId, status, deliveryDatetime, total, createdAt) VALUES (1, 1, '2026-10-16T08:00:00', 5000, '2026-10-16T07:00:00')");

        var result = maintenance.Restore(folder, record);
        Assert.Equal((3, 4, 2, 3, 1, 1), (result.AddedOf("Orders"), result.AddedOf("OrderItems"), result.AddedOf("Purchases"),
            result.AddedOf("PurchaseItems"), result.AddedOf("Users"), result.AddedOf("Products")));
        Assert.Equal((0, 0, 0), (result.Merged, result.NewIds, result.NotInBackup));
        Assert.NotNull(result.SafetyCopy);
        Assert.Equal(6, db.Count("Orders")); // 5 cũ + 1 mới
        Assert.Equal((6L, 3L, 4L, 4L, 3L), (db.Count("OrderItems"), db.Count("Purchases"), db.Count("PurchaseItems"), db.Count("Users"), db.Count("Products")));
        Assert.Equal(4, Value(db, "SELECT userId FROM Orders WHERE id = 5")); // đơn của Dũng nối đúng khách khôi phục
        Assert.Equal(0, maintenance.Missing(record));

        var again = maintenance.Restore(folder, record);
        Assert.Empty(again.Added);
        Assert.Equal(14, again.AlreadyThere);
        Assert.Equal(6, db.Count("Orders"));
    }

    [Fact]
    public void Restore_merges_into_customer_with_same_name_created_after_cleanup()
    {
        using var temp = new TempFolder();
        var db = Seed(temp);
        var (folder, record) = CleanupWithBackup(temp, db, Before2026All);
        Sql(db, "INSERT INTO Users(name, createdAt) VALUES (' dũng ', '2026-10-16T09:00:00')"); // tạo lại khách Dũng
        var newDung = Value(db, "SELECT id FROM Users WHERE trim(name) = 'dũng'");

        var result = new DataMaintenance(db).Restore(folder, record);

        Assert.Equal(1, result.Merged);
        Assert.Equal(0, result.AddedOf("Users"));
        Assert.Equal(1, Value(db, "SELECT COUNT(*) FROM Users WHERE lower(trim(name)) = 'dũng'")); // không thêm khách trùng
        Assert.Equal(newDung, Value(db, "SELECT userId FROM Orders WHERE id = 5"));
    }

    [Fact]
    public void Restore_gives_new_id_when_old_id_was_taken_and_moves_lines_with_it()
    {
        using var temp = new TempFolder();
        var db = Seed(temp);
        var (folder, record) = CleanupWithBackup(temp, db, Before2026All);
        // Như nhập lại backup điện thoại: đơn khác mang id 1 (ngày tạo khác).
        Sql(db, "INSERT INTO Orders(id, userId, status, deliveryDatetime, total, createdAt) VALUES (1, 1, 1, '2026-10-16T08:00:00', 7000, '2026-10-16T07:00:00')");

        var result = new DataMaintenance(db).Restore(folder, record);

        Assert.True(result.NewIds >= 1);
        Assert.Equal(7000, Value(db, "SELECT total FROM Orders WHERE id = 1")); // đơn đang có giữ nguyên
        var restoredId = Value(db, "SELECT id FROM Orders WHERE createdAt = '2024-03-09T00:00:00'");
        Assert.NotEqual(1, restoredId);
        Assert.Equal(2, Value(db, $"SELECT COUNT(*) FROM OrderItems WHERE orderId = {restoredId}")); // 2 dòng món theo đơn mới
        Assert.Equal(0, Value(db, "SELECT COUNT(*) FROM OrderItems WHERE orderId = 1"));
    }

    [Fact]
    public void Cleanup_without_extras_keeps_customers_and_products()
    {
        using var temp = new TempFolder();
        var db = Seed(temp);
        new DataMaintenance(db).Cleanup(new CleanupOptions(new DateOnly(2026, 1, 1), false, false));
        Assert.Equal(4, db.Count("Users"));
        Assert.Equal(3, db.Count("Products"));
        Assert.Equal(2, db.Count("Orders"));
    }

    [Fact]
    public void Empty_database_overview_and_no_cleanups()
    {
        using var temp = new TempFolder();
        var overview = new DataMaintenance(temp.NewDatabase()).Overview();
        Assert.Empty(overview.Years);
        Assert.Null(overview.FirstOrderDay);
        Assert.All(overview.Tables, t => Assert.Equal(0, t.Rows));
        Assert.Empty(DataMaintenance.ListCleanups(Path.Combine(temp.Path, "khong-co")));
    }
}
