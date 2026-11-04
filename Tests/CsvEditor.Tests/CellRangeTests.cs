using System.Globalization;
using CsvEditor.Models;
using CsvEditor.Services;
using CsvEditor.ViewModels;

namespace CsvEditor.Tests;

public sealed class CellRangeTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private async Task<CsvEditorViewModel> OpenedAsync()
    {
        var vm = new CsvEditorViewModel(Csv.FileService(), new CsvEditService());
        var path = _dir.Write("r.csv", "A,B,C\n1,2,x\n3,,y\n5,6,z\n", Csv.Utf8NoBom);
        await vm.OpenAsync(path, _ => Task.FromResult(true), CancellationToken.None);
        return vm;
    }

    private static CellRange Range(int row1, int column1, int row2, int column2) =>
        new(new CellRef(row1, column1), new CellRef(row2, column2));

    private static string[][] Table(CsvEditorViewModel vm) =>
        vm.ViewRows.Select(row => Enumerable.Range(0, 3).Select(row.GetCell).ToArray()).ToArray();

    [Fact]
    public void Range_is_normalized_whichever_way_it_was_dragged()
    {
        var range = Range(5, 7, 2, 3);

        Assert.Equal((2, 5, 3, 7), (range.Top, range.Bottom, range.Left, range.Right));
        Assert.True(range.Contains(new CellRef(3, 4)));
        Assert.False(range.Contains(new CellRef(6, 4)));
        Assert.False(range.IsSingleCell);
        Assert.True(Range(1, 1, 1, 1).IsSingleCell);
    }

    [Fact]
    public async Task Statistics_count_non_empty_and_sum_numbers_only()
    {
        var vm = await OpenedAsync();

        var stats = vm.ComputeRangeStatistics(Range(0, 0, 2, 2));

        Assert.Equal(8, stats.Count);
        Assert.Equal(5, stats.NumericCount);
        Assert.Equal(17, stats.Sum);
        Assert.Equal(3.4, stats.Average);
    }

    [Fact]
    public async Task Statistics_skip_hidden_columns()
    {
        var vm = await OpenedAsync();
        vm.SetHiddenColumns([1]);

        var stats = vm.ComputeRangeStatistics(Range(0, 0, 2, 1));

        Assert.Equal(3, stats.Count);
        Assert.Equal(9, stats.Sum);
    }

    [Fact]
    public void Description_hides_sum_when_there_are_no_numbers()
    {
        var invariant = CultureInfo.InvariantCulture;

        Assert.Equal("Đếm: 3", new RangeStatistics(3, 0, 0).Describe(invariant));
        Assert.Equal("Đếm: 1,200  ·  Tổng: 3,500.5  ·  TB: 2.5", new RangeStatistics(1200, 1400, 3500.5).Describe(invariant));
    }

    [Fact]
    public async Task Copy_gives_tab_separated_cells_without_hidden_columns()
    {
        var vm = await OpenedAsync();
        vm.SetHiddenColumns([1]);

        Assert.Equal("1\tx\r\n3\ty\r\n", vm.FormatRange(Range(0, 0, 1, 2)));
    }

    [Fact]
    public async Task Paste_writes_a_block_and_undoes_in_one_step()
    {
        var vm = await OpenedAsync();
        var before = Table(vm);

        var result = vm.PasteRange(new CellRef(1, 1), [["a", "b"], ["c", "d"]]);

        Assert.Equal(["3", "a", "b"], Table(vm)[1]);
        Assert.Equal(["5", "c", "d"], Table(vm)[2]);
        Assert.Equal((2, 2, 0, 0), (result.Rows, result.Columns, result.SkippedRows, result.SkippedColumns));
        Assert.Equal(new CellRef(2, 2), result.LastCell);
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, Table(vm));
    }

    [Fact]
    public async Task Paste_past_the_table_edge_reports_what_was_skipped()
    {
        var vm = await OpenedAsync();

        var result = vm.PasteRange(new CellRef(2, 2), [["a", "b"], ["c", "d"]]);

        Assert.Equal("a", Table(vm)[2][2]);
        Assert.Equal((1, 1, 1, 1), (result.Rows, result.Columns, result.SkippedRows, result.SkippedColumns));
    }

    [Fact]
    public async Task Paste_goes_around_hidden_columns()
    {
        var vm = await OpenedAsync();
        vm.SetHiddenColumns([1]);

        vm.PasteRange(new CellRef(0, 0), [["p", "q"]]);

        Assert.Equal(["p", "2", "q"], Table(vm)[0]);
    }

    [Fact]
    public async Task Paste_on_a_filtered_view_only_touches_rows_shown()
    {
        var vm = await OpenedAsync();
        var filter = new FilterExpression();
        filter.Conditions.Add(new FilterCondition { ColumnIndex = 2, Operator = FilterOperator.NotEqual, Value = "y" });
        vm.ApplyFilter(filter);

        vm.PasteRange(new CellRef(0, 0), [["a"], ["b"]]);
        vm.ApplyFilter(null);

        Assert.Equal(["a", "3", "b"], Table(vm).Select(row => row[0]));
    }

    [Fact]
    public async Task Fill_writes_one_value_to_the_visible_cells_of_the_range()
    {
        var vm = await OpenedAsync();
        vm.SetHiddenColumns([1]);

        Assert.Equal(4, vm.FillRange(Range(0, 0, 1, 2), "k"));

        Assert.Equal(["k", "2", "k"], Table(vm)[0]);
        Assert.Equal(["k", "", "k"], Table(vm)[1]);
        vm.UndoCommand.Execute(null);
        Assert.Equal(["1", "2", "x"], Table(vm)[0]);
    }

    [Fact]
    public async Task Undo_text_says_paste_not_replace()
    {
        var vm = await OpenedAsync();

        vm.PasteRange(new CellRef(0, 0), [["a", "b"]]);

        Assert.Contains("Dán", vm.UndoDescription);
    }
}
