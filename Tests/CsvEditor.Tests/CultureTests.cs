using System.Globalization;
using CsvEditor.Models;
using CsvEditor.Services;

namespace CsvEditor.Tests;

/// <summary>Máy đặt định dạng số kiểu Việt (vi-VN: dấu phẩy thập phân, dấu chấm phân nhóm) hoặc Đức
/// (de-DE, giống vi-VN) vẫn phải hiểu "2.5" trong file CSV là 2,5 - không phải 25.</summary>
public sealed class CultureTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    private static void Use(string culture) => CultureInfo.CurrentCulture = new CultureInfo(culture);

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Sort_reads_dot_decimal_regardless_of_machine_culture(string culture)
    {
        Use(culture);
        var doc = Csv.Doc(["N"], ["10"], ["2.5"], ["9"], ["-1.25"]);

        var sorted = new SortService().ApplyMultiColumn(doc.Rows, [(0, false)]).Select(r => r.GetCell(0));

        Assert.Equal(["-1.25", "2.5", "9", "10"], sorted);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("de-DE")]
    public void Filter_compares_dot_decimal_as_number(string culture)
    {
        Use(culture);
        var doc = Csv.Doc(["N"], ["2.5"], ["9"], ["30"]);
        var expression = new FilterExpression();
        expression.Conditions.Add(new FilterCondition { ColumnIndex = 0, Operator = FilterOperator.LessThan, Value = "5" });

        var predicate = new FilterService().Compile(expression);

        Assert.Equal(["2.5"], doc.Rows.Where(predicate).Select(r => r.GetCell(0)));
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("de-DE")]
    public void Statistics_sum_uses_dot_decimal(string culture)
    {
        Use(culture);
        var doc = Csv.Doc(["N"], ["2.5"], ["1.5"], ["10"]);

        var stats = new StatisticsService().Compute(doc, null, CancellationToken.None).PerColumn[0];

        Assert.Equal(14, stats.Sum);
        Assert.Equal(1.5, stats.Min);
    }

    [Fact]
    public void Comma_decimal_is_still_understood_on_a_comma_decimal_machine()
    {
        // File CSV dùng ';' làm delimiter (kiểu châu Âu) thường ghi số "2,5" - máy vi-VN vẫn hiểu là số.
        Use("vi-VN");
        var doc = Csv.Doc(["N"], ["10"], ["2,5"], ["9"]);

        var sorted = new SortService().ApplyMultiColumn(doc.Rows, [(0, false)]).Select(r => r.GetCell(0));

        Assert.Equal(["2,5", "9", "10"], sorted);
    }

    [Theory]
    [InlineData("1,000")]   // không đoán dấu phân nhóm: "1,000" trên máy en-US không phải 1000
    [InlineData("abc")]
    [InlineData("")]
    public void Non_plain_numbers_are_text(string value)
    {
        Use("en-US");
        Assert.False(CsvNumber.TryParse(value, out _));
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-3.75", -3.75)]
    [InlineData(" 7 ", 7)]
    [InlineData("1e3", 1000)]
    public void Plain_numbers_parse(string value, double expected)
    {
        Use("vi-VN");
        Assert.True(CsvNumber.TryParse(value, out var number));
        Assert.Equal(expected, number);
    }
}
