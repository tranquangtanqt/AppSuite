using System.Text;
using FileTools.Core;

namespace FileTools.Tests;

public class MergeTests
{
    [Fact]
    public void Merges_in_natural_order_and_adds_missing_newline()
    {
        using var temp = new TempDir();
        temp.Write("in/log10.txt", "c1\r\n");
        temp.Write("in/log2.txt", "b1\r\nb2");          // không có xuống dòng cuối
        temp.Write("in/log1.txt", "a1\r\n");
        var files = FileListing.List(temp.File("in"), "*.txt", false, FileSortOrder.NameNatural).Select(f => f.FullName).ToList();

        var result = FileMerger.Merge(new MergeOptions { Files = files, OutputPath = temp.File("out.txt") });

        Assert.Equal("a1\r\nb1\r\nb2\r\nc1\r\n", File.ReadAllText(temp.File("out.txt")));
        Assert.Equal(3, result.Files);
        Assert.Equal(4, result.Lines);
    }

    [Fact]
    public void Mixed_encodings_are_written_as_one_utf8_file()
    {
        using var temp = new TempDir();
        var a = temp.Write("a.txt", "受注\r\n", Text.ShiftJis);
        var b = temp.Write("b.txt", "Tiếng Việt\r\n", Text.Utf8Bom);
        var c = temp.Write("c.txt", "テスト\r\n", new UnicodeEncoding(false, true));

        FileMerger.Merge(new MergeOptions { Files = [a, b, c], OutputPath = temp.File("out.txt") });

        var bytes = File.ReadAllBytes(temp.File("out.txt"));
        Assert.False(bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]));
        Assert.Equal("受注\r\nTiếng Việt\r\nテスト\r\n", Text.Utf8.GetString(bytes));
    }

    [Fact]
    public void Output_can_be_shift_jis_or_same_as_first_source()
    {
        using var temp = new TempDir();
        var a = temp.Write("a.txt", "受注\r\n", Text.Utf8Bom);

        FileMerger.Merge(new MergeOptions { Files = [a], OutputPath = temp.File("sjis.txt"), Encoding = OutputEncoding.ShiftJis });
        FileMerger.Merge(new MergeOptions { Files = [a], OutputPath = temp.File("same.txt"), Encoding = OutputEncoding.SameAsSource });

        Assert.Equal(Text.ShiftJis.GetBytes("受注\r\n"), File.ReadAllBytes(temp.File("sjis.txt")));
        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(temp.File("same.txt")));
    }

    [Fact]
    public void Csv_header_is_kept_once_and_a_different_header_is_reported()
    {
        using var temp = new TempDir();
        var a = temp.Write("a.csv", "id,name\r\n1,A\r\n");
        var b = temp.Write("b.csv", "id,name\r\n2,\"B\r\nB2\"\r\n");
        var c = temp.Write("c.csv", "ID,NAME\r\n3,C\r\n");

        var result = FileMerger.Merge(new MergeOptions { Files = [a, b, c], OutputPath = temp.File("out.csv"), CsvHeaderOnce = true });

        Assert.Equal("id,name\r\n1,A\r\n2,\"B\r\nB2\"\r\nID,NAME\r\n3,C\r\n", File.ReadAllText(temp.File("out.csv")));
        Assert.Contains(result.Notes, n => n.StartsWith("c.csv: dòng tiêu đề khác"));
    }

    [Fact]
    public void File_name_headers_blank_lines_and_newline_conversion()
    {
        using var temp = new TempDir();
        var a = temp.Write("a.txt", "x\r\n");
        var b = temp.Write("b.txt", "y\n");

        FileMerger.Merge(new MergeOptions
        {
            Files = [a, b],
            OutputPath = temp.File("out.txt"),
            FileNameHeader = true,
            BlankLineBetween = true,
            Newline = NewlineMode.Lf,
        });

        Assert.Equal("===== a.txt =====\nx\n\n===== b.txt =====\ny\n", File.ReadAllText(temp.File("out.txt")));
    }

    [Fact]
    public void Output_inside_the_source_list_is_skipped()
    {
        using var temp = new TempDir();
        var a = temp.Write("a.txt", "x\r\n");
        var output = temp.Write("out.txt", "old\r\n");

        var result = FileMerger.Merge(new MergeOptions { Files = [a, output], OutputPath = output });

        Assert.Equal("x\r\n", File.ReadAllText(output));
        Assert.Single(result.Notes);
    }

    [Fact]
    public void Cancel_leaves_no_output()
    {
        using var temp = new TempDir();
        var a = temp.Write("a.txt", Text.Lines(5000));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            FileMerger.Merge(new MergeOptions { Files = [a], OutputPath = temp.File("out.txt") }, null, cts.Token));
        Assert.False(File.Exists(temp.File("out.txt")));
        Assert.False(File.Exists(temp.File("out.txt.partial")));
    }

    [Fact]
    public void Listing_filters_patterns_recursively_and_skips_lock_files()
    {
        using var temp = new TempDir();
        temp.Write("a.csv", "");
        temp.Write("b.txt", "");
        temp.Write("c.log", "");
        temp.Write("~$a.csv", "");
        temp.Write("sub/d.csv", "");

        var top = FileListing.List(temp.Path, "*.csv;*.txt", false, FileSortOrder.NameNatural).Select(f => f.Name);
        var all = FileListing.List(temp.Path, "*.csv", true, FileSortOrder.NameNatural).Select(f => f.Name);

        Assert.Equal(["a.csv", "b.txt"], top);
        Assert.Equal(["a.csv", "d.csv"], all);
    }
}

