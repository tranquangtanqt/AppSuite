using MeRon.Core.Models;

namespace MeRon.Core.Reports;

/// <summary>1 đơn chưa thanh toán (trạng thái Đã lên đơn) - dữ liệu đầu vào của <see cref="CustomerDebts"/>.
/// <paramref name="Day"/> = ngày giao (null = đơn không có ngày giao).</summary>
public sealed record UnpaidOrderPoint(long? UserId, long OrderId, DateOnly? Day, long Total);

/// <summary>Công nợ của 1 khách: các đơn chưa thanh toán gộp lại.</summary>
/// <param name="UserId">null = đơn không gắn khách.</param>
/// <param name="OldestDay">Ngày giao của đơn cũ nhất (null = không đơn nào có ngày giao).</param>
/// <param name="OldestDays">Đơn cũ nhất đã giao bao nhiêu ngày (0 = hôm nay / chưa tới ngày giao).</param>
public sealed record CustomerDebt(
    long? UserId,
    string Name,
    string? Phone,
    string? Address,
    IReadOnlyList<long> OrderIds,
    long Amount,
    DateOnly? OldestDay,
    int OldestDays,
    DateOnly? NewestDay)
{
    public int OrderCount => OrderIds.Count;

    /// <summary>Đơn cũ nhất đã quá <see cref="CustomerDebts.OverdueDays"/> ngày - nên nhắc trước.</summary>
    public bool IsOverdue => OldestDays >= CustomerDebts.OverdueDays;
}

public enum DebtSort
{
    /// <summary>Nợ nhiều tiền nhất trước.</summary>
    Amount,

    /// <summary>Đơn cũ nhất (nợ lâu nhất) trước.</summary>
    Oldest,
}

/// <summary>
/// Danh sách công nợ: đơn chưa thanh toán (Đã lên đơn) gom theo khách - tổng nợ, số đơn, đơn cũ nhất bao nhiêu ngày.
/// Mặc định chỉ tính đơn đã tới ngày giao (giao hôm nay hoặc trước đó): đơn đặt trước cho ngày mai chưa phải nợ.
/// </summary>
public static class CustomerDebts
{
    /// <summary>Đơn cũ nhất từ ngần này ngày trở lên thì thẻ khách tô cam.</summary>
    public const int OverdueDays = 30;

    public const string NoCustomerName = "(không có khách)";

    public static IReadOnlyList<CustomerDebt> Build(IEnumerable<UnpaidOrderPoint> orders, IReadOnlyDictionary<long, Customer> customers, DateOnly today)
    {
        var result = new List<CustomerDebt>();
        foreach (var group in orders.GroupBy(o => o.UserId))
        {
            var list = group.ToList();
            var days = list.Where(o => o.Day is not null).Select(o => o.Day!.Value).ToList();
            DateOnly? oldest = days.Count > 0 ? days.Min() : null;
            DateOnly? newest = days.Count > 0 ? days.Max() : null;
            Customer? customer = group.Key is { } id ? customers.GetValueOrDefault(id) : null;
            var name = group.Key is null ? NoCustomerName : Blank(customer?.Name) ?? $"Khách #{group.Key}";
            result.Add(new CustomerDebt(group.Key, name, Blank(customer?.Phone), Blank(customer?.Address),
                list.OrderBy(o => o.Day ?? DateOnly.MaxValue).ThenBy(o => o.OrderId).Select(o => o.OrderId).ToList(),
                list.Sum(o => o.Total), oldest, oldest is { } d ? Math.Max(0, today.DayNumber - d.DayNumber) : 0, newest));
        }
        return Sort(result, DebtSort.Amount);
    }

    public static IReadOnlyList<CustomerDebt> Sort(IEnumerable<CustomerDebt> debts, DebtSort sort) => (sort switch
    {
        DebtSort.Oldest => debts.OrderByDescending(d => d.OldestDays).ThenByDescending(d => d.Amount),
        _ => debts.OrderByDescending(d => d.Amount).ThenByDescending(d => d.OldestDays),
    }).ThenBy(d => d.Name, StringComparer.CurrentCulture).ToList();

    /// <summary>Tin nhắn nhắc thanh toán soạn sẵn để dán vào Zalo (gửi kèm ảnh hoá đơn). Gọi tên như tin hỏi thăm.</summary>
    public static string ReminderMessage(CustomerDebt debt)
    {
        var since = debt.OldestDay is { } day && debt.OrderCount > 1 ? $" (từ ngày {day:dd/MM})" : debt.OldestDay is { } only ? $" ngày {only:dd/MM}" : "";
        var orders = debt.OrderCount > 1 ? $"{debt.OrderCount} đơn chưa thanh toán{since}" : $"đơn chưa thanh toán{since}";
        return $"Dạ em chào {LapsedCustomers.FriendlyName(debt.Name)} ạ! Em gửi mình {orders}, tổng {VnFormat.Money(debt.Amount)}. " +
               "Mình xem giúp em và chuyển khoản khi tiện nha, em cảm ơn ạ!";
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
