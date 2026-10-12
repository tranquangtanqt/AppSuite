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
    }
}
