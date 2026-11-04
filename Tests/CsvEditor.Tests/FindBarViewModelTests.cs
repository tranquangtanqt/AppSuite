using CsvEditor.Models;
using CsvEditor.Services;
using CsvEditor.ViewModels;

namespace CsvEditor.Tests;

/// <summary>Thanh tìm không chặn bảng: bảng đổi lúc đang tìm thì kết quả phải tự cập nhật, nhất là Thay thế tất cả
/// không được ghi theo vị trí dòng cũ.</summary>
public sealed class FindBarViewModelTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private async Task<CsvEditorViewModel> OpenedAsync()
    {
        var vm = new CsvEditorViewModel(Csv.FileService(), new CsvEditService());
        var path = _dir.Write("f.csv", "Tên,Số\nAn,3\nBình,1\nChi,2\nAn Hoà,4\n", Csv.Utf8NoBom);
        await vm.OpenAsync(path, _ => Task.FromResult(true), CancellationToken.None);
        return vm;
    }

    private static FilterExpression NameEquals(string value)
    {
        var filter = new FilterExpression();
        filter.Conditions.Add(new FilterCondition { ColumnIndex = 0, Operator = FilterOperator.Equal, Value = value });
        return filter;
    }

    [Fact]
    public async Task Empty_query_shows_no_message()
    {
        var vm = await OpenedAsync();

        vm.Find(string.Empty, SearchMode.Contains, false);

        Assert.Equal(string.Empty, vm.MatchStatusMessage);
        Assert.Null(vm.CurrentMatch);
    }

    [Fact]
    public async Task Half_typed_regex_is_reported_instead_of_throwing()
    {
        var vm = await OpenedAsync();

        vm.Find("(", SearchMode.Regex, false);

        Assert.Equal("Regex không hợp lệ.", vm.MatchStatusMessage);
        Assert.Null(vm.CurrentMatch);
    }

    [Fact]
    public async Task Replace_after_sort_writes_the_cells_that_match_now()
    {
        var vm = await OpenedAsync();
        vm.Find("Bình", SearchMode.Equals, false);
        Assert.Equal(new CellRef(1, 0), vm.CurrentMatch);

        vm.ApplySort([(1, false)]); // Bình (số 1) lên dòng đầu; kết quả cũ trỏ dòng 2, giờ là An

        Assert.Equal(new CellRef(0, 0), vm.CurrentMatch);
        Assert.Equal(1, vm.ReplaceAllMatches("Bảo"));
        Assert.Equal(["Bảo", "Chi", "An", "An Hoà"], vm.ViewRows.Select(row => row.GetCell(0)));
    }

    [Fact]
    public async Task Filter_changes_the_match_count_and_clearing_it_brings_them_back()
    {
        var vm = await OpenedAsync();
        vm.Find("An", SearchMode.Contains, false);
        Assert.Equal("Kết quả 1/2", vm.MatchStatusMessage);

        vm.ApplyFilter(NameEquals("Chi"));
        Assert.Equal("Không tìm thấy kết quả.", vm.MatchStatusMessage);
        Assert.Equal(0, vm.ReplaceAllMatches("x"));

        vm.ApplyFilter(null);
        Assert.Equal("Kết quả 1/2", vm.MatchStatusMessage);
    }

    [Fact]
    public async Task Editing_a_cell_updates_matches_and_keeps_the_current_one()
    {
        var vm = await OpenedAsync();
        vm.Find("An", SearchMode.Contains, false);
        vm.NextMatch();
        Assert.Equal(new CellRef(3, 0), vm.CurrentMatch);

        vm.SetCell(1, 0, "Anh");

        Assert.Equal(new CellRef(3, 0), vm.CurrentMatch);
        Assert.Equal("Kết quả 3/3", vm.MatchStatusMessage);
    }

    [Fact]
    public async Task Current_match_edited_away_moves_to_the_nearest_remaining_one()
    {
        var vm = await OpenedAsync();
        vm.Find("An", SearchMode.Contains, false);
        vm.NextMatch();

        vm.SetCell(3, 0, "Hoà");

        Assert.Equal(new CellRef(0, 0), vm.CurrentMatch);
        Assert.Equal("Kết quả 1/1", vm.MatchStatusMessage);
    }

    [Fact]
    public async Task Replace_all_reports_count_and_searches_the_new_content()
    {
        var vm = await OpenedAsync();
        vm.Find("An", SearchMode.Contains, false);

        Assert.Equal(2, vm.ReplaceAllMatches("Ba"));

        Assert.Equal("Không tìm thấy kết quả.", vm.MatchStatusMessage);
        vm.UndoCommand.Execute(null);
        Assert.Equal("Kết quả 1/2", vm.MatchStatusMessage);
    }

    [Fact]
    public async Task Closing_the_bar_stops_tracking()
    {
        var vm = await OpenedAsync();
        vm.Find("An", SearchMode.Contains, false);

        vm.ClearFind();
        vm.ApplySort([(1, false)]);

        Assert.Equal(string.Empty, vm.MatchStatusMessage);
        Assert.Null(vm.CurrentMatch);
    }

    [Fact]
    public async Task Hidden_columns_are_not_searched_or_replaced()
    {
        var vm = await OpenedAsync();
        vm.Find("3", SearchMode.Contains, false);
        Assert.Equal("Kết quả 1/1", vm.MatchStatusMessage);

        vm.SetHiddenColumns([1]);

        Assert.Equal("Không tìm thấy kết quả.", vm.MatchStatusMessage);
        Assert.Equal(0, vm.ReplaceAllMatches("x"));
        Assert.Equal("3", vm.ViewRows[0].GetCell(1));

        vm.SetHiddenColumns([]);
        Assert.Equal("Kết quả 1/1", vm.MatchStatusMessage);
    }

    [Theory]
    [InlineData("an", SearchMode.Contains, false, "em", "An Bình an", "em Bình em")]
    [InlineData("an", SearchMode.Contains, true, "em", "An Bình an", "An Bình em")]
    [InlineData("an", SearchMode.StartsWith, false, "em", "An an", "em an")]
    [InlineData("an", SearchMode.EndsWith, false, "em", "An an", "An em")]
    [InlineData("An", SearchMode.Equals, false, "em", "an", "em")]
    [InlineData(@"(\d+)-(\d+)", SearchMode.Regex, false, "$2/$1", "a 10-2026 b", "a 2026/10 b")]
    public void Replace_changes_only_the_matched_text(string query, SearchMode mode, bool caseSensitive, string replacement, string cell, string expected)
    {
        Assert.Equal(expected, SearchService.CreateReplacer(query, mode, caseSensitive, replacement)(cell));
    }

    [Fact]
    public async Task Replace_all_keeps_the_rest_of_the_cell_and_undoes_in_one_step()
    {
        var vm = await OpenedAsync();
        vm.Find("An", SearchMode.Contains, false);

        Assert.Equal(2, vm.ReplaceAllMatches("Ba"));

        Assert.Equal(["Ba", "Bình", "Chi", "Ba Hoà"], vm.ViewRows.Select(row => row.GetCell(0)));
        vm.UndoCommand.Execute(null);
        Assert.Equal(["An", "Bình", "Chi", "An Hoà"], vm.ViewRows.Select(row => row.GetCell(0)));
    }

    [Fact]
    public async Task Replacing_with_text_that_still_matches_keeps_it_found()
    {
        var vm = await OpenedAsync();
        vm.Find("An", SearchMode.Contains, false);

        vm.ReplaceAllMatches("Anh");

        Assert.Equal("Kết quả 1/2", vm.MatchStatusMessage);
    }

    [Fact]
    public async Task Replacing_with_nothing_removes_the_matched_text()
    {
        var vm = await OpenedAsync();
        vm.Find(" Hoà", SearchMode.Contains, false);

        vm.ReplaceAllMatches(string.Empty);

        Assert.Equal("An", vm.ViewRows[3].GetCell(0));
    }
}
