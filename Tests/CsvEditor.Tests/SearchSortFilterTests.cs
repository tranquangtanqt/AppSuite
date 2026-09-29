using CsvEditor.Models;
using CsvEditor.Services;

namespace CsvEditor.Tests;

public sealed class SearchTests
{
    private static readonly CsvDocument Doc = Csv.Doc(
        ["Name", "City"],
        ["Apple", "Hanoi"],
        ["pineapple", "HCM"],
        ["Banana", "hanoi"]);

    private static List<CellRef> Find(string query, SearchMode mode, bool caseSensitive = false) =>
        new SearchService().Find(Doc.Rows, query, mode, caseSensitive);

    [Fact]
    public void Contains_is_case_insensitive_by_default() =>
        Assert.Equal([new CellRef(0, 0), new CellRef(1, 0)], Find("apple", SearchMode.Contains));

    [Fact]
    public void Contains_case_sensitive() =>
        Assert.Equal([new CellRef(1, 0)], Find("apple", SearchMode.Contains, caseSensitive: true));

    [Fact]
    public void Equals_matches_whole_cell() =>
        Assert.Equal([new CellRef(0, 1), new CellRef(2, 1)], Find("HANOI", SearchMode.Equals));

    [Fact]
    public void StartsWith_and_EndsWith()
    {
        Assert.Equal([new CellRef(1, 0)], Find("pine", SearchMode.StartsWith));
        Assert.Equal([new CellRef(0, 0), new CellRef(1, 0)], Find("ple", SearchMode.EndsWith));
    }

    [Fact]
    public void Regex_mode() =>
        Assert.Equal([new CellRef(0, 1), new CellRef(1, 1), new CellRef(2, 1)], Find("^h", SearchMode.Regex));

    [Fact]
    public void Empty_query_finds_nothing() => Assert.Empty(Find("", SearchMode.Contains));

    [Fact]
    public void Invalid_regex_throws_argument_exception() =>
        Assert.ThrowsAny<ArgumentException>(() => Find("(", SearchMode.Regex));
}

public sealed class SortTests
{
    private static string[] Col(IEnumerable<CsvRow> rows, int column) => rows.Select(r => r.GetCell(column)).ToArray();

    [Fact]
    public void Numbers_sort_numerically_not_as_text()
    {
        var doc = Csv.Doc(["N"], ["10"], ["9"], ["100"], ["-1"], ["2.5"]);

        var sorted = new SortService().ApplyMultiColumn(doc.Rows, [(0, false)]);

        Assert.Equal(["-1", "2.5", "9", "10", "100"], Col(sorted, 0));
    }

    [Fact]
    public void Text_sorts_case_insensitive_descending()
    {
        var doc = Csv.Doc(["S"], ["banana"], ["Apple"], ["cherry"]);

        var sorted = new SortService().ApplyMultiColumn(doc.Rows, [(0, true)]);

        Assert.Equal(["cherry", "banana", "Apple"], Col(sorted, 0));
    }

    [Fact]
    public void Multi_column_uses_second_key_for_ties()
    {
        var doc = Csv.Doc(["City", "Age"], ["HN", "30"], ["HCM", "20"], ["HN", "25"], ["HCM", "40"]);

        var sorted = new SortService().ApplyMultiColumn(doc.Rows, [(0, false), (1, true)]).ToList();

        Assert.Equal(["HCM", "HCM", "HN", "HN"], Col(sorted, 0));
        Assert.Equal(["40", "20", "30", "25"], Col(sorted, 1));
    }

    [Fact]
    public void Sort_does_not_change_document_order()
    {
        var doc = Csv.Doc(["N"], ["3"], ["1"], ["2"]);

        _ = new SortService().ApplyMultiColumn(doc.Rows, [(0, false)]).ToList();

        Assert.Equal(["3", "1", "2"], Col(doc.Rows, 0));
    }

    [Fact]
    public void Empty_spec_keeps_order()
    {
        var doc = Csv.Doc(["N"], ["3"], ["1"]);
        Assert.Equal(["3", "1"], Col(new SortService().ApplyMultiColumn(doc.Rows, []), 0));
    }
}

public sealed class FilterTests
{
    private static readonly CsvDocument Doc = Csv.Doc(
        ["Name", "Age", "City"],
        ["An", "30", "Hanoi"],
        ["Binh", "25", "HCM"],
        ["Chi", "9", "Hanoi"],
        ["Dung", "", "Da Nang"]);

    private static string[] Names(FilterExpression expression)
    {
        var predicate = new FilterService().Compile(expression);
        return Doc.Rows.Where(predicate).Select(r => r.GetCell(0)).ToArray();
    }

    private static FilterExpression Where(FilterJoin join, params FilterCondition[] conditions)
    {
        var expression = new FilterExpression { Join = join };
        expression.Conditions.AddRange(conditions);
        return expression;
    }

    private static FilterCondition Cond(int column, FilterOperator op, string value, bool not = false) =>
        new() { ColumnIndex = column, Operator = op, Value = value, Negate = not };

    [Theory]
    [InlineData(FilterOperator.Equal, "30", new[] { "An" })]
    [InlineData(FilterOperator.NotEqual, "30", new[] { "Binh", "Chi", "Dung" })]
    [InlineData(FilterOperator.GreaterThan, "10", new[] { "An", "Binh" })]      // số: 9 < 10 (chuỗi thì "9" > "10")
    [InlineData(FilterOperator.LessThan, "10", new[] { "Chi", "Dung" })]        // ô rỗng so như chuỗi: "" < "10"
    [InlineData(FilterOperator.GreaterOrEqual, "25", new[] { "An", "Binh" })]
    [InlineData(FilterOperator.LessOrEqual, "25", new[] { "Binh", "Chi", "Dung" })]
    public void Comparison_operators_on_numbers(FilterOperator op, string value, string[] expected) =>
        Assert.Equal(expected, Names(Where(FilterJoin.And, Cond(1, op, value))));

    [Fact]
    public void Equal_on_text_is_case_insensitive() =>
        Assert.Equal(["An", "Chi"], Names(Where(FilterJoin.And, Cond(2, FilterOperator.Equal, "HANOI"))));

    [Fact]
    public void Contains_and_regex()
    {
        Assert.Equal(["Dung"], Names(Where(FilterJoin.And, Cond(2, FilterOperator.Contains, "nang"))));
        Assert.Equal(["Binh", "Dung"], Names(Where(FilterJoin.And, Cond(0, FilterOperator.Regex, "^(b|d)"))));
    }

    [Fact]
    public void Not_negates_condition() =>
        Assert.Equal(["Binh", "Dung"], Names(Where(FilterJoin.And, Cond(2, FilterOperator.Equal, "Hanoi", not: true))));

    [Fact]
    public void And_requires_all_conditions() =>
        Assert.Equal(["An"], Names(Where(FilterJoin.And,
            Cond(2, FilterOperator.Equal, "Hanoi"),
            Cond(1, FilterOperator.GreaterThan, "10"))));

    [Fact]
    public void Or_requires_any_condition() =>
        Assert.Equal(["An", "Binh", "Chi"], Names(Where(FilterJoin.Or,
            Cond(2, FilterOperator.Equal, "Hanoi"),
            Cond(1, FilterOperator.Equal, "25"))));

    [Fact]
    public void No_conditions_keeps_every_row() =>
        Assert.Equal(["An", "Binh", "Chi", "Dung"], Names(new FilterExpression()));
}
