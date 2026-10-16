using System.Globalization;
using MeRon.Core.Data;
using MeRon.Core.Models;
using Microsoft.Data.Sqlite;

namespace MeRon.Core.Reports;

/// <summary>Đơn vị gom của trục thời gian.</summary>
public enum ChartBucket
{
    Day,
    Week,
    Month,
}

/// <summary>1 cột theo thời gian: kỳ bắt đầu từ <paramref name="Start"/>.</summary>
public sealed record SalesPoint(DateOnly Start, long Orders, long Revenue);

public sealed record WeekdayPoint(DayOfWeek Day, long Orders, long Revenue);

/// <summary>Khách đặt trong kỳ: <paramref name="NewCustomers"/> = lần đầu tiên đặt (tính trên mọi đơn từ trước tới nay)
/// rơi vào kỳ này, <paramref name="Returning"/> = đã từng đặt trước kỳ.</summary>
public sealed record CustomerMixPoint(DateOnly Start, long NewCustomers, long Returning);

public sealed record KpiSet(long Revenue, long Orders, long Customers)
{
    public long AverageOrder => Orders == 0 ? 0 : (long)Math.Round((double)Revenue / Orders);
}

public sealed record ChartReport(
    ChartBucket Bucket,
    DateOnly From,
    DateOnly To,
    KpiSet Current,
    KpiSet Previous,
    IReadOnlyList<SalesPoint> Sales,
    IReadOnlyList<WeekdayPoint> Weekdays,
    IReadOnlyList<CustomerMixPoint> CustomerMix,
    IReadOnlyList<ProductSummary> TopProducts)
{
    public bool IsEmpty => Current.Orders == 0;
}

/// <summary>
/// Số liệu trang Biểu đồ - chỉ từ đơn hàng (không dùng Mua hàng), không tính đơn Huỷ, theo ngày giao như các báo cáo khác.
/// Kỳ trước = khoảng ngày cùng độ dài ngay trước <c>From</c>.
/// </summary>
public sealed class ChartRepository(MeRonDatabase database)
{
    private static readonly long Cancelled = (long)OrderStatus.Cancelled;
    private const string Day = "substr(O.deliveryDatetime, 1, 10)";

    /// <summary>Tự chọn đơn vị: ≤ 31 ngày theo ngày, ≤ 6 tháng (186 ngày) theo tuần, dài hơn theo tháng.</summary>
    public static ChartBucket AutoBucket(DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        return days <= 31 ? ChartBucket.Day : days <= 186 ? ChartBucket.Week : ChartBucket.Month;
    }

    /// <summary>Ngày đầu kỳ chứa <paramref name="day"/>: tuần bắt đầu thứ 2, tháng bắt đầu ngày 1.</summary>
    public static DateOnly BucketStart(DateOnly day, ChartBucket bucket) => bucket switch
    {
        ChartBucket.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
        ChartBucket.Month => new DateOnly(day.Year, day.Month, 1),
        _ => day,
    };

    public static DateOnly NextBucket(DateOnly start, ChartBucket bucket) => bucket switch
    {
        ChartBucket.Week => start.AddDays(7),
        ChartBucket.Month => start.AddMonths(1),
        _ => start.AddDays(1),
    };

