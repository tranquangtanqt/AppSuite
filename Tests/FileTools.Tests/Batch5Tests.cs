using System.Text;
using FileTools.Core;

namespace FileTools.Tests;

public class TextDiffTests
{
    private static CompareResult Diff(TempDir temp, string a, string b, CompareOptions? o = null)
    {
        var pa = temp.Write("a.txt", a);
        var pb = temp.Write("b.txt", b);
        return TextDiff.Compare((o ?? new CompareOptions { PathA = "", PathB = "" }) with { PathA = pa, PathB = pb });
    }

    private static string Render(CompareResult r) => string.Join("|", r.Hunks.SelectMany(h => h.Lines).Select(l =>
        (l.Kind switch { DiffLineKind.Removed => "-", DiffLineKind.Added => "+", _ => " " }) + l.Text));

    [Fact]
    public void Identical_files()
    {
        using var temp = new TempDir();

        var r = Diff(temp, "a\nb\n", "a\nb\n");

        Assert.True(r.Identical);
        Assert.Empty(r.Hunks);
    }

    [Fact]
    public void Changed_line_shows_old_then_new_with_context()
    {
        using var temp = new TempDir();
        var a = Text.Lines(20, "\n");
        var b = a.Replace("line 10\n", "LINE TEN\n");

        var r = Diff(temp, a, b);

        Assert.Equal((1L, 1L, 1), (r.Removed, r.Added, r.ChangeBlocks));
        var hunk = Assert.Single(r.Hunks);
        Assert.Equal(" line 7| line 8| line 9|-line 10|+LINE TEN| line 11| line 12| line 13", Render(r));
        Assert.Equal((7L, 7L), (hunk.StartA, hunk.StartB));
        var removed = hunk.Lines.Single(l => l.Kind == DiffLineKind.Removed);
        Assert.Equal((10L, (long?)null), (removed.LineA!.Value, removed.LineB));
    }

    [Fact]
    public void Insertions_and_deletions_get_correct_line_numbers()
    {
        using var temp = new TempDir();

        var r = Diff(temp, "a\nb\nc\nd\n", "a\nc\nd\nx\n", new CompareOptions { PathA = "", PathB = "", ContextLines = 0 });

        Assert.Equal("-b|+x", Render(r));
        Assert.Equal(2, r.Hunks.Count);
        Assert.Equal(2, r.Hunks[0].Lines[0].LineA);
        Assert.Equal(4, r.Hunks[1].Lines[0].LineB);
    }

    [Fact]
    public void Nearby_changes_merge_into_one_hunk_far_ones_do_not()
    {
        using var temp = new TempDir();
        var a = Text.Lines(100, "\n");
        var near = a.Replace("line 10\n", "X\n").Replace("line 15\n", "Y\n");
        var far = a.Replace("line 10\n", "X\n").Replace("line 60\n", "Y\n");

        Assert.Single(Diff(temp, a, near).Hunks);
        Assert.Equal(2, Diff(temp, a, far).Hunks.Count);
    }

    [Fact]
    public void Ignore_case_and_whitespace()
    {
        using var temp = new TempDir();

        var strict = Diff(temp, "Hello  World\n", "hello world \n");
        var loose = Diff(temp, "Hello  World\n", "hello world \n", new CompareOptions { PathA = "", PathB = "", IgnoreCase = true, IgnoreWhitespace = true });

        Assert.False(strict.Identical);
        Assert.True(loose.Identical);
    }

    [Fact]
    public void Too_different_falls_back_to_set_difference()
    {
        using var temp = new TempDir();
        var a = Text.Lines(300, "\n", prefix: "a ");
        var b = Text.Lines(300, "\n", prefix: "b ") + "a 5\n";

        var r = Diff(temp, a, b, new CompareOptions { PathA = "", PathB = "", MaxEdits = 50 });

        Assert.True(r.TooDifferent);
        Assert.Equal((299L, 300L), (r.Removed, r.Added));
        Assert.DoesNotContain(r.Hunks[0].Lines, l => l.Text == "a 5");
    }

    [Fact]
    public void Writes_html_report_and_truncates_display()
    {
        using var temp = new TempDir();
        var a = Text.Lines(200, "\n", prefix: "<x> ");
        var b = Text.Lines(200, "\n");
        var report = temp.File("r.html");

        var r = Diff(temp, a, b, new CompareOptions { PathA = "", PathB = "", ReportPath = report, MaxDisplayLines = 50 });

        Assert.True(r.Truncated);
        Assert.True(r.Hunks.Sum(h => h.Lines.Count) <= 50);
        var html = File.ReadAllText(report);
        Assert.Contains("&lt;x&gt; 1", html);
        Assert.Contains("chỉ hiện một phần", html);
    }

