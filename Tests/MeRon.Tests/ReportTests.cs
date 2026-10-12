using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.Core.Reports;

namespace MeRon.Tests;

public class PurchaseRepositoryTests
{
    [Fact]
    public void Create_edit_filter_and_delete_purchases()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        var repo = new PurchaseRepository(db);

        var a = repo.Save(new PurchaseDraft(0, new DateTime(2026, 7, 1), [new(" Bột mì ", 120000), new("Trứng", 45000)]));
        var b = repo.Save(new PurchaseDraft(0, new DateTime(2026, 7, 3, 9, 30, 0), [new("Sữa", 60000)]));

        Assert.Equal([b, a], repo.Query(null, null).Select(p => p.Id)); // mới nhất trước
        var first = repo.Get(a)!;
        Assert.Equal(165000, first.Total);
        Assert.Equal("Bột mì", first.Items[0].Name);
        Assert.Equal([a], repo.Query(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2)).Select(p => p.Id));

        repo.Save(new PurchaseDraft(a, new DateTime(2026, 7, 2), [new("Bột mì", 100000)]));
        var edited = repo.Get(a)!;
        Assert.Equal(100000, edited.Total);
        Assert.Single(edited.Items);
        Assert.Equal(new DateTime(2026, 7, 2), edited.Day);

        repo.Delete(b);
        Assert.Null(repo.Get(b));
        Assert.Equal(1, db.Count("PurchaseItems"));
    }

    [Fact]
    public void Validation_points_at_the_line()
    {
        Assert.Throws<PurchaseValidationException>(() => PurchaseRepository.Validate(new PurchaseDraft(0, DateTime.Today, [])));
        var name = Assert.Throws<PurchaseValidationException>(() =>
            PurchaseRepository.Validate(new PurchaseDraft(0, DateTime.Today, [new("A", 1), new(" ", 5)])));
        Assert.Equal((1, false), (name.LineIndex, name.PriceField));
        var price = Assert.Throws<PurchaseValidationException>(() =>
            PurchaseRepository.Validate(new PurchaseDraft(0, DateTime.Today, [new("A", 0)])));
        Assert.True(price.PriceField);
    }
}

public class ReportRepositoryTests
{
    private static readonly DateOnly June27 = new(2026, 6, 27);
    private static readonly DateOnly June28 = new(2026, 6, 28);

    /// <summary>Bộ backup mẫu + 1 đơn huỷ (khách 2, 28/6, món 1 x10) + 1 phiếu nhập.</summary>
    private static (MeRonDatabase Db, ReportRepository Reports, OrderRepository Orders) Setup(TempFolder temp)
    {
        var db = temp.NewDatabase();
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Path)).Resolve()), db);
        var orders = new OrderRepository(db);
        var products = new MasterDataRepository(db).GetProducts().ToDictionary(p => p.Id);
        var cancelled = orders.Save(new OrderDraft(0, 2, OrderStatus.Shipping, new DateTime(2026, 6, 28), false, null, 0, null,
            [new OrderLineDraft(1, 10)]), products);
        orders.SetStatus(cancelled, OrderStatus.Cancelled);
        new PurchaseRepository(db).Save(new PurchaseDraft(0, new DateTime(2026, 6, 28), [new("Bột", 50000), new("bột ", 20000), new("Đường", 10000)]));
        return (db, new ReportRepository(db, orders), orders);
    }

    [Fact]
    public void Order_report_excludes_cancelled_from_totals_and_products()
    {
        using var temp = new TempFolder();
        var (_, reports, _) = Setup(temp);

        var report = reports.OrderReport(new OrderFilter(null, June27, June28));

        Assert.Equal(3, report.ByStatus.Sum(s => s.Count));
        Assert.Equal((1L, 50000L), (report.ByStatus.Single(s => s.Status == OrderStatus.Cancelled).Count, report.ByStatus.Single(s => s.Status == OrderStatus.Cancelled).Amount));
        Assert.Equal(2, report.OrderCount);        // 24.000 (đã thanh toán) + 12.000 (đã lên đơn)
        Assert.Equal(36000, report.Amount);
        // món: 1 (x1, 5.000), 2 (x2, 20.000 - đơn giá lúc đặt), Bánh flan tự do (x1) - không tính 10 món của đơn huỷ
        Assert.Equal(4, report.Products.Sum(p => p.Quantity));
        Assert.Equal(("Bánh bao thịt trứng", 2L, 20000L), (report.Products[0].Name, report.Products[0].Quantity, report.Products[0].Amount));
        Assert.Contains(report.Products, p => p.ProductId is null && p.Name == "Bánh flan");
        // khách: gồm cả đơn huỷ khi lọc Tất cả (như điện thoại), sắp theo tiền giảm dần
        Assert.Equal(["Anh Bình", "Chị An"], report.Customers.Select(c => c.Name));
        Assert.Equal(2, report.Customers[0].OrderCount);

        var onlyPaid = reports.OrderReport(new OrderFilter(OrderStatus.Completed, June27, June28));
        Assert.Equal((1L, 24000L), (onlyPaid.OrderCount, onlyPaid.Amount));
    }

    [Fact]
    public void Product_report_filters_one_product_and_keeps_only_its_lines()
    {
        using var temp = new TempFolder();
        var (_, reports, _) = Setup(temp);

        var all = reports.ProductReport(June27, June28, null);
        Assert.Equal(4, all.TotalQuantity);
        Assert.Equal(2, all.Orders.Count); // đơn huỷ không có

        var one = reports.ProductReport(June27, June28, 2);
        Assert.Equal(2, one.TotalQuantity);
        var order = Assert.Single(one.Orders);
        Assert.All(order.Items, i => Assert.Equal(2, i.ProductId));
    }

    [Fact]
    public void Summary_report_combines_orders_and_purchases_grouping_items_by_name()
    {
        using var temp = new TempFolder();
        var (_, reports, _) = Setup(temp);

        var summary = reports.SummaryReport(June27, June28);

        Assert.Equal((2L, 36000L), (summary.OrderCount, summary.OrderAmount));
        Assert.Equal((1L, 80000L), (summary.PurchaseCount, summary.PurchaseAmount));
        Assert.Equal(2, summary.PurchaseItems.Count); // "Bột" và "bột " gộp 1
        Assert.Equal((2L, 70000L), (summary.PurchaseItems[0].Count, summary.PurchaseItems[0].Amount));

        var empty = reports.SummaryReport(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        Assert.Equal((0L, 0L, 0L), (empty.OrderCount, empty.PurchaseCount, empty.PurchaseAmount));
    }

    [Fact]
    public void Top_customers_rank_by_amount_without_cancelled()
    {
        using var temp = new TempFolder();
        var (_, reports, _) = Setup(temp);

        var top = reports.TopCustomers(null, null);

        Assert.Equal(["Chị An", "Anh Bình"], top.Select(c => c.Name)); // Anh Bình: 12.000 (đơn huỷ 50.000 không tính)
        Assert.Equal((1L, 12000L), (top[1].OrderCount, top[1].Amount));
        Assert.Equal(new DateTime(2026, 6, 27).Date, top[0].FirstDelivery!.Value.Date);
    }
}