public class SplitTests
{
    private static string Concat(IEnumerable<SplitPart> parts) =>
        string.Concat(parts.Select(p => File.ReadAllText(p.Path)));

    [Fact]
    public void By_lines_parts_rejoin_to_the_original()
    {
        using var temp = new TempDir();
        var content = Text.Lines(25, trailing: false);
        var src = temp.Write("data.txt", content);

        var result = FileSplitter.Split(new SplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Mode = SplitMode.ByLines, Lines = 10 });

        Assert.Equal([10L, 10, 5], result.Parts.Select(p => p.Lines));
        Assert.Equal(["data.part001.txt", "data.part002.txt", "data.part003.txt"], result.Parts.Select(p => Path.GetFileName(p.Path)));
        Assert.Equal(content, Concat(result.Parts));
    }

    [Fact]
    public void By_size_never_exceeds_limit_and_never_cuts_a_line()
    {
        using var temp = new TempDir();
        var content = Text.Lines(1000);
        var src = temp.Write("data.txt", content);

        var result = FileSplitter.Split(new SplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Mode = SplitMode.BySize, SizeBytes = 1000 });

        Assert.All(result.Parts, p =>
        {
            Assert.True(new FileInfo(p.Path).Length <= 1000);
            Assert.Equal(p.Bytes, new FileInfo(p.Path).Length);
            Assert.EndsWith("\r\n", File.ReadAllText(p.Path));
        });
        Assert.Equal(content, Concat(result.Parts));
    }

    [Fact]
    public void Line_longer_than_the_size_limit_still_goes_into_its_own_part()
    {
        using var temp = new TempDir();
        var src = temp.Write("data.txt", "short\r\n" + new string('x', 500) + "\r\nend\r\n");

        var result = FileSplitter.Split(new SplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Mode = SplitMode.BySize, SizeBytes = 100 });

        Assert.Equal(3, result.Parts.Count);
        Assert.Equal(1, result.Parts[1].Lines);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void By_parts_gives_exactly_n_roughly_equal_parts(int n)
    {
        using var temp = new TempDir();
        var content = Text.Lines(1000);
        var src = temp.Write("data.txt", content);

        var result = FileSplitter.Split(new SplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Mode = SplitMode.ByParts, Parts = n });

        Assert.Equal(n, result.Parts.Count);
        long size = new FileInfo(src).Length;
        Assert.All(result.Parts, p => Assert.InRange(p.Bytes, size / n - 20, size / n + 20));
        Assert.Equal(content, Concat(result.Parts));
    }

    [Fact]
    public void Csv_header_repeats_and_quoted_newlines_are_not_cut()
    {
        using var temp = new TempDir();
        var src = temp.Write("data.csv", "id,memo\r\n1,\"a\r\nb\"\r\n2,x\r\n3,\"c\r\nd\"\r\n");

        var result = FileSplitter.Split(new SplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Mode = SplitMode.ByLines, Lines = 2, RepeatHeader = true });

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal("id,memo\r\n1,\"a\r\nb\"\r\n2,x\r\n", File.ReadAllText(result.Parts[0].Path));
        Assert.Equal("id,memo\r\n3,\"c\r\nd\"\r\n", File.ReadAllText(result.Parts[1].Path));
        Assert.Equal(3, result.Lines);
    }

    [Fact]
    public void Output_encoding_can_differ_from_source()
    {
        using var temp = new TempDir();
        var src = temp.Write("data.txt", "受注\r\nテスト\r\n", Text.ShiftJis);

        var result = FileSplitter.Split(new SplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Mode = SplitMode.ByLines, Lines = 1 });

        Assert.Equal("受注\r\n", File.ReadAllText(result.Parts[0].Path, Text.Utf8));
    }

    [Fact]
    public void Empty_file_makes_no_parts()
    {
        using var temp = new TempDir();
        var src = temp.Write("data.txt", "");

        var result = FileSplitter.Split(new SplitOptions { SourcePath = src, OutputFolder = temp.File("out") });

        Assert.Empty(result.Parts);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Cancel_removes_every_part()
    {
        using var temp = new TempDir();
        var src = temp.Write("data.txt", Text.Lines(20_000));
        using var cts = new CancellationTokenSource();
        // Huỷ ở lần báo tiến độ đầu (sau 1024 dòng) → phần 1 đã ghi xong, đang ghi phần 2.
        var progress = new CancelAfter(cts, 0);

        Assert.ThrowsAny<OperationCanceledException>(() => FileSplitter.Split(
            new SplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Mode = SplitMode.ByLines, Lines = 1000 }, progress, cts.Token));
        Assert.Empty(Directory.GetFiles(temp.File("out")));
    }

    /// <summary>Huỷ khi tiến độ vượt 1 mốc (đồng bộ, không qua SynchronizationContext).</summary>
    private sealed class CancelAfter(CancellationTokenSource cts, double at) : IProgress<JobProgress>
    {
        public void Report(JobProgress value)
        {
            if (value.Fraction >= at)
            {
                cts.Cancel();
            }
        }
    }
}

