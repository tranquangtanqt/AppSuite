using MeRon.Core.Data;
using MeRon.Core.Models;
using Microsoft.Data.Sqlite;

namespace MeRon.Core.Reports;

/// <summary>Số đơn + thành tiền của 1 trạng thái.</summary>
public sealed record StatusSummary(OrderStatus Status, long Count, long Amount);

/// <summary>1 món trong báo cáo: <see cref="ProductId"/> null = món tự do (gộp theo tên).</summary>
public sealed record ProductSummary(long? ProductId, string Name, long Quantity, long Amount);

/// <summary>1 khách trong "Chi tiết đơn hàng" của báo cáo Theo đơn hàng.</summary>
public sealed record CustomerSummary(long? UserId, string Name, long OrderCount, long Amount, DateTime? FirstDelivery, DateTime? LastDelivery);

/// <summary>1 khoản hàng (gộp theo tên) trong phần Mua hàng của báo cáo Tổng hợp.</summary>
public sealed record PurchaseItemSummary(string Name, long Count, long Amount);

public sealed record OrderReport(
    IReadOnlyList<StatusSummary> ByStatus,
    long OrderCount,
    long Amount,
    IReadOnlyList<ProductSummary> Products,
    IReadOnlyList<CustomerSummary> Customers);

public sealed record ProductReport(long TotalQuantity, IReadOnlyList<ProductSummary> Products, IReadOnlyList<Order> Orders);

public sealed record SummaryReport(
    long OrderCount,
    long OrderAmount,
    IReadOnlyList<ProductSummary> Products,
    long PurchaseCount,
    long PurchaseAmount,
    IReadOnlyList<PurchaseItemSummary> PurchaseItems);

/// <summary>
/// 4 báo cáo của điện thoại (order_local_datasource_impl.dart: getOrderStatusSummary, getOrderProductSummary,
/// getCustomerSummaryReportOrder, getProductSummaryReportProduct, getTopCustomers; report_summary_notifier.dart).
/// Ngày tính theo NGÀY giao (cả 2 đầu). Đơn Huỷ KHÔNG tính vào tổng tiền / số món / xếp hạng (như hướng dẫn của điện
/// thoại); khác điện thoại có chủ đích: "Tổng số đơn" của báo cáo Theo đơn hàng và cả báo cáo Tổng hợp cũng bỏ đơn Huỷ
/// (điện thoại lẫn đơn huỷ vào 2 chỗ này). Món gộp theo productId (tên = tên món hiện tại), món tự do gộp theo tên.
/// </summary>
public sealed class ReportRepository(MeRonDatabase database, OrderRepository orders)
{
    private const long Cancelled = (long)OrderStatus.Cancelled;

    /// <summary>Báo cáo Theo đơn hàng: lọc trạng thái (null = tất cả), khoảng ngày, khách.</summary>
    public OrderReport OrderReport(OrderFilter filter)
    {
        var (where, parameters) = Where(filter.From, filter.To, filter.Status, filter.UserId);
        using var connection = database.Open();

        var byStatus = Query(connection, $"SELECT O.status, COUNT(*), COALESCE(SUM(O.total), 0) FROM Orders AS O WHERE {where} GROUP BY O.status",
                r => new StatusSummary(OrderStatusText.FromValue(r.GetInt64(0)), r.GetInt64(1), r.GetInt64(2)), parameters)
            .GroupBy(s => s.Status) // giá trị trạng thái lạ gộp về Đã lên đơn
            .Select(g => new StatusSummary(g.Key, g.Sum(s => s.Count), g.Sum(s => s.Amount)))
            .ToList();
        var active = byStatus.Where(s => s.Status != OrderStatus.Cancelled).ToList();

        var products = ProductSummaries(connection, $"{where} AND O.status <> {Cancelled}", parameters);

        var customers = Query(connection,
            "SELECT O.userId, U.name, COUNT(*), COALESCE(SUM(O.total), 0), MIN(julianday(replace(O.deliveryDatetime, 'T', ' '))), " +
            "MAX(julianday(replace(O.deliveryDatetime, 'T', ' '))) FROM Orders AS O LEFT JOIN Users AS U ON O.userId = U.id " +
            $"WHERE {where} GROUP BY O.userId ORDER BY 4 DESC",
            r => new CustomerSummary(r.IsDBNull(0) ? null : r.GetInt64(0), r.IsDBNull(1) ? "(không có khách)" : r.GetString(1),
                r.GetInt64(2), r.GetInt64(3), Julian(r, 4), Julian(r, 5)), parameters);

        return new OrderReport(byStatus, active.Sum(s => s.Count), active.Sum(s => s.Amount), products, customers);
    }

