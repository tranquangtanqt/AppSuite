using CsvEditor.Models;
using CsvEditor.Services;

namespace CsvEditor.Tests;

/// <summary>Đi tới cột (Ctrl+Shift+G): tìm cột theo 1 phần tên / số thứ tự.</summary>
public sealed class ColumnFinderTests
{
    private static readonly CsvColumn[] Columns =
        new[] { "DATA_LNO", "CO_CD", "EXT_SYS_CD", "EXT_DATA_NO", "EXT_DATA_LNO", "DATA_MAKE_DT", "MY_EXT_DATA", "2024" }
            .Select(name => new CsvColumn(name)).ToArray();

    private static int[] Find(string query) => ColumnFinder.Find(Columns, query).ToArray();

    [Fact]
    public void Part_of_name_case_insensitive_starts_with_before_contains() =>
        Assert.Equal([3, 4, 6], Find("ext_data"));

    [Fact]
    public void Exact_name_comes_first() =>
        Assert.Equal([2, 1, 0], ColumnFinder.Find([new CsvColumn("X_CODE"), new CsvColumn("CODE_NM"), new CsvColumn("code")], "CODE").ToArray());

    [Fact]
    public void Number_is_column_position_first_then_names_containing_it() =>
        Assert.Equal([1, 7], Find("2")); // cột thứ 2 (CO_CD) trước, rồi cột tên "2024"

    [Fact]
    public void Exact_name_beats_position() =>
        Assert.Equal([2, 1], ColumnFinder.Find([new CsvColumn("A"), new CsvColumn("B"), new CsvColumn("2")], "2").ToArray());

    [Fact]
    public void Number_matching_a_name_finds_that_name() =>
        Assert.Equal([7], Find("2024"));

    [Fact]
    public void Number_out_of_range_or_no_match_is_empty()
    {
        Assert.Empty(Find("99"));
        Assert.Empty(Find("KHONG_CO"));
        Assert.Empty(Find("   "));
    }
}
