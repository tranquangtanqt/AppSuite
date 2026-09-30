using System.Text;
using FileTools.Core;

namespace FileTools.Tests;

public class CsvLayoutTests
{
    [Fact]
    public void Reads_delimiter_and_column_names()
    {
        using var temp = new TempDir();
        var path = temp.Write("f.csv", "id;name;;memo\r\n1;A;x;y\r\n");

        var layout = CsvLayout.Read(path);

        Assert.Equal(';', layout.Delimiter);
        Assert.Equal(["id", "name", "Cột 3", "memo"], layout.Columns);
        Assert.Equal(["Cột 1", "Cột 2", "Cột 3", "Cột 4"], CsvLayout.Read(path, hasHeader: false).Columns);
    }
}

public class CsvTransformTests
{
    [Fact]
    public void Picks_and_reorders_columns()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "id,name,city\r\n1,An,\"Hà Nội\r\ncũ\"\r\n2,Bình\r\n");

        var r = CsvTransformer.Transform(new CsvTransformOptions { SourcePath = src, OutputPath = temp.File("o.csv"), Columns = [2, 0] });

        Assert.Equal("city,id\r\n\"Hà Nội\r\ncũ\",1\r\n,2\r\n", File.ReadAllText(temp.File("o.csv")));
        Assert.Equal((3L, 1L), (r.Records, r.ShortRecords));
    }

    [Fact]
    public void Changes_delimiter_and_quotes_only_when_needed()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "a,b\r\n\"x;y\",\"say \"\"hi\"\"\"\r\nplain,1\r\n");

        var r = CsvTransformer.Transform(new CsvTransformOptions { SourcePath = src, OutputPath = temp.File("o.txt"), OutputDelimiter = ';' });

        Assert.Equal("a;b\r\n\"x;y\";\"say \"\"hi\"\"\"\r\nplain;1\r\n", File.ReadAllText(temp.File("o.txt")));
        Assert.Equal((',', ';'), (r.InputDelimiter, r.OutputDelimiter));
    }

    [Fact]
    public void Tab_to_comma_with_quote_all()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.tsv", "a\tb\n1\t2,5\n");

        CsvTransformer.Transform(new CsvTransformOptions { SourcePath = src, OutputPath = temp.File("o.csv"), OutputDelimiter = ',', QuoteAll = true });

        Assert.Equal("\"a\",\"b\"\n\"1\",\"2,5\"\n", File.ReadAllText(temp.File("o.csv")));
    }

    [Fact]
    public void No_columns_selected_is_an_error()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "a,b\r\n");

        Assert.Throws<ArgumentException>(() => CsvTransformer.Transform(new CsvTransformOptions { SourcePath = src, OutputPath = temp.File("o.csv"), Columns = [] }));
    }
}

public class CsvColumnSplitTests
{
    [Fact]
    public void One_file_per_value_with_header()
    {
        using var temp = new TempDir();
        var src = temp.Write("sales.csv", "id,city\r\n1,HN\r\n2,HCM\r\n3,HN\r\n4,\r\n5,\"Đà Nẵng\r\nx\"\r\n");

        var r = CsvColumnSplitter.Split(new CsvSplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Column = 1 });

        Assert.Equal(5, r.Records);
        Assert.Equal(4, r.Parts.Count);
        Assert.Equal("id,city\r\n1,HN\r\n3,HN\r\n", File.ReadAllText(temp.File("out/sales_HN.csv")));
        Assert.Equal("id,city\r\n4,\r\n", File.ReadAllText(temp.File("out/sales_(trong).csv")));
        Assert.Equal(("HN", 2L), (r.Parts[0].Value, r.Parts[0].Records));
        Assert.Single(Directory.GetFiles(temp.File("out"), "sales_Đà Nẵng*"));
    }

    [Fact]
    public void Values_differing_only_by_case_or_invalid_chars_get_distinct_files()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "k\r\nA\r\na\r\nx/y\r\nx:y\r\n");

        var r = CsvColumnSplitter.Split(new CsvSplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Column = 0 });

        Assert.Equal(4, r.Parts.Count);
        Assert.Equal(4, r.Parts.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(["f_A.csv", "f_a_2.csv", "f_x_y.csv", "f_x_y_2.csv"], r.Parts.Select(p => Path.GetFileName(p.Path)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void More_values_than_open_file_limit_are_reopened_and_appended()
    {
        using var temp = new TempDir();
        var sb = new StringBuilder("id,group\r\n");
        // 200 nhóm xen kẽ nhau → buộc phải đóng / mở lại file nhiều lần.
        for (int i = 0; i < 1000; i++)
        {
            sb.Append(i).Append(",g").Append(i % 200).Append("\r\n");
        }
        var src = temp.Write("f.csv", sb.ToString(), Text.Utf8Bom);

        var r = CsvColumnSplitter.Split(new CsvSplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Column = 1, Encoding = OutputEncoding.Utf8Bom });

        Assert.Equal(200, r.Parts.Count);
        Assert.All(r.Parts, p => Assert.Equal(5, p.Records));
        var g7 = File.ReadAllBytes(temp.File("out/f_g7.csv"));
        // BOM đúng 1 lần dù file được mở lại để ghi tiếp.
        Assert.Equal(Text.Utf8Bom.GetPreamble(), g7[..3]);
        Assert.Equal("id,group\r\n7,g7\r\n207,g7\r\n407,g7\r\n607,g7\r\n807,g7\r\n", Text.Utf8.GetString(g7, 3, g7.Length - 3));
        Assert.Empty(Directory.GetFiles(temp.File("out"), "*.partial"));
    }

    [Fact]
    public void Too_many_values_stops_and_leaves_nothing()
    {
        using var temp = new TempDir();
        var sb = new StringBuilder("id\r\n");
        for (int i = 0; i < 50; i++)
        {
            sb.Append(i).Append("\r\n");
        }
        var src = temp.Write("f.csv", sb.ToString());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CsvColumnSplitter.Split(new CsvSplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Column = 0, MaxFiles = 10 }));

        Assert.Contains("hơn 10", ex.Message);
        Assert.Empty(Directory.GetFiles(temp.File("out")));
    }

    [Fact]
    public void Last_record_without_newline_does_not_glue_to_next()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "k,v\r\nA,1\r\nB,2\r\nA,3");

        CsvColumnSplitter.Split(new CsvSplitOptions { SourcePath = src, OutputFolder = temp.File("out"), Column = 0 });

        Assert.Equal("k,v\r\nA,1\r\nA,3", File.ReadAllText(temp.File("out/f_A.csv")));
    }
}

