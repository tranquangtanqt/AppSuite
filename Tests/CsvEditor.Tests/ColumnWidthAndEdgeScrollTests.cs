using CsvEditor.Services;

namespace CsvEditor.Tests;

/// <summary>Giãn cột vừa nội dung (chọn chữ để đo, kẹp độ rộng) và tự cuộn khi kéo ra mép bảng.</summary>
public sealed class ColumnWidthAndEdgeScrollTests
{
    [Fact]
    public void Display_length_counts_wide_characters_double() =>
        Assert.Equal([3, 6, 4, 5], new[] { "abc", "東京都", "ｶﾀｶﾅ", "Ｆｕ1" }.Select(ColumnAutoFit.DisplayLength).ToArray());

    [Fact]
    public void Display_length_of_multiline_cell_is_its_longest_line() =>
        Assert.Equal(6, ColumnAutoFit.DisplayLength("ab\r\nabcdef\nabc"));

    [Fact]
    public void Longest_texts_scans_whole_column_skips_empty_and_duplicates()
    {
        var values = Enumerable.Range(0, 10_000).Select(i => i.ToString()).Concat(["", "東京都千代田区", "東京都千代田区", "x"]);

        var longest = ColumnAutoFit.LongestTexts(values, 3);

        Assert.Equal("東京都千代田区", longest[0]); // dòng cuối cột, dài nhất khi tính chữ toàn khổ = 2
        Assert.Equal(3, longest.Count);
        Assert.Equal(longest.Count, longest.Distinct().Count());
        Assert.DoesNotContain("", longest);
    }

    [Theory]
    [InlineData(10, 100)] // chữ ngắn vẫn đủ chỗ ô lọc
    [InlineData(300, 324)]
    [InlineData(5000, 800)] // 1 ô rất dài không chiếm hết màn hình
    public void Fit_adds_padding_and_clamps(double measured, double expected) =>
        Assert.Equal(expected, ColumnAutoFit.Fit(measured));

    [Theory]
    [InlineData(50, 0)]
    [InlineData(-5, -5)]
    [InlineData(330, 30)]
    public void Overshoot_is_zero_inside_and_signed_outside(double position, double expected) =>
        Assert.Equal(expected, EdgeScroll.Overshoot(position, 0, 300));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    [InlineData(30, 2)]
    [InlineData(-100, -5)]
    [InlineData(5000, EdgeScroll.MaxStep)]
    public void Row_step_grows_with_distance_and_is_capped(double overshoot, int expected) =>
        Assert.Equal(expected, EdgeScroll.Step(overshoot));

    [Fact]
    public void Column_rate_near_edge_moves_one_column_every_four_ticks()
    {
        var carry = 0.0;
        var moved = Enumerable.Range(0, 8).Select(_ => EdgeScroll.TakeWhole(ref carry, EdgeScroll.ColumnRate(1))).ToArray();

        Assert.Equal(2, moved.Sum());
        Assert.Equal(EdgeScroll.MaxColumnRate, EdgeScroll.ColumnRate(10_000));
    }

    [Fact]
    public void Column_carry_resets_when_direction_changes()
    {
        var carry = 0.0;
        EdgeScroll.TakeWhole(ref carry, 0.75);

        Assert.Equal(0, EdgeScroll.TakeWhole(ref carry, -0.5)); // không dùng 0,75 cũ để bù
        Assert.Equal(-1, EdgeScroll.TakeWhole(ref carry, -0.5));
    }
}
