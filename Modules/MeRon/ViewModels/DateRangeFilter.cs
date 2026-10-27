using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;

namespace MeRon.ViewModels;

/// <summary>
/// Khoảng ngày Từ – Đến dùng chung (Mua hàng, 4 báo cáo): mặc định hôm nay như điện thoại; đổi ngày hoặc chọn nhanh
/// (<see cref="DateRangePreset"/>) → <see cref="Changed"/> 1 lần để trang tải lại.
/// </summary>
public sealed partial class DateRangeFilter : ObservableObject
{
    private bool _suspend;

    public DateRangeFilter()
    {
        Reset();
    }

    [ObservableProperty]
    private DateTimeOffset? _fromDate;

    [ObservableProperty]
    private DateTimeOffset? _toDate;

    public event EventHandler? Changed;

    public DateOnly? From => FromDate is { } v ? DateOnly.FromDateTime(v.Date) : null;

    public DateOnly? To => ToDate is { } v ? DateOnly.FromDateTime(v.Date) : null;

    partial void OnFromDateChanged(DateTimeOffset? value)
    {
        if (!FixRange(toChanged: false))
        {
            Raise();
        }
    }

    partial void OnToDateChanged(DateTimeOffset? value)
    {
        if (!FixRange(toChanged: true))
        {
            Raise();
        }
    }

    /// <summary>Từ ngày &gt; Đến ngày → kéo ô kia theo ô vừa đổi (<see cref="DateRanges.Fix"/>; lần đặt đó tự báo Changed).</summary>
    private bool FixRange(bool toChanged)
    {
        var (from, to) = DateRanges.Fix(From, To, toChanged);
        if (from != From)
        {
            FromDate = ToOffset(from);
            return true;
        }
        if (to != To)
        {
            ToDate = ToOffset(to);
            return true;
        }
        return false;
    }

    public void Apply(DateRangePreset preset)
    {
        var (from, to) = DateRanges.Resolve(preset, DateOnly.FromDateTime(DateTime.Today));
        _suspend = true;
        FromDate = ToOffset(from);
        ToDate = ToOffset(to);
        _suspend = false;
        Raise();
    }

    /// <summary>Về hôm nay, không báo Changed (trang tự tải sau khi reset).</summary>
    public void Reset()
    {
        _suspend = true;
        var today = new DateTimeOffset(DateTime.Today);
        FromDate = today;
        ToDate = today;
        _suspend = false;
    }

    /// <summary>"dd/MM/yyyy – dd/MM/yyyy" / "mọi ngày" - dùng trong tiêu đề báo cáo.</summary>
    public string Describe() => (From, To) switch
    {
        (null, null) => "mọi ngày",
        ({ } f, { } t) when f == t => DbDate.Show(f.ToDateTime(TimeOnly.MinValue)),
        _ => $"{(From is { } f2 ? DbDate.Show(f2.ToDateTime(TimeOnly.MinValue)) : "…")} – {(To is { } t2 ? DbDate.Show(t2.ToDateTime(TimeOnly.MinValue)) : "…")}",
    };

    private void Raise()
    {
        if (!_suspend)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static DateTimeOffset? ToOffset(DateOnly? day) => day is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;
}
