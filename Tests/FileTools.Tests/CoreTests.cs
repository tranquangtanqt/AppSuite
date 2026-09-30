using System.Text;
using FileTools.Core;

namespace FileTools.Tests;

public class EncodingSnifferTests
{
    [Theory]
    [InlineData("utf8bom", "UTF-8 có BOM")]
    [InlineData("utf8", "UTF-8")]
    [InlineData("utf16le", "UTF-16 LE")]
    [InlineData("utf16be", "UTF-16 BE")]
    [InlineData("sjis", "Shift-JIS")]
    [InlineData("ascii", "UTF-8")]
    public void Detects_common_encodings(string kind, string expected)
    {
        const string text = "受注番号,得意先,Tiếng Việt\r\n1,テスト,ok\r\n";
        Encoding enc = kind switch
        {
            "utf8bom" => Text.Utf8Bom,
            "utf16le" => new UnicodeEncoding(false, true),
            "utf16be" => new UnicodeEncoding(true, true),
            "sjis" => Text.ShiftJis,
            _ => Text.Utf8,
        };
        string content = kind switch { "sjis" => "受注番号,得意先\r\n1,テスト\r\n", "ascii" => "a,b\r\n1,2\r\n", _ => text };
        using var temp = new TempDir();

        var result = EncodingSniffer.Detect(temp.Write("f.txt", content, enc));

        Assert.Equal(expected, result.Name);
    }

    [Fact]
    public void Utf16_without_bom_is_guessed_with_a_warning()
    {
        var bytes = new UnicodeEncoding(false, false).GetBytes("hello world\r\nline 2\r\n");

        var result = EncodingSniffer.Detect(bytes);

        Assert.Equal("UTF-16 LE", result.Name);
        Assert.Equal(SniffConfidence.Medium, result.Confidence);
    }

    [Fact]
    public void Multibyte_char_cut_at_end_of_sample_is_not_an_error()
    {
        var bytes = Text.Utf8.GetBytes("abc 日本");
        // Cắt mất byte cuối của "本" - như khi mẫu 64 KB rơi giữa 1 ký tự.
        var result = EncodingSniffer.Detect(bytes.AsSpan(0, bytes.Length - 1));

        Assert.Equal("UTF-8", result.Name);
        Assert.Equal(SniffConfidence.High, result.Confidence);
    }

    [Fact]
    public void Garbage_falls_back_to_utf8_with_low_confidence()
    {
        // 0x81 0x20: không hợp lệ ở cả UTF-8 (byte nối không có byte dẫn) lẫn Shift-JIS (byte sau phải ≥ 0x40).
        byte[] bytes = [0x41, 0x41, 0x81, 0x20, 0x41, 0x81, 0x20, 0x41];

        var result = EncodingSniffer.Detect(bytes);

        Assert.Equal(SniffConfidence.Low, result.Confidence);
        Assert.NotNull(result.Warning);
    }
}

public class LineReaderTests
{
    [Fact]
    public void Keeps_every_kind_of_line_ending()
    {
        using var temp = new TempDir();
        var path = temp.Write("f.txt", "a\r\nb\nc\rd");

        var lines = Text.ReadAll(path);

        Assert.Equal([new("a", LineEnding.CrLf), new("b", LineEnding.Lf), new("c", LineEnding.Cr), new("d", LineEnding.None)], lines);
    }

    [Fact]
    public void Empty_lines_and_trailing_newline()
    {
        using var temp = new TempDir();

        var lines = Text.ReadAll(temp.Write("f.txt", "\r\n\r\nx\r\n"));

        Assert.Equal(["", "", "x"], lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.Equal(LineEnding.CrLf, l.Ending));
        Assert.Empty(Text.ReadAll(temp.Write("empty.txt", "")));
    }

    [Fact]
    public void Crlf_split_across_buffer_boundary_is_one_ending()
    {
        // Buffer ký tự 64K: đặt "\r" đúng ở ký tự cuối của buffer đầu.
        var first = new string('x', 65535) + "\r\n";
        using var temp = new TempDir();

        var lines = Text.ReadAll(temp.Write("f.txt", first + "y\r\n"));

        Assert.Equal(2, lines.Count);
        Assert.Equal(65535, lines[0].Text.Length);
        Assert.Equal(LineEnding.CrLf, lines[0].Ending);
        Assert.Equal("y", lines[1].Text);
    }

    [Fact]
    public void Long_line_across_many_buffers()
    {
        var longLine = new string('日', 300_000);
        using var temp = new TempDir();

        var lines = Text.ReadAll(temp.Write("f.txt", longLine + "\nend"));

        Assert.Equal(longLine, lines[0].Text);
        Assert.Equal("end", lines[1].Text);
    }