    /// <param name="from">null = từ đơn đầu tiên.</param>
    /// <param name="to">null = tới đơn cuối cùng.</param>
    /// <param name="bucket">null = tự chọn theo độ dài khoảng ngày.</param>
    public ChartReport Build(DateOnly? from, DateOnly? to, ChartBucket? bucket, int topProducts = 10)
    {
        using var connection = database.Open();
        var orders = LoadOrders(connection);
        var first = orders.Count > 0 ? orders.Min(o => o.Day) : DateOnly.FromDateTime(DateTime.Today);
        var last = orders.Count > 0 ? orders.Max(o => o.Day) : first;
        var start = from ?? first;
        var end = to ?? last;
        if (end < start)
        {
            (start, end) = (end, start);
        }
        var unit = bucket ?? AutoBucket(start, end);
        var length = end.DayNumber - start.DayNumber + 1;
        var previousEnd = start.AddDays(-1);
        var previousStart = previousEnd.AddDays(-(length - 1));

        var inRange = orders.Where(o => o.Day >= start && o.Day <= end).ToList();
        var inPrevious = orders.Where(o => o.Day >= previousStart && o.Day <= previousEnd).ToList();

        // Theo thời gian: mọi kỳ từ đầu tới cuối khoảng, kỳ không có đơn = 0 (cột trống vẫn hiện).
        var sales = new List<SalesPoint>();
        var mix = new List<CustomerMixPoint>();
        var firstDayOf = orders.Where(o => o.UserId is not null).GroupBy(o => o.UserId!.Value).ToDictionary(g => g.Key, g => g.Min(o => o.Day));
        var byBucket = inRange.GroupBy(o => BucketStart(o.Day, unit)).ToDictionary(g => g.Key, g => g.ToList());
        for (var b = BucketStart(start, unit); b <= end; b = NextBucket(b, unit))
        {
            var list = byBucket.GetValueOrDefault(b) ?? [];
            sales.Add(new SalesPoint(b, list.Count, list.Sum(o => o.Total)));
            var bucketEnd = NextBucket(b, unit);
            var customers = list.Where(o => o.UserId is not null).Select(o => o.UserId!.Value).Distinct().ToList();
            var newOnes = customers.Count(c => firstDayOf[c] >= b && firstDayOf[c] < bucketEnd);
            mix.Add(new CustomerMixPoint(b, newOnes, customers.Count - newOnes));
        }

        // Thứ 2 → Chủ nhật
        var weekdays = Enumerable.Range(0, 7).Select(i => (DayOfWeek)((i + 1) % 7))
            .Select(d => new WeekdayPoint(d, inRange.Count(o => o.Day.DayOfWeek == d), inRange.Where(o => o.Day.DayOfWeek == d).Sum(o => o.Total)))
            .ToList();

        return new ChartReport(unit, start, end, Kpi(inRange), Kpi(inPrevious), sales, weekdays, mix,
            TopProducts(connection, start, end, topProducts));
    }

    private static KpiSet Kpi(List<OrderPoint> orders) =>
        new(orders.Sum(o => o.Total), orders.Count, orders.Where(o => o.UserId is not null).Select(o => o.UserId).Distinct().Count());

    private sealed record OrderPoint(DateOnly Day, long? UserId, long Total);

    /// <summary>Mọi đơn không Huỷ có ngày giao (nhẹ: vài nghìn đơn) - gom trong bộ nhớ cho dễ kiểm tra.</summary>
    private static List<OrderPoint> LoadOrders(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Day}, O.userId, COALESCE(O.total, 0) FROM Orders AS O WHERE O.status <> {Cancelled} AND O.deliveryDatetime IS NOT NULL AND O.deliveryDatetime <> ''";
        using var reader = command.ExecuteReader();
        var result = new List<OrderPoint>();
        while (reader.Read())
        {
            if (DateOnly.TryParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                result.Add(new OrderPoint(day, reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.GetInt64(2)));
            }
        }
        return result;
    }

    /// <summary>Món bán nhiều tiền nhất trong khoảng (món tự do gộp theo tên).</summary>
    private static List<ProductSummary> TopProducts(SqliteConnection connection, DateOnly from, DateOnly to, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT I.productId, COALESCE(P.name, TRIM(I.snapshotName), '(không tên)'), SUM(COALESCE(I.quantity, 0)), SUM(COALESCE(I.lineTotal, 0))
            FROM OrderItems AS I JOIN Orders AS O ON O.id = I.orderId LEFT JOIN Products AS P ON P.id = I.productId
            WHERE O.status <> {Cancelled} AND {Day} >= $from AND {Day} <= $to
            GROUP BY COALESCE(CAST(I.productId AS TEXT), 'free:' || TRIM(I.snapshotName))
            ORDER BY 4 DESC, 3 DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$from", DbDate.Day(from));
        command.Parameters.AddWithValue("$to", DbDate.Day(to));
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var result = new List<ProductSummary>();
        while (reader.Read())
        {
            result.Add(new ProductSummary(reader.IsDBNull(0) ? null : reader.GetInt64(0), reader.GetString(1).Trim(), reader.GetInt64(2), reader.GetInt64(3)));
        }
        return result;
    }
}
