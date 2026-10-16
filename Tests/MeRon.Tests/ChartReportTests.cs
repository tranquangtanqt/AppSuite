using MeRon.Core.Data;
using MeRon.Core.Reports;

namespace MeRon.Tests;

public class ChartReportTests
{
    /// <summary>
    /// Tháng 9/2026: An đặt lần đầu (thứ 3 1/9, 2 đơn 10k + 20k) và lại 15/9 (30k); Bình đặt lần đầu tháng 8 (5/8, 7k) và 15/9 (40k)
    /// → khách quay lại; Chi 20/9 Huỷ (không tính). Tháng 10: An 5/10 (thứ 2, 50k). Đơn không có ngày giao: bỏ qua.
    /// Món: Bánh bao (id 1) + món tự do "Trà" 2 dòng.
    /// </summary>
    private static MeRonDatabase Seed(TempFolder temp)
    {
        var db = temp.NewDatabase();
        using var connection = db.Open();
        foreach (var sql in new[]
        {
            "INSERT INTO Users(id, name) VALUES (1, 'An'), (2, 'Bình'), (3, 'Chi')",
            "INSERT INTO Products(id, name, price) VALUES (1, 'Bánh bao', 10000)",
            "INSERT INTO Orders(id, userId, status, deliveryDatetime, total) VALUES " +
            "(1, 1, 2, '2026-09-01T08:00:00.000000', 10000), (2, 1, 1, '2026-09-01 17:00:00', 20000), (3, 1, 2, '2026-09-15T08:00:00', 30000), " +
            "(4, 2, 2, '2026-08-05T08:00:00', 7000), (5, 2, 2, '2026-09-15T09:00:00', 40000), (6, 3, 3, '2026-09-20T08:00:00', 99000), " +
            "(7, 1, 2, '2026-10-05T08:00:00', 50000), (8, 2, 1, NULL, 1000)",
            "INSERT INTO OrderItems(id, orderId, productId, snapshotName, quantity, lineTotal) VALUES " +
            "(1, 1, 1, 'Bánh bao', 1, 10000), (2, 2, NULL, 'Trà ', 2, 20000), (3, 3, NULL, 'Trà', 3, 30000), (4, 5, 1, 'Bánh bao', 3, 30000), " +
            "(5, 6, 1, 'Bánh bao', 9, 99000)",
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        return db;
    }

    [Theory]
    [InlineData("2026-09-01", "2026-09-30", ChartBucket.Day)]
    [InlineData("2026-09-01", "2026-10-01", ChartBucket.Day)]   // 31 ngày
    [InlineData("2026-09-01", "2026-10-02", ChartBucket.Week)]
    [InlineData("2026-01-01", "2026-07-05", ChartBucket.Week)]  // 186 ngày
    [InlineData("2026-01-01", "2026-12-31", ChartBucket.Month)]
    public void Auto_bucket_by_range_length(string from, string to, ChartBucket expected) =>
        Assert.Equal(expected, ChartRepository.AutoBucket(DateOnly.Parse(from), DateOnly.Parse(to)));

    [Fact]
    public void Week_starts_monday_month_starts_first()
    {
        Assert.Equal(new DateOnly(2026, 9, 14), ChartRepository.BucketStart(new DateOnly(2026, 9, 20), ChartBucket.Week)); // CN → thứ 2
        Assert.Equal(new DateOnly(2026, 9, 14), ChartRepository.BucketStart(new DateOnly(2026, 9, 14), ChartBucket.Week));
        Assert.Equal(new DateOnly(2026, 9, 1), ChartRepository.BucketStart(new DateOnly(2026, 9, 20), ChartBucket.Month));
    }

    [Fact]
    public void Daily_sales_fill_empty_days_and_skip_cancelled()
    {
        using var temp = new TempFolder();
        var report = new ChartRepository(Seed(temp)).Build(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);

        Assert.Equal(ChartBucket.Day, report.Bucket);
        Assert.Equal(30, report.Sales.Count);
        Assert.Equal((2L, 30000L), (report.Sales[0].Orders, report.Sales[0].Revenue)); // 1/9: 2 đơn (cả 2 dạng chuỗi ngày)
        Assert.Equal((2L, 70000L), (report.Sales[14].Orders, report.Sales[14].Revenue)); // 15/9
        Assert.Equal(0, report.Sales[19].Orders); // 20/9: chỉ đơn Huỷ
        Assert.Equal((100000L, 4L, 2L, 25000L), (report.Current.Revenue, report.Current.Orders, report.Current.Customers, report.Current.AverageOrder));
        // kỳ trước = 2/8 → 31/8 (cùng 30 ngày): đơn Bình 5/8
        Assert.Equal((7000L, 1L), (report.Previous.Revenue, report.Previous.Orders));
    }

    [Fact]
    public void New_vs_returning_customers_use_first_order_ever()
    {
        using var temp = new TempFolder();
        var report = new ChartRepository(Seed(temp)).Build(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31), ChartBucket.Month);

        Assert.Equal([new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], report.Sales.Select(s => s.Start));
        Assert.Equal((1L, 1L), (report.CustomerMix[0].NewCustomers, report.CustomerMix[0].Returning)); // An mới, Bình quay lại
        Assert.Equal((0L, 1L), (report.CustomerMix[1].NewCustomers, report.CustomerMix[1].Returning)); // An quay lại
    }

    [Fact]
    public void Weekdays_monday_first_and_top_products_group_free_items_by_name()
    {
        using var temp = new TempFolder();
        var report = new ChartRepository(Seed(temp)).Build(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31), null);

        Assert.Equal(DayOfWeek.Monday, report.Weekdays[0].Day);
        Assert.Equal(DayOfWeek.Sunday, report.Weekdays[6].Day);
        Assert.Equal((1L, 50000L), (report.Weekdays[0].Orders, report.Weekdays[0].Revenue)); // 5/10 thứ 2
        Assert.Equal(4, report.Weekdays[1].Orders); // 1/9 thứ 3 (2 đơn), 15/9 thứ 3 (2 đơn)

        Assert.Equal(["Trà", "Bánh bao"], report.TopProducts.Select(p => p.Name)); // Trà (tự do, 2 dòng) 50k > Bánh bao 40k; đơn Huỷ không tính
        Assert.Equal((5L, 50000L), (report.TopProducts[0].Quantity, report.TopProducts[0].Amount));
        Assert.Null(report.TopProducts[0].ProductId);
        Assert.Equal((4L, 40000L), (report.TopProducts[1].Quantity, report.TopProducts[1].Amount));
    }

    [Fact]
    public void All_dates_when_range_is_open_and_empty_database()
    {
        using var temp = new TempFolder();
        var report = new ChartRepository(Seed(temp)).Build(null, null, null);
        Assert.Equal((new DateOnly(2026, 8, 5), new DateOnly(2026, 10, 5)), (report.From, report.To));
        Assert.Equal(ChartBucket.Week, report.Bucket);

        using var other = new TempFolder();
        var empty = new ChartRepository(other.NewDatabase()).Build(null, null, null);
        Assert.True(empty.IsEmpty);
        Assert.Single(empty.Sales);
    }
}
