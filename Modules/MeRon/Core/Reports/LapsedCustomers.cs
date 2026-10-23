using MeRon.Core.Models;

namespace MeRon.Core.Reports;

/// <summary>1 đơn (không huỷ, có ngày giao) của 1 khách - dữ liệu đầu vào của <see cref="LapsedCustomers"/>.</summary>
public sealed record CustomerOrderPoint(long UserId, long OrderId, DateOnly Day, long Total);

/// <summary>Tổng số lượng 1 món (món tự do gộp theo tên) của 1 khách.</summary>
public sealed record CustomerProductQuantity(long UserId, string Name, long Quantity);

/// <summary>1 khách lâu không đặt.</summary>
/// <param name="AverageGapDays">Trung bình bao nhiêu ngày đặt 1 lần (theo các ngày giao khác nhau); null = mới đặt dưới 3 ngày khác nhau - 1 khoảng cách chưa nói
/// được thói quen (2 đơn cách nhau 1 ngày thành "nghỉ gấp 100 lần thói quen").</param>
/// <param name="LastOrderId">Đơn gần nhất - để "Đặt lại như lần trước".</param>
public sealed record LapsedCustomer(
    long UserId,
    string Name,
    string? Phone,
    string? Address,
    long OrderCount,
    long Amount,
    DateOnly LastDay,
    int DaysSince,
    double? AverageGapDays,
    IReadOnlyList<string> TopProducts,
    long LastOrderId)
{
    /// <summary>Khoảng nghỉ hiện tại gấp mấy lần thói quen (null = chưa có thói quen).</summary>
    public double? OverdueRatio => AverageGapDays is > 0 and var gap ? DaysSince / gap : null;

    /// <summary>Nghỉ lâu gấp từ 2 lần thói quen trở lên - đáng hỏi thăm hơn cả.</summary>
    public bool IsOverHabit => OverdueRatio >= 2;
}

public enum LapsedSort
{
    /// <summary>Lâu không đặt nhất trước.</summary>
    Longest,

    /// <summary>Tổng tiền đã mua nhiều nhất trước.</summary>
    Amount,

    /// <summary>Quá thói quen nhiều nhất trước (khách chưa có thói quen xếp cuối).</summary>
    OverHabit,
}

/// <summary>
/// Khách lâu không đặt: khách đã đặt ít nhất <c>minOrders</c> đơn (không tính đơn Huỷ) mà đơn gần nhất cách hôm nay từ
/// <c>minDays</c> ngày trở lên. Ngày tính theo ngày giao; khách có đơn giao trong tương lai coi như vẫn đang đặt.
/// </summary>
public static class LapsedCustomers
{
    public const int DefaultMinDays = 30;
    public const int DefaultMinOrders = 2;

    /// <summary>Số ngày đặt khác nhau tối thiểu để tính thói quen.</summary>
    public const int MinHabitDays = 3;

    public static IReadOnlyList<LapsedCustomer> Build(
        IEnumerable<CustomerOrderPoint> orders,
        IEnumerable<CustomerProductQuantity> products,
        IReadOnlyDictionary<long, Customer> customers,
        DateOnly today,
        int minDays,
        int minOrders)
    {
        var topProducts = products
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.OrderByDescending(p => p.Quantity).ThenBy(p => p.Name)
                .Take(3).Select(p => p.Name.Trim()).ToList());

        var result = new List<LapsedCustomer>();
        foreach (var group in orders.GroupBy(o => o.UserId))
        {
            if (!customers.TryGetValue(group.Key, out var customer))
            {
                continue;
            }
            var list = group.ToList();
            var last = list.MaxBy(o => (o.Day, o.OrderId))!;
            var daysSince = today.DayNumber - last.Day.DayNumber;
            if (list.Count < minOrders || daysSince < minDays)
            {
                continue;
            }
            var days = list.Select(o => o.Day).Distinct().ToList();
            double? gap = days.Count >= MinHabitDays ? (double)(days.Max().DayNumber - days.Min().DayNumber) / (days.Count - 1) : null;
            result.Add(new LapsedCustomer(group.Key, customer.Name ?? "?", Blank(customer.Phone), Blank(customer.Address),
                list.Count, list.Sum(o => o.Total), last.Day, daysSince, gap,
                topProducts.GetValueOrDefault(group.Key) ?? [], last.OrderId));
        }
        return Sort(result, LapsedSort.Longest);
    }

    public static IReadOnlyList<LapsedCustomer> Sort(IEnumerable<LapsedCustomer> customers, LapsedSort sort) => (sort switch
    {
        LapsedSort.Amount => customers.OrderByDescending(c => c.Amount).ThenByDescending(c => c.DaysSince),
        LapsedSort.OverHabit => customers.OrderByDescending(c => c.OverdueRatio ?? double.MinValue).ThenByDescending(c => c.DaysSince),
        _ => customers.OrderByDescending(c => c.DaysSince).ThenByDescending(c => c.Amount),
    }).ToList();

    /// <summary>
    /// Tin nhắn hỏi thăm soạn sẵn để dán vào Zalo. Tên khách trên điện thoại hay có mã phòng / mã nhà phía trước
    /// ("908 Chị Huyền") → bỏ các chữ đầu có số và mã ở cuối ("Mai Anh w1"); tên viết hoa hết thì đổi thành chữ hoa
    /// đầu từ; không còn gì thì gọi "anh/chị".
    /// </summary>
    public static string ZaloMessage(LapsedCustomer customer)
    {
        var product = customer.TopProducts.Count > 0 ? customer.TopProducts[0] : "bánh";
        return $"Dạ em chào {FriendlyName(customer.Name)} ạ! Lâu rồi chưa thấy mình đặt {product}. Tuần này mình có cần đặt không ạ? Em làm sẵn cho mình nha.";
    }

    /// <summary>Tên để gọi trong tin nhắn (dùng chung với nhắc nợ - <see cref="CustomerDebts.ReminderMessage"/>): bỏ chữ có số ở
    /// đầu / cuối ("908 Chị Huyền" → "Chị Huyền"), viết hoa hết thì đổi thành chữ hoa đầu từ, không còn gì thì "anh/chị".</summary>
    public static string FriendlyName(string fullName)
    {
        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).SkipWhile(HasDigit).Reverse().SkipWhile(HasDigit).Reverse().ToList();
        var name = words.Count > 0 ? string.Join(' ', words) : "anh/chị";
        if (name.Any(char.IsLetter) && name == name.ToUpper(Vietnamese))
        {
            name = Vietnamese.TextInfo.ToTitleCase(name.ToLower(Vietnamese)); // "KHÁNH QUỲNH" → "Khánh Quỳnh"
        }
        return name;
    }

    private static readonly System.Globalization.CultureInfo Vietnamese = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");

    private static bool HasDigit(string word) => word.Any(char.IsDigit);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