public class DedupeTests
{
    [Fact]
    public void Removes_whole_line_duplicates_keeping_first_and_order()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "id,name\r\n1,A\r\n2,B\r\n1,A\r\n3,C\r\n2,B\r\n");

        var r = DedupeRun(temp, src, new DedupeOptions { SourcePath = src, OutputPath = temp.File("o.csv") });

        Assert.Equal("id,name\r\n1,A\r\n2,B\r\n3,C\r\n", r.Text);
        Assert.Equal((4L, 2L), (r.Result.Kept, r.Result.Removed));
        Assert.Equal([4L, 6L], r.Result.DuplicateSamples);
    }

    [Fact]
    public void By_key_columns_with_ignore_case_and_trim()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "code,name,note\r\nA01,An,x\r\na01 ,An,y\r\nA02,An,z\r\n");

        var r = DedupeRun(temp, src, new DedupeOptions { SourcePath = src, OutputPath = temp.File("o.csv"), KeyColumns = [0], IgnoreCase = true, Trim = true });

        Assert.Equal("code,name,note\r\nA01,An,x\r\nA02,An,z\r\n", r.Text);
    }

    [Fact]
    public void Key_separator_prevents_false_matches()
    {
        using var temp = new TempDir();
        var src = temp.Write("f.csv", "a,b\r\nx,yz\r\nxy,z\r\n");

        var r = DedupeRun(temp, src, new DedupeOptions { SourcePath = src, OutputPath = temp.File("o.csv"), KeyColumns = [0, 1] });

        Assert.Equal(0, r.Result.Removed);
    }

    [Fact]
    public void Plain_text_without_header_and_multiline_csv_records()
    {
        using var temp = new TempDir();
        var txt = temp.Write("f.log", "b\nb\na\nb");
        var csv = temp.Write("m.csv", "h\r\n\"x\r\ny\"\r\n\"x\r\ny\"\r\nx\r\n");

        var t = DedupeRun(temp, txt, new DedupeOptions { SourcePath = txt, OutputPath = temp.File("o.log"), HasHeader = false });
        var c = DedupeRun(temp, csv, new DedupeOptions { SourcePath = csv, OutputPath = temp.File("o.csv") });

        Assert.Equal("b\na\n", t.Text);
        Assert.Equal("h\r\n\"x\r\ny\"\r\nx\r\n", c.Text);
    }

    private static (DedupeResult Result, string Text) DedupeRun(TempDir temp, string src, DedupeOptions o)
    {
        var r = CsvDeduplicator.Dedupe(o);
        return (r, File.ReadAllText(o.OutputPath));
    }
}

public class OutputAppendTests
{
    [Fact]
    public void Append_continues_an_existing_file_without_second_bom()
    {
        using var temp = new TempDir();
        var path = temp.File("o.txt");
        using (var a = new OutputFile(path, Text.Utf8Bom))
        {
            a.WriteLine(new TextLine("x", LineEnding.CrLf));
            a.Commit();
        }
        using (var b = new OutputFile(path, Text.Utf8Bom, append: true))
        {
            Assert.Equal(new FileInfo(path + ".partial").Length, b.BytesWritten);
            b.WriteLine(new TextLine("y", LineEnding.CrLf));
            b.Commit();
        }

        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'x', 13, 10, (byte)'y', 13, 10], File.ReadAllBytes(path));
    }
}

public class UInt64SetTests
{
    [Fact]
    public void Add_reports_duplicates_and_grows_past_the_estimate()
    {
        var set = new UInt64Set(10);
        var rnd = new Random(1);
        var reference = new HashSet<ulong>();
        for (int i = 0; i < 100_000; i++)
        {
            ulong v = (ulong)rnd.NextInt64() % 50_000;
            Assert.Equal(reference.Add(v == 0 ? 1 : v), set.Add(v));
        }
        Assert.Equal(reference.Count, set.Count);
    }

    [Fact]
    public void Zero_is_stored_like_any_other_value()
    {
        var set = new UInt64Set(4);

        Assert.True(set.Add(0));
        Assert.False(set.Add(0));
        Assert.True(set.Add(ulong.MaxValue));
        Assert.False(set.Add(ulong.MaxValue));
    }

    [Fact]
    public void Line_estimate_is_close_for_uniform_lines()
    {
        using var temp = new TempDir();
        var path = temp.Write("f.txt", Text.Lines(20_000, prefix: "row-"));

        long estimate = CsvDeduplicator.EstimateLines(path);

        Assert.InRange(estimate, 20_000, 26_000);
    }
}
