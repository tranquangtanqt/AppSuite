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

    [Fact]
    public void Formats_as_day_month_year() => Assert.Equal("04/02/2027", DateInput.Format(new DateOnly(2027, 2, 4)));
}
