using System.Text;
using FileTools.Core;

namespace FileTools.Tests;

public class TextMatcherTests
{
    private static TextMatcher M(string term, bool regex = false, bool matchCase = false, bool fold = false, bool all = false, params string[] more) =>
        new(new MatchOptions { Terms = [term, .. more], IsRegex = regex, MatchCase = matchCase, IgnoreDiacritics = fold, MatchAll = all });

    [Fact]
    public void Plain_text_ignores_case_by_default()
    {
        Assert.Equal(4, M("error").Find("[E] ERROR: disk"));
        Assert.Equal(-1, M("error", matchCase: true).Find("[E] ERROR: disk"));
    }

    [Fact]
    public void Diacritics_folding_keeps_positions()
    {
        var m = M("thanh toan", fold: true);

        Assert.Equal(9, m.Find("Khách đã thanh toán"));
        Assert.True(M("da giao", fold: true).IsMatch("Đã giao hàng"));
        Assert.False(M("da giao").IsMatch("Đã giao hàng"));
        Assert.Equal("Khach da thanh toan", TextMatcher.FoldDiacritics("Khách đã thanh toán"));
        Assert.Equal("受注テスト", TextMatcher.FoldDiacritics("受注テスト"));
    }

    [Fact]
    public void Any_or_all_terms()
    {
        Assert.True(M("error", all: false, more: "timeout").IsMatch("timeout after 30s"));
        Assert.False(M("error", all: true, more: "timeout").IsMatch("timeout after 30s"));
        Assert.True(M("error", all: true, more: "timeout").IsMatch("error: timeout"));
    }

    [Fact]
    public void Regex_and_invalid_regex()
    {
        Assert.Equal(5, M(@"\d{4}-\d{2}", regex: true).Find("date 2026-09 ok"));
        var ex = Assert.Throws<ArgumentException>(() => M("(unclosed", regex: true));
        Assert.StartsWith("Regex không hợp lệ", ex.Message);
        Assert.Throws<ArgumentException>(() => M(""));
    }
}

public class InspectorTests
{
    [Fact]
    public void Counts_lines_endings_and_longest_line()
    {
        using var temp = new TempDir();
        var path = temp.Write("f.log", "a\r\n\r\nlongest line\nx\r\ny");

        var r = FileInspector.Inspect(path);

        Assert.Equal(5, r.Lines);
        Assert.Equal(1, r.EmptyLines);
        Assert.Equal((3L, 1L, 0L), (r.CrLf, r.Lf, r.Cr));
        Assert.False(r.EndsWithNewline);
        Assert.Equal((12L, 3L), (r.LongestLineLength, r.LongestLineNumber));
        Assert.StartsWith("Lẫn lộn", r.NewlineStyle);
        Assert.Null(r.Delimiter);
    }

    [Fact]
    public void Csv_reports_columns_and_mismatched_records()
    {
        using var temp = new TempDir();
        var path = temp.Write("f.csv", "id,name,memo\r\n1,A,\"x\r\ny\"\r\n2,B\r\n3,C,z\r\n4,D,e,f\r\n");

        var r = FileInspector.Inspect(path);

        Assert.Equal(',', r.Delimiter);
        Assert.Equal(3, r.HeaderColumns);
        Assert.Equal(["id", "name", "memo"], r.HeaderNames);
        Assert.Equal(5, r.Records);
        Assert.Equal(6, r.Lines);
        Assert.Equal(2, r.MismatchedRecords);
        Assert.Equal([3L, 5L], r.MismatchSamples);
        Assert.Equal("CRLF (Windows)", r.NewlineStyle);
    }

    [Fact]
    public void Text_file_that_looks_like_a_table_gets_a_delimiter()
    {
        using var temp = new TempDir();

        Assert.Equal('\t', FileInspector.Inspect(temp.Write("f.txt", "a\tb\n1\t2\n3\t4\n")).Delimiter);
        Assert.Null(FileInspector.Inspect(temp.Write("g.txt", "hello, world\nplain text\n")).Delimiter);
    }
}