    /// <summary>Báo cáo Theo món ăn: khoảng ngày + (tuỳ chọn) 1 món trong danh mục. Đơn liên quan chỉ hiện dòng của món đó.</summary>
    public ProductReport ProductReport(DateOnly? from, DateOnly? to, long? productId)
    {
        var (where, parameters) = Where(from, to, null, null);
        where += $" AND O.status <> {Cancelled}";
        using var connection = database.Open();
        var productWhere = where;
        var productParameters = parameters.ToList();
        if (productId is not null)
        {
            productWhere += " AND D.productId = $productId";
            productParameters.Add(("$productId", productId));
        }
        var products = ProductSummaries(connection, productWhere, productParameters);

        var related = orders.Query(new OrderFilter(null, from, to))
            .Where(o => o.Status != OrderStatus.Cancelled)
            .Select(o => productId is null ? o : o with { Items = o.Items.Where(i => i.ProductId == productId).ToList() })
            .Where(o => productId is null || o.Items.Count > 0)
            .ToList();
        return new ProductReport(products.Sum(p => p.Quantity), products, related);
    }

    /// <summary>Báo cáo Tổng hợp: đơn hàng (số đơn, thành tiền, theo món) + mua hàng (số phiếu, thành tiền, theo khoản).</summary>
    public SummaryReport SummaryReport(DateOnly? from, DateOnly? to)
    {
        var (where, parameters) = Where(from, to, null, null);
        where += $" AND O.status <> {Cancelled}";
        using var connection = database.Open();
        var (count, amount) = Query(connection, $"SELECT COUNT(*), COALESCE(SUM(O.total), 0) FROM Orders AS O WHERE {where}",
            r => (r.GetInt64(0), r.GetInt64(1)), parameters)[0];
        var products = ProductSummaries(connection, where, parameters);

        var purchaseWhere = new List<string> { "1 = 1" };
        var purchaseParameters = new List<(string, object?)>();
        if (from is { } f)
        {
            purchaseWhere.Add("substr(P.date, 1, 10) >= $from");
            purchaseParameters.Add(("$from", DbDate.Day(f)));
        }
        if (to is { } t)
        {
            purchaseWhere.Add("substr(P.date, 1, 10) <= $to");
            purchaseParameters.Add(("$to", DbDate.Day(t)));
        }
        var pw = string.Join(" AND ", purchaseWhere);
        var (purchaseCount, purchaseAmount) = Query(connection, $"SELECT COUNT(*), COALESCE(SUM(P.total), 0) FROM Purchases AS P WHERE {pw}",
            r => (r.GetInt64(0), r.GetInt64(1)), purchaseParameters)[0];
        var items = Query(connection,
            "SELECT TRIM(I.name), COUNT(*), COALESCE(SUM(I.price), 0) FROM PurchaseItems AS I JOIN Purchases AS P ON P.id = I.purchaseId " +
            $"WHERE {pw} GROUP BY TRIM(I.name) COLLATE NOCASE ORDER BY 3 DESC",
            r => new PurchaseItemSummary(r.IsDBNull(0) ? string.Empty : r.GetString(0), r.GetInt64(1), r.GetInt64(2)), purchaseParameters);

        return new SummaryReport(count, amount, products, purchaseCount, purchaseAmount, items);
    }

    /// <summary>Khách hàng tiềm năng: xếp hạng theo tổng tiền đã đặt (bỏ đơn Huỷ), tối đa <paramref name="limit"/> khách.</summary>
    public IReadOnlyList<CustomerSummary> TopCustomers(DateOnly? from, DateOnly? to, int limit = 100)
    {
        var (where, parameters) = Where(from, to, null, null);
        using var connection = database.Open();
        return Query(connection,
            "SELECT O.userId, U.name, COUNT(*), COALESCE(SUM(O.total), 0), MIN(julianday(replace(O.deliveryDatetime, 'T', ' '))), " +
            "MAX(julianday(replace(O.deliveryDatetime, 'T', ' '))) FROM Orders AS O JOIN Users AS U ON O.userId = U.id " +
            $"WHERE {where} AND O.status <> {Cancelled} GROUP BY O.userId ORDER BY 4 DESC, 3 DESC LIMIT {limit}",
            r => new CustomerSummary(r.GetInt64(0), r.IsDBNull(1) ? "?" : r.GetString(1), r.GetInt64(2), r.GetInt64(3), Julian(r, 4), Julian(r, 5)),
            parameters);
    }