    [Fact]
    public void Different_encodings_compare_by_text()
    {
        using var temp = new TempDir();
        var pa = temp.Write("a.txt", "受注\r\nテスト\r\n", Text.ShiftJis);
        var pb = temp.Write("b.txt", "受注\r\nテスト\r\n", Text.Utf8Bom);

        Assert.True(TextDiff.Compare(new CompareOptions { PathA = pa, PathB = pb }).Identical);
    }
}

public class ReplaceTests
{
    [Fact]
    public void Preview_counts_without_writing()
    {
        using var temp = new TempDir();
        var f = temp.Write("a.txt", "foo bar foo\r\nbaz\r\nFOO\r\n");

        var p = Assert.Single(BatchReplacer.Preview([f], new ReplaceOptions { Find = "foo", Replacement = "X" }));

        Assert.Equal((3L, 2L), (p.Matches, p.Lines));
        Assert.Equal((1L, "foo bar foo", "X bar X"), p.Samples[0]);
        Assert.Equal("foo bar foo\r\nbaz\r\nFOO\r\n", File.ReadAllText(f));
    }

    [Fact]
    public void Apply_in_place_keeps_encoding_newlines_and_bak()
    {
        using var temp = new TempDir();
        var f = temp.Write("a.csv", "受注,旧\r\nb,旧\n", Text.ShiftJis);
        var untouched = temp.Write("b.csv", "nothing\r\n");

        var items = BatchReplacer.Apply([f, untouched], new ReplaceOptions { Find = "旧", Replacement = "新", MatchCase = true }, null);

        Assert.Equal(2, items[0].Replacements);
        Assert.Equal(RewriteStatus.Written, items[0].Status);
        Assert.Equal(RewriteStatus.Unchanged, items[1].Status);
        Assert.Equal(Text.ShiftJis.GetBytes("受注,新\r\nb,新\n"), File.ReadAllBytes(f));
        Assert.True(File.Exists(f + ".bak"));
        Assert.False(File.Exists(untouched + ".bak"));
    }

    [Fact]
    public void Literal_dollar_regex_groups_and_whole_word()
    {
        var literal = BatchReplacer.BuildTransform(new ReplaceOptions { Find = "price", Replacement = "$1 each" });
        var regex = BatchReplacer.BuildTransform(new ReplaceOptions { Find = @"(\d{4})-(\d{2})", Replacement = "$2/$1", IsRegex = true });
        var word = BatchReplacer.BuildTransform(new ReplaceOptions { Find = "cat", Replacement = "dog", WholeWord = true });

        Assert.Equal(("$1 each: 5", 1), literal("price: 5"));
        Assert.Equal(("09/2026 và 10/2026", 2), regex("2026-09 và 2026-10"));
        // "_" tính là ký tự của từ (như \w) nên "cat_x" không phải nguyên từ "cat".
        Assert.Equal(("dog concat cat_x dog.", 2), word("cat concat cat_x cat."));
    }

    [Fact]
    public void Invalid_regex_is_reported()
    {
        var ex = Assert.Throws<ArgumentException>(() => BatchReplacer.BuildRegex(new ReplaceOptions { Find = "(", IsRegex = true }));

        Assert.StartsWith("Regex không hợp lệ", ex.Message);
    }
}

public class LogTailerTests
{
    private static void Append(string path, string text) =>
        File.AppendAllText(path, text, new UTF8Encoding(false));

    [Fact]
    public void Starts_with_last_lines_then_reads_only_complete_new_lines()
    {
        using var temp = new TempDir();
        var path = temp.Write("app.log", Text.Lines(10));
        var tail = new LogTailer(path);

        Assert.Equal(["line 9", "line 10"], tail.Start(2));
        Assert.Empty(tail.Poll().Lines);

        Append(path, "new 1\r\nnew 2 (đang ghi");
        Assert.Equal(["new 1"], tail.Poll().Lines);

        Append(path, " dở)\r\n");
        Assert.Equal(["new 2 (đang ghi dở)"], tail.Poll().Lines);
    }

    [Fact]
    public void Crlf_split_between_two_polls_is_one_line_break()
    {
        using var temp = new TempDir();
        var path = temp.Write("app.log", "");
        var tail = new LogTailer(path);
        tail.Start(0);

        Append(path, "a\r");
        Assert.Equal(["a"], tail.Poll().Lines);
        Append(path, "\nb\r\n");
        Assert.Equal(["b"], tail.Poll().Lines);
    }

    [Fact]
    public void Truncated_file_is_read_again_from_start()
    {
        using var temp = new TempDir();
        var path = temp.Write("app.log", Text.Lines(100));
        var tail = new LogTailer(path);
        tail.Start(0);

        File.WriteAllText(path, "rotated 1\r\n");
        var batch = tail.Poll();

        Assert.NotNull(batch.Notice);
        Assert.Equal(["rotated 1"], batch.Lines);
    }

