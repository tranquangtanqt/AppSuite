using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Reports;
using MeRon.Services;
using MeRon.Views;

namespace MeRon.ViewModels;

/// <summary>1 ô số chính: giá trị + so với kỳ trước.</summary>
public sealed record KpiTile(string Label, string Value, string Delta);

/// <summary>
/// Trang Biểu đồ: chỉ dữ liệu đơn hàng (chưa dùng Mua hàng) - 4 ô số chính so với kỳ trước, doanh thu theo ngày / tuần /
/// tháng, số đơn theo thứ trong tuần, khách mới / khách quay lại, 10 món bán chạy. Khoảng ngày như các trang Thống kê.
/// </summary>
public sealed partial class ChartsViewModel : ReportViewModel
{
    private static readonly CultureInfo Vi = VnFormat.Culture;

    /// <summary>0 = tự chọn, 1 = ngày, 2 = tuần, 3 = tháng (thứ tự combobox).</summary>
    [ObservableProperty]
    private int _bucketChoice;

    [ObservableProperty]
    private bool _showTables;

    [ObservableProperty]
    private string _rangeText = string.Empty;

    [ObservableProperty]
    private string _salesTitle = "Doanh thu theo ngày";

    [ObservableProperty]
    private string _mixTitle = "Khách mới và khách quay lại theo ngày";

    [ObservableProperty]
    private ChartReport? _report;

    public IReadOnlyList<KpiTile> Kpis { get; private set; } = [];
    public IReadOnlyList<ChartColumn> SalesColumns { get; private set; } = [];
    public IReadOnlyList<ChartColumn> WeekdayColumns { get; private set; } = [];
    public IReadOnlyList<ChartColumn> MixColumns { get; private set; } = [];
    public IReadOnlyList<ChartBar> TopBars { get; private set; } = [];

    public IReadOnlyList<string[]> SalesTable { get; private set; } = [];
    public IReadOnlyList<string[]> WeekdayTable { get; private set; } = [];
    public IReadOnlyList<string[]> MixTable { get; private set; } = [];
    public IReadOnlyList<string[]> TopTable { get; private set; } = [];

    partial void OnBucketChoiceChanged(int value) => Load();

    /// <summary>Lần đầu mở: 30 ngày qua (hôm nay như các trang khác thì biểu đồ chỉ có 1 cột).</summary>
    public void SetDefaultRange()
    {
        var today = DateTime.Today;
        Range.FromDate = new DateTimeOffset(today.AddDays(-29));
        Range.ToDate = new DateTimeOffset(today);
    }

