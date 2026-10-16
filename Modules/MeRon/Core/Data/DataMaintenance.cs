using System.Globalization;
using MeRon.Core.Models;
using Microsoft.Data.Sqlite;

namespace MeRon.Core.Data;

public sealed record TableRowCount(string Name, string Title, long Rows);

/// <summary>Đơn / phiếu nhập trong 1 năm (<paramref name="Month"/> = 0) hoặc 1 tháng. <paramref name="Sales"/> không tính đơn Huỷ.</summary>
public sealed record PeriodStats(int Year, int Month, long Orders, long CancelledOrders, long Sales, long Purchases, long PurchaseAmount);

public sealed record DataOverview(
    long FileBytes,
    IReadOnlyList<TableRowCount> Tables,
    DateOnly? FirstOrderDay,
    DateOnly? LastOrderDay,
    DateOnly? FirstPurchaseDay,
    DateOnly? LastPurchaseDay,
    IReadOnlyList<PeriodStats> Years,
    long CustomersWithoutOrders,
    long ProductsNeverSold,
    long EmptyCategories);

/// <summary>Dọn dữ liệu cũ: xoá đơn hàng + phiếu nhập có ngày trước <paramref name="Before"/>; tuỳ chọn xoá thêm khách hàng
/// không còn đơn nào và món không còn dòng đơn nào (chỉ những khách / món tạo trước mốc - vừa thêm thì giữ).</summary>
public sealed record CleanupOptions(DateOnly Before, bool DeleteCustomersWithoutOrders, bool DeleteProductsNeverSold);

/// <summary>Số dòng sẽ xoá (xem trước) hoặc đã xoá.</summary>
public sealed record CleanupPlan(long Orders, long OrderItems, long OrderAmount, long Purchases, long PurchaseItems, long PurchaseAmount,
    long Customers, long Products)
{
    public bool IsEmpty => Orders + Purchases + Customers + Products == 0;
}

/// <summary>
/// Trang "Thông tin dữ liệu": tổng quan dữ liệu đang có (số dòng từng bảng, khoảng ngày, theo năm / tháng, dữ liệu thừa)
/// và dọn dữ liệu cũ. Ngày của đơn = ngày giao (không có thì ngày tạo); của phiếu nhập = ngày nhập (không có thì ngày
/// tạo) - so bằng substr(…, 1, 10) đúng với cả 2 dạng chuỗi ngày của điện thoại.
/// </summary>
public sealed class DataMaintenance(MeRonDatabase database)
{
    private const string OrderDay = "substr(COALESCE(NULLIF(O.deliveryDatetime, ''), O.createdAt), 1, 10)";
    private const string PurchaseDay = "substr(COALESCE(NULLIF(P.date, ''), P.createdAt), 1, 10)";
    private static readonly long Cancelled = (long)OrderStatus.Cancelled;

    public DataOverview Overview()
    {
        using var connection = database.Open();
        var tables = Schema.BackupTables.Select(t => new TableRowCount(t.Name, t.Title, Scalar(connection, $"SELECT COUNT(*) FROM {t.Name}"))).ToList();
        var (firstOrder, lastOrder) = Span(connection, $"SELECT MIN({OrderDay}), MAX({OrderDay}) FROM Orders AS O");
        var (firstPurchase, lastPurchase) = Span(connection, $"SELECT MIN({PurchaseDay}), MAX({PurchaseDay}) FROM Purchases AS P");
        return new DataOverview(
            File.Exists(database.Path) ? new FileInfo(database.Path).Length : 0,
            tables,
            firstOrder, lastOrder, firstPurchase, lastPurchase,
            Periods(connection, year: null),
            Scalar(connection, "SELECT COUNT(*) FROM Users AS U WHERE NOT EXISTS (SELECT 1 FROM Orders AS O WHERE O.userId = U.id)"),
            Scalar(connection, "SELECT COUNT(*) FROM Products AS P WHERE NOT EXISTS (SELECT 1 FROM OrderItems AS I WHERE I.productId = P.id)"),
            Scalar(connection, "SELECT COUNT(*) FROM Categories AS C WHERE NOT EXISTS (SELECT 1 FROM Products AS P WHERE P.categoryId = C.id)"));
    }

    /// <summary>12 tháng của 1 năm (tháng không có gì vẫn có dòng 0) - bấm 1 năm trong bảng để xem.</summary>
    public IReadOnlyList<PeriodStats> Months(int year)
    {
        using var connection = database.Open();
        var found = Periods(connection, year).ToDictionary(p => p.Month);
        return Enumerable.Range(1, 12).Select(m => found.GetValueOrDefault(m) ?? new PeriodStats(year, m, 0, 0, 0, 0, 0)).ToList();
    }

    public CleanupPlan Plan(CleanupOptions options)
    {
        using var connection = database.Open();
        return Plan(connection, options);
    }

    /// <summary>
    /// Xoá theo <paramref name="options"/> trong 1 transaction (lỗi giữa chừng = không xoá gì). Chép bản sao an toàn .db
    /// trước (Backups\me_ron_truoc-don_…). Trigger đếm "thay đổi chưa xuất" đếm các dòng bị xoá - nhắc xuất sang điện thoại.
    /// </summary>
    public CleanupPlan Cleanup(CleanupOptions options)
    {
        database.MakeSafetyCopy("truoc-don");
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var plan = Plan(connection, options);
        var before = Day(options.Before);
        Run(connection, $"DELETE FROM OrderItems WHERE orderId IN (SELECT O.id FROM Orders AS O WHERE {OrderDay} < $before)", before);
        Run(connection, $"DELETE FROM Orders AS O WHERE {OrderDay} < $before", before);
        Run(connection, $"DELETE FROM PurchaseItems WHERE purchaseId IN (SELECT P.id FROM Purchases AS P WHERE {PurchaseDay} < $before)", before);
        Run(connection, $"DELETE FROM Purchases AS P WHERE {PurchaseDay} < $before", before);
        // Sau khi xoá đơn cũ: "không còn đơn" / "không còn dòng đơn" tính trên phần còn lại.
        if (options.DeleteCustomersWithoutOrders)
        {
            Run(connection, $"DELETE FROM Users AS U WHERE {OrphanCustomer}", before);
        }
        if (options.DeleteProductsNeverSold)
        {
            Run(connection, $"DELETE FROM Products AS P WHERE {UnsoldProduct}", before);
        }
        transaction.Commit();
        return plan;
    }

