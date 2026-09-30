using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FileTools.Core;

namespace FileTools.Tests;

/// <summary>
/// File ~2 GB thật (sinh trong %TEMP%, xoá khi xong) - chỉ chạy tay vì tốn ~6 GB đĩa và vài phút:
/// <c>dotnet run --project Tests\FileTools.Tests -- -explicit only</c>. Đổi cỡ bằng biến môi trường
/// <c>FILETOOLS_LARGE_MB</c>. Kiểm: trích đầu / cuối gần như tức thì, tách 10 phần + nối lại đúng từng byte, RAM thấp.
/// </summary>
public class LargeFileTests
{
    [Fact(Explicit = true)]
    public void Two_gigabyte_file_round_trip()
    {
        long targetMb = long.TryParse(Environment.GetEnvironmentVariable("FILETOOLS_LARGE_MB"), out var mb) ? mb : 2048;
        using var temp = new TempDir();
        var src = temp.File("big.csv");
        var watch = Stopwatch.StartNew();
        long lines = Generate(src, targetMb * 1024 * 1024);
        Log($"Sinh {new FileInfo(src).Length / 1048576} MB, {lines:N0} dòng: {watch.Elapsed.TotalSeconds:0.0} s");

        watch.Restart();
        var head = LineExtractor.Extract(new ExtractOptions { SourcePath = src, OutputPath = temp.File("head.csv"), Mode = ExtractMode.Head, Count = 1000 });
        Log($"1.000 dòng đầu: {watch.ElapsedMilliseconds} ms");
        Assert.Equal(1000, head.Lines);
        Assert.True(watch.ElapsedMilliseconds < 1000);

        watch.Restart();
        var tail = LineExtractor.Extract(new ExtractOptions { SourcePath = src, OutputPath = temp.File("tail.csv"), Mode = ExtractMode.Tail, Count = 1000 });
        Log($"1.000 dòng cuối: {watch.ElapsedMilliseconds} ms");
        Assert.Equal(1000, tail.Lines);
        Assert.StartsWith($"{lines:D10},", File.ReadLines(temp.File("tail.csv")).Last());
        Assert.True(watch.ElapsedMilliseconds < 1000);

        watch.Restart();
        var report = FileInspector.Inspect(src);
        Log($"Thông tin file (1 lượt, gồm đếm cột CSV): {watch.Elapsed.TotalSeconds:0.0} s, {report.Records:N0} bản ghi, lệch cột {report.MismatchedRecords}");
        Assert.Equal(lines, report.Lines);
        Assert.Equal(0, report.MismatchedRecords);

        watch.Restart();
        var search = TextSearcher.Search(new SearchOptions { SourcePath = src, Match = new MatchOptions { Terms = ["khach hang 99999,"], IgnoreDiacritics = true } });
        Log($"Tìm không dấu: {watch.Elapsed.TotalSeconds:0.0} s, {search.TotalMatches:N0} dòng khớp");
        Assert.True(search.TotalMatches > 0);

        watch.Restart();
        var filter = LineFilter.Filter(new FilterOptions { SourcePath = src, OutputPath = temp.File("filtered.csv"), Match = new MatchOptions { Terms = [",0.99"] }, KeepHeader = true });
        Log($"Lọc dòng: {watch.Elapsed.TotalSeconds:0.0} s, giữ {filter.Kept:N0}");
        File.Delete(temp.File("filtered.csv"));

        watch.Restart();
        var split = FileSplitter.Split(new SplitOptions { SourcePath = src, OutputFolder = temp.File("parts"), Mode = SplitMode.ByParts, Parts = 10, Encoding = OutputEncoding.SameAsSource });
        Log($"Tách 10 phần: {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(10, split.Parts.Count);

        watch.Restart();
        var merged = temp.File("merged.csv");
        FileMerger.Merge(new MergeOptions { Files = split.Parts.Select(p => p.Path).ToList(), OutputPath = merged, Encoding = OutputEncoding.SameAsSource });
        Log($"Nối lại: {watch.Elapsed.TotalSeconds:0.0} s");
        foreach (var part in split.Parts)
        {
            File.Delete(part.Path);
        }

        Assert.Equal(Hash(src), Hash(merged));
        File.Delete(merged);
        long peakMb = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576;
        Log($"RAM cao nhất: {peakMb} MB");
        Assert.True(peakMb < 400, $"RAM cao nhất {peakMb} MB");

        // CSV (đợt 4) - đo sau mốc RAM ở trên vì bỏ trùng theo cả dòng phải giữ mã băm của mọi dòng.
        watch.Restart();
        var transform = CsvTransformer.Transform(new CsvTransformOptions { SourcePath = src, OutputPath = temp.File("cols.csv"), Columns = [2, 0], OutputDelimiter = '\t' });
        Log($"Chọn / sắp cột + đổi dấu phân cách: {watch.Elapsed.TotalSeconds:0.0} s, {transform.Records:N0} bản ghi");
        File.Delete(temp.File("cols.csv"));

        watch.Restart();
        var byColumn = CsvDeduplicator.Dedupe(new DedupeOptions { SourcePath = src, OutputPath = temp.File("dd1.csv"), KeyColumns = [2], HasHeader = false });
        Log($"Bỏ trùng theo 1 cột: {watch.Elapsed.TotalSeconds:0.0} s, giữ {byColumn.Kept:N0}");
        File.Delete(temp.File("dd1.csv"));

        watch.Restart();
        var whole = CsvDeduplicator.Dedupe(new DedupeOptions { SourcePath = src, OutputPath = temp.File("dd2.csv"), HasHeader = false });
        long afterMb = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576;
        Log($"Bỏ trùng cả dòng ({lines:N0} dòng khác nhau): {watch.Elapsed.TotalSeconds:0.0} s, giữ {whole.Kept:N0}, RAM cao nhất {afterMb} MB");
        Assert.Equal(lines, whole.Kept);
    }

    private static long Generate(string path, long bytes)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20);
        var rnd = new Random(1);
        long n = 0, written = 0;
        var sb = new StringBuilder();
        while (written < bytes)
        {
            n++;
            sb.Clear().Append(n.ToString("D10")).Append(",受注テスト,Khách hàng ").Append(rnd.Next(100_000))
              .Append(',').Append(rnd.NextDouble().ToString("0.0000")).Append(",\"ghi chú, có dấu phẩy\"\r\n");
            writer.Write(sb);
            written += Encoding.UTF8.GetByteCount(sb.ToString());
        }
        return n;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Log(string message) => TestContext.Current.SendDiagnosticMessage(message);
}
