using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.Core.Reports;

namespace MeRon.Tests;

/// <summary>Theo món ăn: ▲▼ so với kỳ trước + bảng món × thứ (2026-11-13).</summary>
public class ProductTrendsTests
{
    private static ProductSummary P(long? id, string name, long quantity) => new(id, name, quantity, quantity * 10000);

    [Fact]
    public void Previous_period_has_the_same_length_right_before()
    {
        Assert.Equal((new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 9)),
            ProductTrends.PreviousPeriod(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 16)));
        Assert.Equal((new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12)),
            ProductTrends.PreviousPeriod(new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 13)));
        Assert.Null(ProductTrends.PreviousPeriod(null, new DateOnly(2026, 11, 13)));
        Assert.Null(ProductTrends.PreviousPeriod(null, null));
    }

    [Fact]
    public void Compare_marks_up_down_same_new_and_gone_products()
    {
        var trends = ProductTrends.Compare(
            [P(1, "Bánh bao", 42), P(2, "Xôi", 10), P(3, "Chè", 20), P(null, "flan", 5), P(4, "Sữa chua", 8)],
            [P(1, "Bánh bao", 33), P(2, "Xôi", 20), P(3, "Chè", 20), P(5, "Cháo", 7), P(null, "Flan", 4), P(6, "Bún", 12)]);

        Assert.Equal(["Bánh bao ▲ 27%", "Xôi ▼ 50%", "Chè bằng", "flan mới", "Sữa chua mới", "Bún ▼ 100%", "Cháo ▼ 100%", "Flan ▼ 100%"], // món tự do: đúng tên như bảng gộp
            trends.Select(t => $"{t.Current.Name} {t.Text}"));
        var gone = trends[5];
        Assert.Equal((TrendKind.Gone, 0L, 12L), (gone.Kind, gone.Current.Quantity, gone.PreviousQuantity!.Value));
        Assert.Equal(33, trends[0].PreviousQuantity);
    }

    [Fact]
    public void Compare_without_previous_period_leaves_the_column_empty()
    {
        var trends = ProductTrends.Compare([P(1, "Bánh bao", 3)], null);
        Assert.Equal((TrendKind.None, string.Empty), (trends[0].Kind, trends[0].Text));
    }

    [Fact]
    public void Weekday_table_averages_by_how_many_times_each_weekday_occurs()
    {
        // 03/11/2026 là thứ 3. Khoảng 03/11 – 12/11 (10 ngày): thứ 3, 4, 5 có 2 lần, các thứ khác 1 lần.
        var from = new DateOnly(2026, 11, 3);
        var table = ProductTrends.Weekdays(
        [
            new(1, "Bánh bao", new DateOnly(2026, 11, 3), 10),
            new(1, "Bánh bao", new DateOnly(2026, 11, 10), 5),
            new(1, "Bánh bao", new DateOnly(2026, 11, 8), 4),
            new(null, "flan", new DateOnly(2026, 11, 9), 30),
        ], from, new DateOnly(2026, 11, 12));

        Assert.Equal([1, 2, 2, 2, 1, 1, 1], table.DayCounts);
        Assert.False(table.TooShort);
        Assert.Equal(["flan", "Bánh bao"], table.Rows.Select(r => r.Name)); // tổng nhiều trước
        var gio = table.Rows[1];
        Assert.Equal(19, gio.Total);
        Assert.Equal((15L, 2, 7.5), (gio.Cells[1].Total, gio.Cells[1].Days, gio.Cells[1].Average!.Value)); // thứ 3
        Assert.Equal(4, gio.Cells[6].Average);  // chủ nhật 08/11
        Assert.Equal(0, gio.Cells[0].Average);  // thứ 2 có trong khoảng nhưng không bán
        Assert.Equal(7.5, gio.MaxAverage);
    }

    [Fact]
    public void Weekday_table_for_all_dates_uses_the_data_range_and_flags_short_ranges()
    {
        var all = ProductTrends.Weekdays([new(1, "A", new DateOnly(2026, 11, 2), 3), new(1, "A", new DateOnly(2026, 11, 16), 1)], null, null);
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 16)), (all.From!.Value, all.To!.Value));
        Assert.Equal(3, all.DayCounts[0]); // 3 thứ 2: 02, 09, 16/11
        Assert.Null(ProductTrends.Weekdays([], new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 13)).Rows.FirstOrDefault());
        Assert.True(ProductTrends.Weekdays([], new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 15)).TooShort);
        Assert.False(ProductTrends.Weekdays([], new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 16)).TooShort);
        Assert.Equal([0, 0, 0, 0, 0, 0, 0], ProductTrends.Weekdays([], null, null).DayCounts);
    }

    [Fact]
    public void Product_report_reads_previous_period_and_daily_quantities_from_the_database()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Path)).Resolve()), db);
        var orders = new OrderRepository(db);
        var products = new MasterDataRepository(db).GetProducts().ToDictionary(p => p.Id);
        // Kỳ trước của 27–28/6 là 25–26/6: món 1 x3 (26/6) + đơn huỷ món 1 x9 (25/6, không tính).
        orders.Save(new OrderDraft(0, 2, OrderStatus.Shipping, new DateTime(2026, 6, 26, 9, 0, 0), false, null, 0, null, [new OrderLineDraft(1, 3)]), products);
        var cancelled = orders.Save(new OrderDraft(0, 2, OrderStatus.Shipping, new DateTime(2026, 6, 25), false, null, 0, null, [new OrderLineDraft(1, 9)]), products);
        orders.SetStatus(cancelled, OrderStatus.Cancelled);
        var reports = new ReportRepository(db, orders);

        var report = reports.ProductReport(new DateOnly(2026, 6, 27), new DateOnly(2026, 6, 28), null);

        Assert.Equal(3, Assert.Single(report.Previous!, p => p.ProductId == 1).Quantity);
        Assert.Equal(report.TotalQuantity, report.Days.Sum(d => d.Quantity));
        Assert.All(report.Days, d => Assert.InRange(d.Day, new DateOnly(2026, 6, 27), new DateOnly(2026, 6, 28)));
        Assert.Null(reports.ProductReport(null, null, null).Previous);
        Assert.Empty(reports.ProductReport(new DateOnly(2026, 6, 27), new DateOnly(2026, 6, 28), 999).Previous!);
    }
}