public class ExtractTests
{
    private static ExtractResult Run(TempDir temp, string src, ExtractOptions o) =>
        LineExtractor.Extract(o with { SourcePath = src, OutputPath = temp.File("out.txt") });

    private static readonly ExtractOptions Base = new() { SourcePath = "", OutputPath = "" };

    [Fact]
    public void Head_takes_first_n_lines()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.txt", Text.Lines(100));

        var r = Run(temp, src, Base with { Mode = ExtractMode.Head, Count = 3 });

        Assert.Equal("line 1\r\nline 2\r\nline 3\r\n", File.ReadAllText(temp.File("out.txt")));
        Assert.Equal((3L, 1L), (r.Lines, r.FirstLineNumber!.Value));
    }

    [Fact]
    public void Range_with_csv_header()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.txt", Text.Lines(100));

        Run(temp, src, Base with { Mode = ExtractMode.Range, From = 50, To = 52, IncludeHeader = true });

        Assert.Equal("line 1\r\nline 50\r\nline 51\r\nline 52\r\n", File.ReadAllText(temp.File("out.txt")));
    }

    [Fact]
    public void Range_past_the_end_writes_nothing()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.txt", Text.Lines(10));

        var r = Run(temp, src, Base with { Mode = ExtractMode.Range, From = 20, To = 30 });

        Assert.Equal(0, r.Lines);
        Assert.Null(r.FirstLineNumber);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tail_takes_last_n_lines_with_or_without_trailing_newline(bool trailing)
    {
        using var temp = new TempDir();
        var src = temp.Write("f.txt", Text.Lines(100, "\n", trailing));

        Run(temp, src, Base with { Mode = ExtractMode.Tail, Count = 3 });

        Assert.Equal(trailing ? "line 98\nline 99\nline 100\n" : "line 98\nline 99\nline 100", File.ReadAllText(temp.File("out.txt")));
    }

    [Fact]
    public void Tail_larger_than_file_returns_whole_file_without_bom()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.txt", "a\r\nb\r\n", Text.Utf8Bom);

        var r = Run(temp, src, Base with { Mode = ExtractMode.Tail, Count = 50 });

        Assert.Equal("a\r\nb\r\n", File.ReadAllText(temp.File("out.txt")));
        Assert.Equal(2, r.Lines);
    }

    [Theory]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("sjis")]
    public void Tail_works_for_multibyte_encodings(string kind)
    {
        Encoding enc = kind switch { "utf16le" => new UnicodeEncoding(false, true), "utf16be" => new UnicodeEncoding(true, true), _ => Text.ShiftJis };
        using var temp = new TempDir();
        var src = temp.Write("f.txt", Text.Lines(50, prefix: "受注 "), enc);

        Run(temp, src, Base with { Mode = ExtractMode.Tail, Count = 2, Encoding = OutputEncoding.Utf8 });

        Assert.Equal("受注 49\r\n受注 50\r\n", File.ReadAllText(temp.File("out.txt")));
    }

    [Fact]
    public void Tail_across_many_64k_blocks()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.txt", Text.Lines(50_000));

        Run(temp, src, Base with { Mode = ExtractMode.Tail, Count = 20_000 });

        var lines = Text.ReadAll(temp.File("out.txt"));
        Assert.Equal(20_000, lines.Count);
        Assert.Equal("line 30001", lines[0].Text);
        Assert.Equal("line 50000", lines[^1].Text);
    }

    [Fact]
    public void Tail_with_header_does_not_duplicate_line_one()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "id\r\n1\r\n2\r\n");

        Run(temp, src, Base with { Mode = ExtractMode.Tail, Count = 10, IncludeHeader = true });
        Assert.Equal("id\r\n1\r\n2\r\n", File.ReadAllText(temp.File("out.txt")));

        Run(temp, src, Base with { Mode = ExtractMode.Tail, Count = 1, IncludeHeader = true });
        Assert.Equal("id\r\n2\r\n", File.ReadAllText(temp.File("out.txt")));
    }

    [Fact]
    public void Tail_of_empty_or_single_line_file()
    {
        using var temp = new TempDir();

        Assert.Equal(0, Run(temp, temp.Write("e.txt", ""), Base with { Mode = ExtractMode.Tail, Count = 5 }).Lines);
        Assert.Equal(1, Run(temp, temp.Write("s.txt", "only"), Base with { Mode = ExtractMode.Tail, Count = 5 }).Lines);
        Assert.Equal("only", File.ReadAllText(temp.File("out.txt")));
    }
}
