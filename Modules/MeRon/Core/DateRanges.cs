namespace MeRon.Core;

/// <summary>Khoảng ngày chọn nhanh (bộ lọc Đặt hàng, báo cáo).</summary>
public enum DateRangePreset
{
    Today,
    Yesterday,
    Last7Days,
    ThisMonth,
    LastMonth,
    All,
}

public static class DateRanges
{
    public static string Label(DateRangePreset preset) => preset switch
    {
        DateRangePreset.Yesterday => "Hôm qua",
        DateRangePreset.Last7Days => "7 ngày qua",
        DateRangePreset.ThisMonth => "Tháng này",
        DateRangePreset.LastMonth => "Tháng trước",
        DateRangePreset.All => "Mọi ngày (bỏ lọc ngày)",
        _ => "Hôm nay",
    };

    /// <summary>(Từ, Đến) tính cả 2 ngày; <see cref="DateRangePreset.All"/> = (null, null).</summary>
    public static (DateOnly? From, DateOnly? To) Resolve(DateRangePreset preset, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return preset switch
        {
            DateRangePreset.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
            DateRangePreset.Last7Days => (today.AddDays(-6), today),
            DateRangePreset.ThisMonth => (monthStart, monthStart.AddMonths(1).AddDays(-1)),
            DateRangePreset.LastMonth => (monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            DateRangePreset.All => (null, null),
            _ => (today, today),
        };
    }

    /// <summary>
    /// Sau khi đổi 1 ô mà Từ ngày lớn hơn Đến ngày: đổi Từ → Đến ngày = Từ ngày; đổi Đến → Từ ngày = Đến ngày
    /// (thiếu 1 trong 2 thì giữ nguyên).
    /// </summary>
    public static (DateOnly? From, DateOnly? To) Fix(DateOnly? from, DateOnly? to, bool toChanged) =>
        from is { } f && to is { } t && f > t ? (toChanged ? (t, t) : (f, f)) : (from, to);
}