    /// <summary>Khách lâu không đặt (xem <see cref="Reports.LapsedCustomers"/>): mọi đơn không huỷ có ngày giao, mọi thời gian.</summary>
    public IReadOnlyList<LapsedCustomer> LapsedCustomers(DateOnly today, int minDays, int minOrders)
    {
        var where = $"O.userId IS NOT NULL AND O.status <> {Cancelled} AND length(O.deliveryDatetime) >= 10";
        using var connection = database.Open();
        var orders = Query(connection,
            $"SELECT O.userId, O.id, substr(O.deliveryDatetime, 1, 10), O.total FROM Orders AS O WHERE {where}",
            r => (r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? 0 : r.GetInt64(3)), [])
            .Select(o => DateOnly.TryParseExact(o.Item3, "yyyy-MM-dd", out var day) ? new CustomerOrderPoint(o.Item1, o.Item2, day, o.Item4) : null)
            .OfType<CustomerOrderPoint>()
            .ToList();
        var products = Query(connection,
            "SELECT O.userId, COALESCE(MAX(P.name), MIN(D.snapshotName)), COALESCE(SUM(D.quantity), 0) " +
            "FROM Orders AS O JOIN OrderItems AS D ON O.id = D.orderId LEFT JOIN Products AS P ON P.id = D.productId " +
            $"WHERE {where} GROUP BY O.userId, COALESCE(CAST(D.productId AS TEXT), 'name:' || D.snapshotName)",
            r => new CustomerProductQuantity(r.GetInt64(0), r.IsDBNull(1) ? string.Empty : r.GetString(1), r.GetInt64(2)), []);
        var customers = Query(connection, "SELECT id, name, address, phone, note FROM Users",
                r => new Customer(r.GetInt64(0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4)), [])
            .ToDictionary(c => c.Id);
        return Reports.LapsedCustomers.Build(orders, products, customers, today, minDays, minOrders);
    }

    /// <summary>Công nợ (xem <see cref="Reports.CustomerDebts"/>): đơn Đã lên đơn gom theo khách. <paramref name="includeFuture"/> = false
    /// → chỉ đơn có ngày giao ≤ hôm nay (cùng điều kiện với bộ lọc To = hôm nay của màn Chi tiết).</summary>
    public IReadOnlyList<CustomerDebt> CustomerDebts(DateOnly today, bool includeFuture)
    {
        var where = $"O.status = {(long)OrderStatus.Shipping}" + (includeFuture ? "" : " AND substr(O.deliveryDatetime, 1, 10) <= $today");
        using var connection = database.Open();
        var orders = Query(connection,
            $"SELECT O.userId, O.id, substr(O.deliveryDatetime, 1, 10), O.total FROM Orders AS O WHERE {where}",
            r => new UnpaidOrderPoint(r.IsDBNull(0) ? null : r.GetInt64(0), r.GetInt64(1),
                !r.IsDBNull(2) && DateOnly.TryParseExact(r.GetString(2), "yyyy-MM-dd", out var day) ? day : null,
                r.IsDBNull(3) ? 0 : r.GetInt64(3)),
            [("$today", DbDate.Day(today))]);
        var customers = Query(connection, "SELECT id, name, address, phone, note FROM Users",
                r => new Customer(r.GetInt64(0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4)), [])
            .ToDictionary(c => c.Id);
        return Reports.CustomerDebts.Build(orders, customers, today);
    }

    // ---- tiện ích ----

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>Tổng theo món (đơn O + dòng D đã lọc bởi <paramref name="where"/>), số lượng giảm dần.</summary>
    private static List<ProductSummary> ProductSummaries(SqliteConnection connection, string where, IReadOnlyList<(string, object?)> parameters) =>
        Query(connection,
            "SELECT D.productId, COALESCE(MAX(P.name), MIN(D.snapshotName)), COALESCE(SUM(D.quantity), 0), COALESCE(SUM(D.lineTotal), 0) " +
            "FROM Orders AS O JOIN OrderItems AS D ON O.id = D.orderId LEFT JOIN Products AS P ON P.id = D.productId " +
            $"WHERE {where} GROUP BY COALESCE(CAST(D.productId AS TEXT), 'name:' || D.snapshotName) ORDER BY 3 DESC, 4 DESC",
            r => new ProductSummary(r.IsDBNull(0) ? null : r.GetInt64(0), r.IsDBNull(1) ? string.Empty : r.GetString(1), r.GetInt64(2), r.GetInt64(3)),
            parameters);

    private static (string Where, List<(string, object?)> Parameters) Where(DateOnly? from, DateOnly? to, OrderStatus? status, long? userId)
    {
        var where = new List<string> { "1 = 1" };
        var parameters = new List<(string, object?)>();
        if (from is { } f)
        {
            where.Add("substr(O.deliveryDatetime, 1, 10) >= $from");
            parameters.Add(("$from", DbDate.Day(f)));
        }
        if (to is { } t)
        {
            where.Add("substr(O.deliveryDatetime, 1, 10) <= $to");
            parameters.Add(("$to", DbDate.Day(t)));
        }
        if (status is { } s)
        {
            where.Add("O.status = $status");
            parameters.Add(("$status", (long)s));
        }
        if (userId is { } u)
        {
            where.Add("O.userId = $userId");
            parameters.Add(("$userId", u));
        }
        return (string.Join(" AND ", where), parameters);
    }

    private static DateTime? Julian(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateTime.FromOADate(r.GetDouble(i) - 2415018.5);

    private static List<T> Query<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map, IEnumerable<(string Name, object? Value)> parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        using var r = command.ExecuteReader();
        var list = new List<T>();
        while (r.Read())
        {
            list.Add(map(r));
        }
        return list;
    }
}
