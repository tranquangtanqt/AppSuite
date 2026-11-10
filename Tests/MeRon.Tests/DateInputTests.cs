using MeRon.Core;

namespace MeRon.Tests;

public class DateInputTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    [Theory]
    [InlineData("14/10/2026", 2026, 10, 14)]
    [InlineData(" 4/2/2027 ", 2027, 2, 4)]
    [InlineData("14-10-26", 2026, 10, 14)]
    [InlineData("14.10.2026", 2026, 10, 14)]
    [InlineData("14 10 2026", 2026, 10, 14)]
    [InlineData("1/1", 2026, 1, 1)]
    [InlineData("20", 2026, 10, 20)]
    [InlineData("5", 2026, 10, 5)]
    [InlineData("14102026", 2026, 10, 14)]
    [InlineData("141026", 2026, 10, 14)]
    [InlineData("0111", 2026, 11, 1)]
    [InlineData("29/2/2028", 2028, 2, 29)]
    public void Parses_typed_dates(string text, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), DateInput.Parse(text, Today));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("32/10/2026")]
    [InlineData("29/2/2026")]
    [InlineData("1/13")]
    [InlineData("0/5")]
    [InlineData("abc")]
    [InlineData("14/10/2026/1")]
    [InlineData("14/10/202")]
    [InlineData("12345")]
    [InlineData("hôm nay")]
    public void Rejects_invalid_text(string? text) => Assert.Null(DateInput.Parse(text, Today));

    [Theory]
    [InlineData("30/12", 2027, 1, 2, 2026, 12, 30)]   // đầu năm nhập sổ cuối năm trước
    [InlineData("3012", 2027, 1, 2, 2026, 12, 30)]
    [InlineData("2/1", 2026, 12, 30, 2027, 1, 2)]     // cuối năm hẹn giao đầu năm sau
    [InlineData("1/1", 2026, 11, 15, 2027, 1, 1)]     // 47 ngày tới (≤ 60)
    [InlineData("1/1", 2026, 10, 14, 2026, 1, 1)]     // 79 ngày tới > 60 → năm nay như cũ
    [InlineData("14/4", 2026, 10, 14, 2026, 4, 14)]
    [InlineData("15/11", 2027, 1, 14, 2026, 11, 15)]  // đúng 60 ngày trước: vẫn lấy năm trước
    [InlineData("14/11", 2027, 1, 14, 2027, 11, 14)]  // 61 ngày → năm nay như cũ
    [InlineData("20/10", 2026, 10, 14, 2026, 10, 20)]
    [InlineData("30/12/2027", 2027, 1, 2, 2027, 12, 30)] // có ghi năm → giữ nguyên
    [InlineData("30/12/27", 2027, 1, 2, 2027, 12, 30)]
    [InlineData("31", 2027, 1, 2, 2027, 1, 31)]       // chỉ ngày → tháng này
    public void Near_today_picks_the_year_closest_to_today(string text, int ty, int tm, int td, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), DateInput.Parse(text, new DateOnly(ty, tm, td), nearToday: true));

    [Fact]
    public void Without_near_today_a_date_without_year_stays_in_this_year() =>
        Assert.Equal(new DateOnly(2027, 12, 30), DateInput.Parse("30/12", new DateOnly(2027, 1, 2), nearToday: false));

    [Fact]
    public void Formats_as_day_month_year() => Assert.Equal("04/02/2027", DateInput.Format(new DateOnly(2027, 2, 4)));
}
