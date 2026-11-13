namespace MeRon.Core.Reports;

/// <summary>Kiểu thay đổi so với kỳ trước (tô màu): lên / xuống / bằng / món mới / kỳ này không bán.</summary>
public enum TrendKind
{
    None,
    Up,
    Down,
    Same,
    New,
    Gone,
}

/// <summary>1 món của báo cáo Theo món ăn kèm so sánh số lượng với kỳ trước. Món kỳ này không bán (chỉ có ở kỳ trước):
/// <see cref="Current"/> số lượng 0, <see cref="Kind"/> = Gone.</summary>
public sealed record ProductTrend(ProductSummary Current, long? PreviousQuantity, TrendKind Kind, double? Change)
{
    /// <summary>"▲ 27%", "▼ 100%", "bằng", "mới"; không có kỳ trước → trống.</summary>
    public string Text => Kind switch
    {
        TrendKind.New => "mới",
        TrendKind.Same => "bằng",
        TrendKind.Up or TrendKind.Down or TrendKind.Gone => $"{(Kind == TrendKind.Up ? "▲" : "▼")} {Math.Abs(Change ?? 0).ToString("0%", VnFormat.Culture)}",
        _ => string.Empty,
    };
}

/// <summary>Số lượng 1 món trong 1 ngày giao (để dựng bảng món × thứ).</summary>
public sealed record ProductDay(long? ProductId, string Name, DateOnly Day, long Quantity);

/// <summary>1 ô của bảng món × thứ: tổng số lượng các ngày thứ đó, số lần thứ đó có trong khoảng, trung bình mỗi ngày.</summary>
public sealed record WeekdayCell(long Total, int Days)
{
    public double? Average => Days > 0 ? (double)Total / Days : null;
}

/// <summary>1 dòng (1 món) của bảng món × thứ: 7 ô thứ 2 … chủ nhật + tổng.</summary>
public sealed record WeekdayRow(long? ProductId, string Name, IReadOnlyList<WeekdayCell> Cells, long Total)
{
    /// <summary>Trung bình lớn nhất của dòng (tô đậm ô đó, độ đậm màu các ô so với ô này).</summary>
    public double MaxAverage => Cells.Max(c => c.Average ?? 0);
}

/// <summary>Bảng món × thứ của khoảng ngày <see cref="From"/> – <see cref="To"/>; <see cref="DayCounts"/> = số lần mỗi thứ
/// (T2 … CN) có trong khoảng. <see cref="TooShort"/>: khoảng dưới 7 ngày - có thứ chưa xuất hiện, nhắc chọn dài hơn.</summary>
public sealed record WeekdayTable(DateOnly? From, DateOnly? To, IReadOnlyList<int> DayCounts, IReadOnlyList<WeekdayRow> Rows)
{
    public bool TooShort => From is { } f && To is { } t && t.DayNumber - f.DayNumber + 1 < 7;
}

/// <summary>
/// Thống kê tốt hơn cho trang Theo món ăn (người dùng chốt 2026-11-13): ▲▼ % số lượng so với kỳ trước (khoảng ngày cùng độ
/// dài ngay trước khoảng đang chọn, như trang Biểu đồ; món kỳ trước chưa bán = "mới", món kỳ này không bán nữa xếp cuối ▼ 100%)
/// và bảng món × thứ trong tuần (ô = trung bình mỗi thứ = tổng / số lần thứ đó có trong khoảng ngày).
/// </summary>
public static class ProductTrends
{
    /// <summary>Kỳ trước: cùng số ngày, liền trước <paramref name="from"/>. Khoảng không đủ 2 đầu (Tất cả) → không có kỳ trước.</summary>
    public static (DateOnly From, DateOnly To)? PreviousPeriod(DateOnly? from, DateOnly? to)
    {
        if (from is not { } f || to is not { } t || t < f)
        {
            return null;
        }
        var previousTo = f.AddDays(-1);
        return (previousTo.AddDays(-(t.DayNumber - f.DayNumber)), previousTo);
    }

    private static string Key(long? productId, string name) => productId is { } id ? $"id:{id}" : $"name:{name}";

    /// <summary>Ghép kỳ này với kỳ trước (<paramref name="previous"/> null = không có kỳ trước, cột ▲▼ để trống). Thứ tự giữ
    /// như <paramref name="current"/>; món chỉ có ở kỳ trước nối cuối, kỳ trước bán nhiều trước.</summary>
    public static IReadOnlyList<ProductTrend> Compare(IReadOnlyList<ProductSummary> current, IReadOnlyList<ProductSummary>? previous)
    {
        if (previous is null)
        {
            return current.Select(p => new ProductTrend(p, null, TrendKind.None, null)).ToList();
        }
        var before = previous.GroupBy(p => Key(p.ProductId, p.Name)).ToDictionary(g => g.Key, g => g.First());
        var list = new List<ProductTrend>();
        foreach (var p in current)
        {
            var old = before.Remove(Key(p.ProductId, p.Name), out var o) ? o.Quantity : 0;
            if (old == 0)
            {
                list.Add(new ProductTrend(p, 0, TrendKind.New, null));
                continue;
            }
            var change = (double)(p.Quantity - old) / old;
            var kind = Math.Abs(change) < 0.005 ? TrendKind.Same : change > 0 ? TrendKind.Up : TrendKind.Down;
            list.Add(new ProductTrend(p, old, kind, change));
        }
        list.AddRange(before.Values.Where(p => p.Quantity > 0).OrderByDescending(p => p.Quantity)
            .Select(p => new ProductTrend(p with { Quantity = 0, Amount = 0 }, p.Quantity, TrendKind.Gone, -1)));
        return list;
    }

    /// <summary>Thứ 2 = 0 … chủ nhật = 6.</summary>
    public static int WeekdayIndex(DateOnly day) => ((int)day.DayOfWeek + 6) % 7;

    /// <summary>Bảng món × thứ. Khoảng ngày thiếu đầu nào (Tất cả) thì lấy theo ngày giao sớm / muộn nhất trong dữ liệu.
    /// Dòng xếp theo tổng số lượng giảm dần.</summary>
    public static WeekdayTable Weekdays(IReadOnlyList<ProductDay> days, DateOnly? from, DateOnly? to)
    {
        var first = from ?? (days.Count > 0 ? days.Min(d => d.Day) : null);
        var last = to ?? (days.Count > 0 ? days.Max(d => d.Day) : null);
        var counts = new int[7];
        if (first is { } f && last is { } l)
        {
            for (var d = f; d <= l; d = d.AddDays(1))
            {
                counts[WeekdayIndex(d)]++;
            }
        }
        var rows = days.GroupBy(d => Key(d.ProductId, d.Name))
            .Select(g =>
            {
                var totals = new long[7];
                foreach (var d in g)
                {
                    totals[WeekdayIndex(d.Day)] += d.Quantity;
                }
                var cells = totals.Select((t, i) => new WeekdayCell(t, counts[i])).ToList();
                return new WeekdayRow(g.First().ProductId, g.First().Name, cells, totals.Sum());
            })
            .Where(r => r.Total > 0)
            .OrderByDescending(r => r.Total).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new WeekdayTable(first, last, counts, rows);
    }
}
