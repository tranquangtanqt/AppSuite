namespace MeRon.Core.Reports;

/// <summary>Loại nhóm tuổi nợ: nhóm theo mốc ngày, đơn chưa tới ngày giao (khi tính cả đơn đặt trước), đơn không có ngày giao.</summary>
public enum AgingKind
{
    Age,
    Future,
    NoDay,
}

/// <summary>1 nhóm tuổi nợ: các đơn chưa thanh toán có số ngày (từ ngày giao tới hôm nay) trong [<see cref="From"/>, <see cref="To"/>]
/// (<see cref="To"/> null = không giới hạn trên). <see cref="IsOldest"/> = nhóm lâu nhất (tô cam).</summary>
public sealed record AgingBucket(int Index, AgingKind Kind, string Label, int From, int? To, long Amount, int OrderCount, int CustomerCount, bool IsOldest)
{
    public bool Contains(UnpaidOrderPoint order, DateOnly today) => DebtAging.KindOf(order, today) == Kind
        && (Kind != AgingKind.Age || DebtAging.Age(order, today) is var age && age >= From && (To is null || age <= To));
}

/// <summary>
/// Công nợ theo tuổi nợ (người dùng chốt 2026-11-13): mỗi đơn chưa thanh toán vào 1 nhóm theo số ngày từ ngày giao tới hôm nay
/// (1 khách có thể ở nhiều nhóm); mốc do người dùng nhập ("7, 30" → 0–7, 8–30, trên 30 ngày; tối đa 5 mốc, nhớ trong
/// settings.json); đơn chưa tới ngày giao (khi tính cả đơn đặt trước) và đơn không có ngày giao có nhóm riêng.
/// </summary>
public static class DebtAging
{
    public static readonly IReadOnlyList<int> DefaultMarks = [7, 30];

    public const int MaxMarks = 5;

    public const int MaxDays = 3650;

    /// <summary>"3, 7, 30" → [3, 7, 30] (sắp xếp, bỏ trùng; cách nhau bằng dấu phẩy / chấm phẩy / khoảng trắng). Sai → lời báo.</summary>
    public static (IReadOnlyList<int>? Marks, string? Error) Parse(string? text)
    {
        var parts = (text ?? string.Empty).Split([',', ';', ' ', '，', '、'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return (null, "Nhập ít nhất 1 mốc ngày, vd 7, 30.");
        }
        var marks = new SortedSet<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var day) || day < 1 || day > MaxDays)
            {
                return (null, $"\"{part}\" không phải số ngày (1 – {MaxDays}).");
            }
            marks.Add(day);
        }
        return marks.Count > MaxMarks ? (null, $"Tối đa {MaxMarks} mốc.") : ([.. marks], null);
    }

    public static string Format(IReadOnlyList<int> marks) => string.Join(", ", marks);

    /// <summary>Mốc đọc từ settings.json: hỏng / trống → mặc định 7, 30.</summary>
    public static IReadOnlyList<int> Normalize(IEnumerable<int>? marks) =>
        marks is null ? DefaultMarks : Parse(string.Join(",", marks)).Marks ?? DefaultMarks;

    public static int Age(UnpaidOrderPoint order, DateOnly today) => order.Day is { } day ? today.DayNumber - day.DayNumber : 0;

    public static AgingKind KindOf(UnpaidOrderPoint order, DateOnly today) =>
        order.Day is null ? AgingKind.NoDay : order.Day > today ? AgingKind.Future : AgingKind.Age;

    /// <summary>Thẻ khách tô cam khi đơn cũ nhất thuộc nhóm lâu nhất (quá mốc lớn nhất).</summary>
    public static bool IsOverdue(CustomerDebt debt, IReadOnlyList<int> marks) => debt.OldestDays > marks[^1];

    /// <summary>Các nhóm theo mốc (luôn đủ, kể cả nhóm 0 đơn) + nhóm Chưa tới ngày giao / Không có ngày giao nếu có đơn.</summary>
    public static IReadOnlyList<AgingBucket> Summarize(IEnumerable<CustomerDebt> debts, IReadOnlyList<int> marks, DateOnly today)
    {
        var points = debts.SelectMany(d => d.Orders.Select(o => (Debt: d, Order: o))).ToList();
        var ranges = new List<(AgingKind Kind, string Label, int From, int? To)>();
        var from = 0;
        foreach (var mark in marks)
        {
            ranges.Add((AgingKind.Age, $"{from}–{mark} ngày", from, mark));
            from = mark + 1;
        }
        ranges.Add((AgingKind.Age, $"Trên {marks[^1]} ngày", from, null));
        ranges.Add((AgingKind.Future, "Chưa tới ngày giao", 0, null));
        ranges.Add((AgingKind.NoDay, "Không có ngày giao", 0, null));

        var result = new List<AgingBucket>();
        for (var i = 0; i < ranges.Count; i++)
        {
            var r = ranges[i];
            var probe = new AgingBucket(i, r.Kind, r.Label, r.From, r.To, 0, 0, 0, false);
            var hits = points.Where(p => probe.Contains(p.Order, today)).ToList();
            if (r.Kind != AgingKind.Age && hits.Count == 0)
            {
                continue;
            }
            result.Add(probe with
            {
                Amount = hits.Sum(p => p.Order.Total),
                OrderCount = hits.Count,
                CustomerCount = hits.Select(p => p.Debt.UserId).Distinct().Count(),
                IsOldest = r.Kind == AgingKind.Age && r.To is null,
            });
        }
        return result;
    }

    /// <summary>Khách có ít nhất 1 đơn trong nhóm (lọc danh sách khi bấm ô nhóm).</summary>
    public static bool HasOrderIn(CustomerDebt debt, AgingBucket bucket, DateOnly today) => debt.Orders.Any(o => bucket.Contains(o, today));
}