    [Theory]
    [InlineData("utf8bom")]
    [InlineData("sjis")]
    [InlineData("utf16")]
    public void Consumed_bytes_equals_file_length_at_the_end(string kind)
    {
        Encoding enc = kind switch { "utf8bom" => Text.Utf8Bom, "sjis" => Text.ShiftJis, _ => new UnicodeEncoding(false, true) };
        using var temp = new TempDir();
        var path = temp.Write("f.txt", "受注\r\nテスト\nabc", enc);

        using var reader = LineReader.Open(path);
        while (reader.TryRead(out _))
        {
        }

        Assert.Equal(new FileInfo(path).Length, reader.ConsumedBytes);
        Assert.Equal(3, reader.LinesRead);
        Assert.Equal(1, reader.Fraction);
    }

    [Fact]
    public void Bom_is_not_part_of_the_first_line()
    {
        using var temp = new TempDir();

        var lines = Text.ReadAll(temp.Write("f.txt", "id,name\r\n", Text.Utf8Bom));

        Assert.Equal("id,name", lines[0].Text);
    }
}

public class OutputFileTests
{
    [Fact]
    public void Converts_line_endings_but_keeps_missing_last_one()
    {
        using var temp = new TempDir();
        var path = temp.File("out.txt");

        using (var output = new OutputFile(path, Text.Utf8, NewlineMode.Lf))
        {
            output.WriteLine(new TextLine("a", LineEnding.CrLf));
            output.WriteLine(new TextLine("b", LineEnding.None));
            output.Commit();
        }

        Assert.Equal("a\nb", File.ReadAllText(path));
    }

    [Fact]
    public void Not_committed_leaves_no_file()
    {
        using var temp = new TempDir();
        var path = temp.File("out.txt");

        using (var output = new OutputFile(path, Text.Utf8))
        {
            output.WriteLine("x");
        }

        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Bytes_written_includes_bom_and_matches_file()
    {
        using var temp = new TempDir();
        var path = temp.File("out.txt");
        long counted;

        using (var output = new OutputFile(path, Text.Utf8Bom))
        {
            output.WriteLine(new TextLine("受注", LineEnding.CrLf));
            counted = output.BytesWritten;
            output.Commit();
        }

        Assert.Equal(new FileInfo(path).Length, counted);
    }

    [Fact]
    public void End_line_if_needed_uses_last_seen_ending()
    {
        using var temp = new TempDir();
        var path = temp.File("out.txt");

        using (var output = new OutputFile(path, Text.Utf8))
        {
            output.WriteLine(new TextLine("a", LineEnding.Lf));
            output.WriteLine(new TextLine("b", LineEnding.None));
            output.EndLineIfNeeded();
            output.EndLineIfNeeded();
            output.Commit();
        }

        Assert.Equal("a\nb\n", File.ReadAllText(path));
    }
}

public class CsvTests
{
    [Fact]
    public void Record_reader_joins_lines_inside_quotes()
    {
        using var temp = new TempDir();
        var path = temp.Write("f.csv", "id,memo\r\n1,\"dòng 1\r\ndòng 2\"\r\n2,x\r\n");

        using var reader = new CsvRecordReader(LineReader.Open(path));
        var records = new List<TextLine>();
        while (reader.TryRead(out var r))
        {
            records.Add(r);
        }

        Assert.Equal(["id,memo", "1,\"dòng 1\r\ndòng 2\"", "2,x"], records.Select(r => r.Text));
    }

    [Fact]
    public void Split_and_join_round_trip()
    {
        var fields = Csv.Split("a,\"b,c\",\"say \"\"hi\"\"\",", ',');

        Assert.Equal(["a", "b,c", "say \"hi\"", ""], fields);
        Assert.Equal("a,\"b,c\",\"say \"\"hi\"\"\",", Csv.Join(fields, ','));
    }

    [Theory]
    [InlineData("a,b,c\n1,2,3\n4,5,6", "f.csv", ',')]
    [InlineData("a;b;c\n1;2;3", "f.csv", ';')]
    [InlineData("a\tb\n1\t2", "f.tsv", '\t')]
    [InlineData("a|b|c\n1|2|3", "f.txt", '|')]
    [InlineData("khong co dau phan cach", "f.txt", ',')]
    public void Detects_delimiter(string sample, string path, char expected)
    {
        Assert.Equal(expected, Csv.DetectDelimiter(sample.Split('\n'), path));
    }
}

public class NaturalComparerTests
{
    [Fact]
    public void Numbers_compare_by_value()
    {
        string[] names = ["file10.csv", "File2.csv", "file1.csv", "file02b.csv", "a.csv"];

        Assert.Equal(["a.csv", "file1.csv", "File2.csv", "file02b.csv", "file10.csv"], names.Order(NaturalComparer.Instance));
    }
}
