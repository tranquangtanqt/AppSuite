using MeRon.Core.Data;
using MeRon.Core.Import;

namespace MeRon.Tests;

/// <summary>
/// Chạy thử với 1 bộ backup thật của điện thoại (dữ liệu khách hàng - không đưa vào repo): đặt biến môi trường
/// MERON_REAL_BACKUP = thư mục backup (hoặc thư mục cha). Không đặt thì test tự bỏ qua.
/// </summary>
public class RealBackupTests
{
    [Fact]
    public void Real_phone_backup_imports_without_errors()
    {
        var folder = Environment.GetEnvironmentVariable("MERON_REAL_BACKUP");
        Assert.SkipWhen(string.IsNullOrEmpty(folder), "MERON_REAL_BACKUP chưa đặt");

        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        var plan = BackupImporter.Prepare(new LocalFolderBackupSource(folder!).Resolve());
        Assert.True(plan.CanImport, string.Join("\n", plan.Errors));

        var result = BackupImporter.Apply(plan, db);

        foreach (var table in Schema.BackupTables)
        {
            Assert.Equal(result.RowCounts[table.Name], db.Count(table.Name));
        }
        TestContext.Current.SendDiagnosticMessage(string.Join(", ", result.RowCounts.Select(kv => $"{kv.Key}={kv.Value}")));
        TestContext.Current.SendDiagnosticMessage(string.Join("\n", plan.Warnings));

        // Tải toàn bộ đơn (không phân trang) phải nhanh; mọi ngày giao đọc được; tổng dòng món khớp.
        var orders = new OrderRepository(db);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var all = orders.Query(new MeRon.Core.Models.OrderFilter(null, null, null));
        watch.Stop();
        Assert.Equal(result.RowCounts["Orders"], all.Count);
        Assert.Equal(result.RowCounts["OrderItems"], all.Sum(o => o.Items.Count));
        Assert.All(all, o => Assert.NotNull(o.Delivery));
        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");
        TestContext.Current.SendDiagnosticMessage($"Tải {all.Count} đơn: {watch.ElapsedMilliseconds} ms; đơn mới nhất {all[0].DeliveryDatetime}");

        // Báo cáo khớp với tự cộng từ danh sách đơn (bỏ đơn huỷ).
        var reports = new MeRon.Core.Reports.ReportRepository(db, orders);
        watch.Restart();
        var report = reports.OrderReport(new MeRon.Core.Models.OrderFilter(null, null, null));
        var summary = reports.SummaryReport(null, null);
        var top = reports.TopCustomers(null, null);
        var products = reports.ProductReport(null, null, null);
        watch.Stop();
        var active = all.Where(o => o.Status != MeRon.Core.Models.OrderStatus.Cancelled).ToList();
        Assert.Equal(active.Count, report.OrderCount);
        Assert.Equal(active.Sum(o => o.Total), report.Amount);
        Assert.Equal(active.SelectMany(o => o.Items).Sum(i => i.Quantity), report.Products.Sum(p => p.Quantity));
        Assert.Equal(active.SelectMany(o => o.Items).Sum(i => i.LineTotal), report.Products.Sum(p => p.Amount));
        Assert.Equal(report.Amount, summary.OrderAmount);
        Assert.Equal(all.Count, report.ByStatus.Sum(s => s.Count));
        Assert.Equal(active.Count, products.Orders.Count);
        Assert.True(top.Count <= 100);
        TestContext.Current.SendDiagnosticMessage(
            $"4 báo cáo: {watch.ElapsedMilliseconds} ms; {report.OrderCount} đơn, {report.Products.Count} món, top 1 = {top[0].Name} {top[0].Amount}");
    }
}
