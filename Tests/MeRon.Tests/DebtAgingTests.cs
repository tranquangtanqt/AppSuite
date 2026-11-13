using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Reports;

namespace MeRon.Tests;

/// <summary>Công nợ theo tuổi nợ, mốc người dùng nhập (2026-11-13).</summary>
public class DebtAgingTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static UnpaidOrderPoint O(long? user, long id, int? daysAgo, long total) =>
        new(user, id, daysAgo is { } d ? Today.AddDays(-d) : null, total);

    [Fact]
    public void Parse_sorts_dedupes_and_rejects_bad_marks()
    {
        Assert.Equal([7, 30], DebtAging.Parse("30, 7").Marks);
        Assert.Equal([3, 7, 15], DebtAging.Parse(" 7;3  15,7 ").Marks);
        Assert.Contains("ít nhất 1 mốc", DebtAging.Parse("  ").Error);
        Assert.Contains("\"abc\"", DebtAging.Parse("7, abc").Error);
        Assert.NotNull(DebtAging.Parse("0").Error);
        Assert.NotNull(DebtAging.Parse("5000").Error);
        Assert.Contains("Tối đa 5", DebtAging.Parse("1,2,3,4,5,6").Error);
        Assert.Equal("3, 7, 15", DebtAging.Format([3, 7, 15]));
        Assert.Equal(DebtAging.DefaultMarks, DebtAging.Normalize(null));
        Assert.Equal(DebtAging.DefaultMarks, DebtAging.Normalize([0, -1]));
        Assert.Equal([10], DebtAging.Normalize([10, 10]));
    }

    [Fact]
    public void Each_order_goes_to_its_own_bucket_and_a_customer_can_span_several()
    {
        var debts = CustomerDebts.Build(
        [
            O(1, 1, 0, 10000), O(1, 2, 7, 20000), O(1, 3, 40, 30000), // khách 1: 0, 7, 40 ngày
            O(2, 4, 8, 15000), O(2, 5, 30, 5000),                      // khách 2: 8, 30 ngày
            O(null, 6, 31, 1000),
        ], new Dictionary<long, Customer>(), Today);

        var buckets = DebtAging.Summarize(debts, DebtAging.DefaultMarks, Today);

        Assert.Equal(["0–7 ngày", "8–30 ngày", "Trên 30 ngày"], buckets.Select(b => b.Label));
        Assert.Equal((30000L, 2, 1), (buckets[0].Amount, buckets[0].OrderCount, buckets[0].CustomerCount));
        Assert.Equal((20000L, 2, 1), (buckets[1].Amount, buckets[1].OrderCount, buckets[1].CustomerCount));
        Assert.Equal((31000L, 2, 2), (buckets[2].Amount, buckets[2].OrderCount, buckets[2].CustomerCount));
        Assert.Equal([false, false, true], buckets.Select(b => b.IsOldest));
        Assert.Equal(debts.Sum(d => d.Amount), buckets.Sum(b => b.Amount));

        var khach1 = debts.Single(d => d.UserId == 1);
        Assert.True(DebtAging.HasOrderIn(khach1, buckets[0], Today));
        Assert.False(DebtAging.HasOrderIn(khach1, buckets[1], Today));
        Assert.True(DebtAging.HasOrderIn(khach1, buckets[2], Today));
        Assert.True(DebtAging.IsOverdue(khach1, DebtAging.DefaultMarks));             // 40 > 30
        Assert.False(DebtAging.IsOverdue(debts.Single(d => d.UserId == 2), DebtAging.DefaultMarks)); // 30 không quá 30
    }

    [Fact]
    public void Custom_marks_make_more_buckets_and_empty_ones_stay()
    {
        var debts = CustomerDebts.Build([O(1, 1, 2, 1000), O(1, 2, 5, 2000)], new Dictionary<long, Customer>(), Today);

        var buckets = DebtAging.Summarize(debts, [3, 7, 15], Today);

        Assert.Equal(["0–3 ngày", "4–7 ngày", "8–15 ngày", "Trên 15 ngày"], buckets.Select(b => b.Label));
        Assert.Equal([1000L, 2000, 0, 0], buckets.Select(b => b.Amount));
        Assert.True(DebtAging.IsOverdue(debts[0], [3])); // 5 > 3
    }

    [Fact]
    public void Future_and_no_date_orders_get_their_own_buckets_only_when_present()
    {
        var debts = CustomerDebts.Build([O(1, 1, -2, 1000), O(2, 2, null, 2000), O(1, 3, 1, 500)], new Dictionary<long, Customer>(), Today);

        var buckets = DebtAging.Summarize(debts, DebtAging.DefaultMarks, Today);

        Assert.Equal(["0–7 ngày", "8–30 ngày", "Trên 30 ngày", "Chưa tới ngày giao", "Không có ngày giao"], buckets.Select(b => b.Label));
        Assert.Equal([500L, 0, 0, 1000, 2000], buckets.Select(b => b.Amount));
        Assert.Equal(3, DebtAging.Summarize(CustomerDebts.Build([O(1, 1, 1, 1)], new Dictionary<long, Customer>(), Today), DebtAging.DefaultMarks, Today).Count);
    }

    [Fact]
    public void Marks_are_saved_in_settings_json()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MeRonAging_" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, MenuSettings.FileName);
            Assert.Equal(DebtAging.DefaultMarks, MenuSettings.Load(path).DebtAgingMarks);
            var settings = new MenuSettings { DebtAgingMarks = [3, 7, 15] };
            settings.Save(path);
            Assert.Equal([3, 7, 15], MenuSettings.Load(path).DebtAgingMarks);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