    [Fact]
    public void Missing_file_is_reported_not_thrown()
    {
        using var temp = new TempDir();
        var path = temp.Write("app.log", "x\r\n");
        var tail = new LogTailer(path);
        tail.Start(5);
        File.Delete(path);

        Assert.NotNull(tail.Poll().Notice);
    }
}

public class DuplicateFinderTests
{
    [Fact]
    public void Groups_identical_files_and_ignores_same_size_different_content()
    {
        using var temp = new TempDir();
        temp.Write("a/1.txt", "same content");
        temp.Write("b/2.txt", "same content");
        temp.Write("b/3.txt", "diff content");   // cùng cỡ, khác nội dung
        temp.Write("c.txt", "unique");

        var r = DuplicateFinder.Find([temp.Path], "*", recursive: true);

        var g = Assert.Single(r.Groups);
        Assert.Equal(["1.txt", "2.txt"], g.Files.Select(Path.GetFileName));
        Assert.Equal(12, g.Wasted);
        Assert.Equal(4, r.FilesScanned);
    }

    [Fact]
    public void Large_files_differing_only_after_64k_are_not_duplicates()
    {
        using var temp = new TempDir();
        var body = new string('x', 100_000);
        temp.Write("a.txt", body + "A");
        temp.Write("b.txt", body + "B");
        temp.Write("c.txt", body + "A");

        var g = Assert.Single(DuplicateFinder.Find([temp.Path], "*.txt", false).Groups);

        Assert.Equal(["a.txt", "c.txt"], g.Files.Select(Path.GetFileName));
    }

    [Fact]
    public void Empty_files_are_skipped_by_default()
    {
        using var temp = new TempDir();
        temp.Write("a.txt", "");
        temp.Write("b.txt", "");

        Assert.Empty(DuplicateFinder.Find([temp.Path], "*", false).Groups);
    }
}

public class PresetTests
{
    private sealed class FakeVm
    {
        public string Patterns { get; set; } = "*.txt";
        public bool Recursive { get; set; }
        public double SizeMb { get; set; } = 100;
        public int ModeIndex { get; set; }
        public bool IsBusy { get; set; }
        public string ReadOnly => "x";
        public FakeOutput Output { get; } = new();
    }

    private sealed class FakeOutput
    {
        public int EncodingIndex { get; set; }
    }

    [Fact]
    public void Capture_and_apply_round_trip_with_nested_and_exclusions()
    {
        var vm = new FakeVm { Patterns = "*.csv", Recursive = true, SizeMb = 12.5, ModeIndex = 2, IsBusy = true };
        vm.Output.EncodingIndex = 3;

        var values = PresetMapper.Capture(vm, new HashSet<string> { "IsBusy" }, ["Output"]);
        var fresh = new FakeVm();
        int applied = PresetMapper.Apply(fresh, values);

        Assert.DoesNotContain("IsBusy", values.Keys);
        Assert.DoesNotContain("ReadOnly", values.Keys);
        Assert.Equal(5, applied);
        Assert.Equal(("*.csv", true, 12.5, 2, 3), (fresh.Patterns, fresh.Recursive, fresh.SizeMb, fresh.ModeIndex, fresh.Output.EncodingIndex));
        Assert.False(fresh.IsBusy);
    }

    [Fact]
    public void Apply_skips_unknown_and_invalid_values()
    {
        var vm = new FakeVm();

        int applied = PresetMapper.Apply(vm, new Dictionary<string, string> { ["Gone"] = "1", ["SizeMb"] = "abc", ["ModeIndex"] = "1" });

        Assert.Equal(1, applied);
        Assert.Equal(100, vm.SizeMb);
    }

    [Fact]
    public void Store_saves_overwrites_lists_and_deletes()
    {
        using var temp = new TempDir();
        var store = new PresetStore(temp.File("cfg/presets.json"));

        store.Save("SplitPage", new Preset("Log 100MB", new() { ["SizeMb"] = "100" }));
        store.Save("SplitPage", new Preset("log 100mb", new() { ["SizeMb"] = "200" }));
        store.Save("SplitPage", new Preset("CSV 1 triệu dòng", new() { ["ModeIndex"] = "1" }));
        store.Save("MergePage", new Preset("x", new()));

        var list = store.List("SplitPage");
        Assert.Equal(["CSV 1 triệu dòng", "log 100mb"], list.Select(p => p.Name));
        Assert.Equal("200", list[1].Values["SizeMb"]);
        Assert.True(store.Delete("SplitPage", "LOG 100MB"));
        Assert.Single(store.List("SplitPage"));
        Assert.Single(store.List("MergePage"));
    }

    [Fact]
    public void Corrupt_file_means_no_presets()
    {
        using var temp = new TempDir();
        var path = temp.Write("presets.json", "{ not json");

        Assert.Empty(new PresetStore(path).List("SplitPage"));
    }
}