public class SearchTests
{
    [Fact]
    public void Finds_counts_and_exports_with_line_numbers()
    {
        using var temp = new TempDir();
        var src = temp.Write("app.log", "INFO start\r\nERROR disk full\r\nINFO ok\r\nerror again");

        var r = TextSearcher.Search(new SearchOptions
        {
            SourcePath = src,
            Match = new MatchOptions { Terms = ["error"] },
            ExportPath = temp.File("hits.txt"),
            ExportLineNumbers = true,
        });

        Assert.Equal(2, r.TotalMatches);
        Assert.Equal(4, r.LinesScanned);
        Assert.Equal([2L, 4L], r.Hits.Select(h => h.LineNumber));
        Assert.Equal(0, r.Hits[0].Column);
        Assert.Equal("2: ERROR disk full\r\n4: error again\r\n", File.ReadAllText(temp.File("hits.txt")));
    }

    [Fact]
    public void Keeps_at_most_max_results_but_counts_all()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.txt", Text.Lines(1000));

        var r = TextSearcher.Search(new SearchOptions { SourcePath = src, Match = new MatchOptions { Terms = ["line"] }, MaxResults = 10 });

        Assert.Equal(1000, r.TotalMatches);
        Assert.Equal(10, r.Hits.Count);
        Assert.True(r.Truncated);
    }

    [Fact]
    public void Very_long_line_is_clipped_around_the_match()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.txt", new string('x', 5000) + "NEEDLE" + new string('y', 5000));

        var hit = Assert.Single(TextSearcher.Search(new SearchOptions { SourcePath = src, Match = new MatchOptions { Terms = ["needle"] } }).Hits);

        Assert.Contains("NEEDLE", hit.Text);
        Assert.True(hit.Text.Length < 500);
        Assert.Equal(5000, hit.Column);
    }
}

public class FilterTests
{
    [Fact]
    public void Keep_or_remove_matching_lines()
    {
        using var temp = new TempDir();
        var src = temp.Write("app.log", "INFO a\r\nERROR b\r\nWARN c\r\nERROR d");

        var keep = LineFilter.Filter(new FilterOptions { SourcePath = src, OutputPath = temp.File("keep.log"), Match = new MatchOptions { Terms = ["error"] } });
        var drop = LineFilter.Filter(new FilterOptions { SourcePath = src, OutputPath = temp.File("drop.log"), Match = new MatchOptions { Terms = ["error", "warn"] }, KeepMatching = false });

        Assert.Equal((2L, 2L), (keep.Kept, keep.Removed));
        Assert.Equal("ERROR b\r\nERROR d", File.ReadAllText(temp.File("keep.log")));
        Assert.Equal((1L, 3L), (drop.Kept, drop.Removed));
        Assert.Equal("INFO a\r\n", File.ReadAllText(temp.File("drop.log")));
    }

    [Fact]
    public void Last_source_line_without_newline_is_ended_when_more_lines_follow_it()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.txt", "keep 1\r\ndrop\r\nkeep 2");

        LineFilter.Filter(new FilterOptions { SourcePath = src, OutputPath = temp.File("o.txt"), Match = new MatchOptions { Terms = ["keep"] } });

        Assert.Equal("keep 1\r\nkeep 2", File.ReadAllText(temp.File("o.txt")));
    }

    [Fact]
    public void Csv_keeps_header_and_whole_multiline_records()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "id,memo\r\n1,\"Hà Nội\r\nline 2\"\r\n2,HCM\r\n");

        var r = LineFilter.Filter(new FilterOptions
        {
            SourcePath = src,
            OutputPath = temp.File("o.csv"),
            Match = new MatchOptions { Terms = ["ha noi"], IgnoreDiacritics = true },
            KeepHeader = true,
        });

        Assert.Equal("id,memo\r\n1,\"Hà Nội\r\nline 2\"\r\n", File.ReadAllText(temp.File("o.csv")));
        Assert.Equal((2L, 1L), (r.Kept, r.Removed));
    }
}

