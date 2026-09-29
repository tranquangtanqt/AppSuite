using CsvEditor.Services;

namespace CsvEditor.Tests;

public sealed class StatisticsTests
{
    private static readonly Models.TableStatistics Stats = new StatisticsService().Compute(
        Csv.Doc(
            ["Name", "Score", "Note"],
            ["An", "10", "x"],
            ["an", "20", ""],
            ["Binh", "abc", ""],
            ["An", "10", "x"],
            ["Chi", "-4.5", ""]),
        null, CancellationToken.None);

    [Fact]
    public void Row_and_column_count()
    {
        Assert.Equal(5, Stats.RowCount);
        Assert.Equal(3, Stats.ColumnCount);
    }

    [Fact]
    public void Duplicate_rows_count_exact_repeats_only() =>
        Assert.Equal(1, Stats.DuplicateRowCount); // "An,10,x" lặp; "an,20," khác

    [Fact]
    public void Empty_cells_per_column()
    {
        Assert.Equal([0, 0, 3], Stats.PerColumn.Select(c => c.EmptyCount).ToArray());
    }

    [Fact]
    public void Unique_is_case_insensitive_and_ignores_empty()
    {
        Assert.Equal(3, Stats.PerColumn[0].UniqueCount); // An/an, Binh, Chi
        Assert.Equal(1, Stats.PerColumn[2].UniqueCount); // chỉ "x"
    }

    [Fact]
    public void Numeric_min_max_sum_average_skip_text_cells()
    {
        var score = Stats.PerColumn[1];
        Assert.Equal(-4.5, score.Min);
        Assert.Equal(20, score.Max);
        Assert.Equal(35.5, score.Sum);
        Assert.Equal(35.5 / 4, score.Average); // 4 ô số, "abc" bỏ qua
    }

    [Fact]
    public void Text_only_column_has_no_numeric_stats()
    {
        var name = Stats.PerColumn[0];
        Assert.Null(name.Sum);
        Assert.Null(name.Average);
        Assert.Null(name.Min);
        Assert.Null(name.Max);
    }

    [Fact]
    public void Empty_document()
    {
        var stats = new StatisticsService().Compute(Csv.Doc(["A"]), null, CancellationToken.None);
        Assert.Equal(0, stats.RowCount);
        Assert.Equal(0, stats.DuplicateRowCount);
        Assert.Null(stats.PerColumn[0].Sum);
    }

    [Fact]
    public void Honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new StatisticsService().Compute(Csv.Doc(["A"], ["1"]), null, cts.Token));
    }
}