    /// <summary>Khách tạo trước mốc và không còn đơn nào từ mốc trở đi (đơn cũ hơn sẽ bị xoá cùng lúc).</summary>
    private const string OrphanCustomer =
        $"substr(COALESCE(U.createdAt, ''), 1, 10) < $before AND NOT EXISTS (SELECT 1 FROM Orders AS O WHERE O.userId = U.id AND {OrderDay} >= $before)";

    /// <summary>Món tạo trước mốc và không còn dòng đơn nào của đơn từ mốc trở đi.</summary>
    private const string UnsoldProduct =
        $"substr(COALESCE(P.createdAt, ''), 1, 10) < $before AND NOT EXISTS (SELECT 1 FROM OrderItems AS I JOIN Orders AS O ON O.id = I.orderId WHERE I.productId = P.id AND {OrderDay} >= $before)";

    private static CleanupPlan Plan(SqliteConnection connection, CleanupOptions options)
    {
        var before = Day(options.Before);
        long Count(string sql) => Scalar(connection, sql, before);
        return new CleanupPlan(
            Count($"SELECT COUNT(*) FROM Orders AS O WHERE {OrderDay} < $before"),
            Count($"SELECT COUNT(*) FROM OrderItems WHERE orderId IN (SELECT O.id FROM Orders AS O WHERE {OrderDay} < $before)"),
            Count($"SELECT COALESCE(SUM(O.total), 0) FROM Orders AS O WHERE {OrderDay} < $before AND O.status <> {Cancelled}"),
            Count($"SELECT COUNT(*) FROM Purchases AS P WHERE {PurchaseDay} < $before"),
            Count($"SELECT COUNT(*) FROM PurchaseItems WHERE purchaseId IN (SELECT P.id FROM Purchases AS P WHERE {PurchaseDay} < $before)"),
            Count($"SELECT COALESCE(SUM(P.total), 0) FROM Purchases AS P WHERE {PurchaseDay} < $before"),
            options.DeleteCustomersWithoutOrders ? Count($"SELECT COUNT(*) FROM Users AS U WHERE {OrphanCustomer}") : 0,
            options.DeleteProductsNeverSold ? Count($"SELECT COUNT(*) FROM Products AS P WHERE {UnsoldProduct}") : 0);
    }

    /// <summary>Theo năm (<paramref name="year"/> null) hoặc theo tháng của 1 năm; mới nhất trước.</summary>
    private static List<PeriodStats> Periods(SqliteConnection connection, int? year)
    {
        // Gộp đơn và phiếu nhập theo cùng khoá kỳ: "yyyy" hoặc "yyyy-MM".
        var length = year is null ? 4 : 7;
        var filter = year is null ? string.Empty : "WHERE substr(k, 1, 4) = $year";
        var sql = $"""
            SELECT k, SUM(orders), SUM(cancelled), SUM(sales), SUM(purchases), SUM(amount) FROM (
                SELECT substr({OrderDay}, 1, {length}) AS k, COUNT(*) AS orders,
                       SUM(CASE WHEN O.status = {Cancelled} THEN 1 ELSE 0 END) AS cancelled,
                       SUM(CASE WHEN O.status = {Cancelled} THEN 0 ELSE O.total END) AS sales, 0 AS purchases, 0 AS amount
                FROM Orders AS O GROUP BY k
                UNION ALL
                SELECT substr({PurchaseDay}, 1, {length}), 0, 0, 0, COUNT(*), SUM(P.total) FROM Purchases AS P GROUP BY 1
            ) {filter} GROUP BY k ORDER BY k DESC
            """;
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$year", (year ?? 0).ToString("D4", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        var result = new List<PeriodStats>();
        while (reader.Read())
        {
            var key = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (!int.TryParse(key.AsSpan(0, Math.Min(4, key.Length)), NumberStyles.None, CultureInfo.InvariantCulture, out var y))
            {
                continue; // đơn không có ngày nào (dữ liệu hỏng) - không xếp vào năm nào
            }
            var month = key.Length >= 7 && int.TryParse(key.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : 0;
            result.Add(new PeriodStats(y, month, Long(reader, 1), Long(reader, 2), Long(reader, 3), Long(reader, 4), Long(reader, 5)));
        }
        return result;
    }

    private static long Long(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? 0 : reader.GetInt64(i);

    private static string Day(DateOnly value) => DbDate.Day(value);

    private static (DateOnly?, DateOnly?) Span(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        reader.Read();
        DateOnly? Read(int i) => !reader.IsDBNull(i) && DateOnly.TryParseExact(reader.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day) ? day : null;
        return (Read(0), Read(1));
    }

    private static long Scalar(SqliteConnection connection, string sql, string? before = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (before is not null)
        {
            command.Parameters.AddWithValue("$before", before);
        }
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private static void Run(SqliteConnection connection, string sql, string before)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$before", before);
        command.ExecuteNonQuery();
    }
}