public class RewriteTests
{
    [Fact]
    public void Converts_shift_jis_to_utf8_into_output_folder()
    {
        using var temp = new TempDir();
        var src = temp.Write("in/a.csv", "受注,テスト\r\n", Text.ShiftJis);

        var item = Assert.Single(FileRewriter.Rewrite(new RewriteOptions { Files = [src], OutputFolder = temp.File("out"), Encoding = OutputEncoding.Utf8 }));

        Assert.Equal(RewriteStatus.Written, item.Status);
        Assert.Equal(("Shift-JIS", "UTF-8"), (item.From, item.To));
        Assert.Equal(Text.Utf8.GetBytes("受注,テスト\r\n"), File.ReadAllBytes(temp.File("out/a.csv")));
        Assert.Equal(Text.ShiftJis.GetBytes("受注,テスト\r\n"), File.ReadAllBytes(src));
    }

    [Fact]
    public void Overwrite_in_place_keeps_a_bak()
    {
        using var temp = new TempDir();
        var src = temp.Write("a.txt", "x\r\ny\r\n");

        var item = Assert.Single(FileRewriter.Rewrite(new RewriteOptions { Files = [src], Newline = NewlineMode.Lf }));

        Assert.Equal(2, item.EndingsChanged);
        Assert.Equal("x\ny\n", File.ReadAllText(src));
        Assert.Equal("x\r\ny\r\n", File.ReadAllText(src + ".bak"));
        Assert.Equal(["a.txt", "a.txt.bak"], Directory.GetFiles(temp.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Nothing_to_change_is_skipped_without_bak()
    {
        using var temp = new TempDir();
        var src = temp.Write("a.txt", "x\ny\n");

        var item = Assert.Single(FileRewriter.Rewrite(new RewriteOptions { Files = [src], Newline = NewlineMode.Lf }));

        Assert.Equal(RewriteStatus.Unchanged, item.Status);
        Assert.Equal(["a.txt"], Directory.GetFiles(temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Chars_missing_in_shift_jis_are_counted()
    {
        using var temp = new TempDir();
        var src = temp.Write("a.txt", "受注 Tiếng Việt\r\n");

        var item = Assert.Single(FileRewriter.Rewrite(new RewriteOptions { Files = [src], OutputFolder = temp.File("out"), Encoding = OutputEncoding.ShiftJis }));

        // ế, ệ không có trong Shift-JIS (i, V, t thì có).
        Assert.Equal(2, item.LostChars);
        Assert.Equal("受注 Ti?ng Vi?t\r\n", File.ReadAllText(temp.File("out/a.txt"), Text.ShiftJis));
    }

    [Fact]
    public void Forced_source_encoding_and_failure_does_not_stop_the_batch()
    {
        using var temp = new TempDir();
        var good = temp.Write("good.txt", "受注\r\n", Text.ShiftJis);
        var missing = temp.File("missing.txt");

        var items = FileRewriter.Rewrite(new RewriteOptions
        {
            Files = [missing, good],
            OutputFolder = temp.File("out"),
            Encoding = OutputEncoding.Utf8Bom,
            SourceEncoding = Text.ShiftJis,
        });

        Assert.Equal(RewriteStatus.Failed, items[0].Status);
        Assert.NotNull(items[0].Error);
        Assert.Equal(RewriteStatus.Written, items[1].Status);
        Assert.Equal("受注\r\n", File.ReadAllText(temp.File("out/good.txt")));
        Assert.True(File.ReadAllBytes(temp.File("out/good.txt")).AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]));
    }

    [Fact]
    public void Output_folder_equal_to_source_folder_is_rejected()
    {
        using var temp = new TempDir();
        var src = temp.Write("a.txt", "x\r\n");

        var item = Assert.Single(FileRewriter.Rewrite(new RewriteOptions { Files = [src], OutputFolder = temp.Path, Newline = NewlineMode.Lf }));

        Assert.Equal(RewriteStatus.Failed, item.Status);
        Assert.Equal("x\r\n", File.ReadAllText(src));
    }
}

public class OutputLostCharsTests
{
    [Fact]
    public void Lost_chars_counted_once_even_when_measuring()
    {
        using var temp = new TempDir();
        using var output = new OutputFile(temp.File("o.txt"), Text.ShiftJis);
        var line = new TextLine("ệ", LineEnding.CrLf);

        output.MeasureLine(line);
        output.WriteLine(line);
        output.Commit();

        Assert.Equal(1, output.LostChars);
    }
}