    protected override void Refresh()
    {
        ChartBucket? bucket = BucketChoice switch { 1 => ChartBucket.Day, 2 => ChartBucket.Week, 3 => ChartBucket.Month, _ => null };
        var r = new ChartRepository(AppServices.Database).Build(Range.From, Range.To, bucket);
        HasData = !r.IsEmpty;
        var unit = r.Bucket switch { ChartBucket.Week => "tuần", ChartBucket.Month => "tháng", _ => "ngày" };
        SalesTitle = $"Doanh thu theo {unit}";
        MixTitle = $"Khách mới và khách quay lại theo {unit}";
        var previousTo = r.From.AddDays(-1);
        var previousFrom = previousTo.AddDays(-(r.To.DayNumber - r.From.DayNumber));
        RangeText = $"{Show(r.From)} – {Show(r.To)} · so với kỳ trước {Show(previousFrom)} – {Show(previousTo)} · không tính đơn Huỷ, theo ngày giao";

        Kpis =
        [
            new("Doanh thu", VnFormat.Money(r.Current.Revenue), Delta(r.Current.Revenue, r.Previous.Revenue)),
            new("Số đơn", VnFormat.Number(r.Current.Orders), Delta(r.Current.Orders, r.Previous.Orders)),
            new("TB mỗi đơn", VnFormat.Money(r.Current.AverageOrder), Delta(r.Current.AverageOrder, r.Previous.AverageOrder)),
            new("Số khách đặt", VnFormat.Number(r.Current.Customers), Delta(r.Current.Customers, r.Previous.Customers)),
        ];

        SalesColumns = r.Sales.Select(p => new ChartColumn(AxisLabel(p.Start, r.Bucket), [p.Revenue],
            $"{Period(p.Start, r.Bucket)}: {VnFormat.Money(p.Revenue)} · {VnFormat.Number(p.Orders)} đơn")).ToList();
        SalesTable = r.Sales.Select(p => new[] { Period(p.Start, r.Bucket), VnFormat.Number(p.Orders), VnFormat.Money(p.Revenue) }).ToList();

        WeekdayColumns = r.Weekdays.Select(w => new ChartColumn(ShortDay(w.Day), [w.Orders],
            $"{LongDay(w.Day)}: {VnFormat.Number(w.Orders)} đơn · {VnFormat.Money(w.Revenue)}" +
            (w.Orders > 0 ? $" (TB {VnFormat.Money(w.Revenue / w.Orders)}/đơn)" : string.Empty))).ToList();
        WeekdayTable = r.Weekdays.Select(w => new[] { LongDay(w.Day), VnFormat.Number(w.Orders), VnFormat.Money(w.Revenue) }).ToList();

        MixColumns = r.CustomerMix.Select(m => new ChartColumn(AxisLabel(m.Start, r.Bucket), [m.Returning, m.NewCustomers],
            $"{Period(m.Start, r.Bucket)}: {VnFormat.Number(m.Returning)} khách quay lại, {VnFormat.Number(m.NewCustomers)} khách mới")).ToList();
        MixTable = r.CustomerMix.Select(m => new[] { Period(m.Start, r.Bucket), VnFormat.Number(m.Returning), VnFormat.Number(m.NewCustomers) }).ToList();

        TopBars = r.TopProducts.Select((p, i) => new ChartBar(p.Name, p.Amount, VnFormat.Money(p.Amount),
            $"{i + 1}. {p.Name}: {VnFormat.Money(p.Amount)} · {VnFormat.Number(p.Quantity)} phần")).ToList();
        TopTable = r.TopProducts.Select((p, i) => new[] { $"{i + 1}. {p.Name}", VnFormat.Number(p.Quantity), VnFormat.Money(p.Amount) }).ToList();

        Report = r; // trang vẽ lại khi Report đổi (mỗi lần là 1 đối tượng mới)
    }

    /// <summary>"▲ 12% so với kỳ trước" / "▼ 5%…" / "bằng kỳ trước" / "kỳ trước chưa có".</summary>
    internal static string Delta(long current, long previous)
    {
        if (previous == 0)
        {
            return current == 0 ? "kỳ trước cũng chưa có" : "kỳ trước chưa có";
        }
        var change = (double)(current - previous) / previous;
        if (Math.Abs(change) < 0.005)
        {
            return "bằng kỳ trước";
        }
        return $"{(change > 0 ? "▲" : "▼")} {Math.Abs(change).ToString("0%", Vi)} so với kỳ trước";
    }

    /// <summary>Số trục tiền rút gọn: 0, 500k, 1,2tr.</summary>
    public static string CompactMoney(double value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000).ToString("0.#", Vi) + "tr",
        >= 1_000 => (value / 1_000).ToString("0.#", Vi) + "k",
        _ => value.ToString("0", Vi),
    };

    private static string Show(DateOnly day) => day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string AxisLabel(DateOnly start, ChartBucket bucket) => bucket == ChartBucket.Month
        ? start.ToString("MM/yy", CultureInfo.InvariantCulture)
        : start.ToString("dd/MM", CultureInfo.InvariantCulture);

    private static string Period(DateOnly start, ChartBucket bucket) => bucket switch
    {
        ChartBucket.Week => $"Tuần {start:dd/MM} – {start.AddDays(6):dd/MM/yyyy}",
        ChartBucket.Month => $"Tháng {start:MM/yyyy}",
        _ => $"{LongDay(start.DayOfWeek)}, {start:dd/MM/yyyy}",
    };

    private static string ShortDay(DayOfWeek day) => day == DayOfWeek.Sunday ? "CN" : $"T{(int)day + 1}";

    private static string LongDay(DayOfWeek day) => day == DayOfWeek.Sunday ? "Chủ nhật" : $"Thứ {(int)day + 1}";
}
