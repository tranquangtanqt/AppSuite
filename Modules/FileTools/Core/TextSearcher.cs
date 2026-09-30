namespace FileTools.Core;

public sealed record SearchOptions
{
    public required string SourcePath { get; init; }
    public required MatchOptions Match { get; init; }
    /// <summary>Số kết quả giữ lại để hiển thị (vẫn đếm hết).</summary>
    public int MaxResults { get; init; } = 10_000;
    /// <summary>Ghi mọi dòng khớp ra file này (null = không ghi).</summary>
    public string? ExportPath { get; init; }
    /// <summary>Thêm số dòng gốc vào đầu mỗi dòng khi xuất ("123: ...").</summary>
    public bool ExportLineNumbers { get; init; }
    public OutputEncoding Encoding { get; init; } = OutputEncoding.Utf8;
    public NewlineMode Newline { get; init; } = NewlineMode.Keep;
}

/// <param name="Column">Vị trí khớp đầu tiên trong dòng (0-based).</param>
public sealed record SearchHit(long LineNumber, int Column, string Text);

public sealed record SearchResult(long TotalMatches, long LinesScanned, IReadOnlyList<SearchHit> Hits, bool Truncated);

/// <summary>Tìm chữ / regex trong file lớn: đếm mọi dòng khớp, giữ tối đa <see cref="SearchOptions.MaxResults"/> để
/// hiển thị, tuỳ chọn xuất toàn bộ dòng khớp ra file.</summary>
public static class TextSearcher
{
    /// <summary>Dòng rất dài chỉ giữ phần quanh chỗ khớp để hiển thị.</summary>
    private const int DisplayChars = 400;

    public static SearchResult Search(SearchOptions o, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var matcher = new TextMatcher(o.Match);
        var sniff = EncodingSniffer.Detect(o.SourcePath);
        var hits = new List<SearchHit>();
        var throttle = new ProgressThrottle(progress);
        long total = 0;
        using var export = o.ExportPath is null ? null : new OutputFile(o.ExportPath, TextEncodings.Resolve(o.Encoding, sniff.Encoding), o.Newline);
        using var reader = LineReader.Open(o.SourcePath, sniff.Encoding);
        while (reader.TryRead(out var line))
        {
            long n = reader.LinesRead;
            if ((n & 0x3FF) == 0)
            {
                ct.ThrowIfCancellationRequested();
                throttle.Report(reader.Fraction, $"{total:N0} kết quả");
            }
            int at = matcher.Find(line.Text);
            if (at < 0)
            {
                continue;
            }
            total++;
            if (hits.Count < o.MaxResults)
            {
                hits.Add(new SearchHit(n, at, Clip(line.Text, at)));
            }
            if (export is not null)
            {
                var ending = line.Ending == LineEnding.None ? LineEnding.CrLf : line.Ending;
                export.WriteLine(new TextLine(o.ExportLineNumbers ? $"{n}: {line.Text}" : line.Text, ending));
            }
        }
        ct.ThrowIfCancellationRequested();
        export?.Commit();
        throttle.Report(1, null, force: true);
        return new SearchResult(total, reader.LinesRead, hits, total > hits.Count);
    }

    private static string Clip(string text, int at)
    {
        if (text.Length <= DisplayChars)
        {
            return text;
        }
        int start = Math.Clamp(at - DisplayChars / 4, 0, text.Length - DisplayChars);
        return (start > 0 ? "…" : string.Empty) + text.Substring(start, DisplayChars) + (start + DisplayChars < text.Length ? "…" : string.Empty);
    }
}

public sealed record FilterOptions
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public required MatchOptions Match { get; init; }
    /// <summary>true = giữ dòng khớp, false = bỏ dòng khớp.</summary>
    public bool KeepMatching { get; init; } = true;
    /// <summary>Luôn giữ dòng 1 (tiêu đề CSV) dù có khớp hay không.</summary>
    public bool KeepHeader { get; init; }
    /// <summary>Đọc theo bản ghi CSV (ô nhiều dòng không bị tách). Mặc định: file .csv/.tsv.</summary>
    public bool? CsvRecords { get; init; }
    public OutputEncoding Encoding { get; init; } = OutputEncoding.Utf8;
    public NewlineMode Newline { get; init; } = NewlineMode.Keep;
}

public sealed record FilterResult(long Kept, long Removed);

/// <summary>Lọc dòng: giữ hoặc bỏ các dòng (bản ghi CSV) chứa từ khoá / khớp regex, ghi ra file mới.</summary>
public static class LineFilter
{
    public static FilterResult Filter(FilterOptions o, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var matcher = new TextMatcher(o.Match);
        var sniff = EncodingSniffer.Detect(o.SourcePath);
        bool csv = o.CsvRecords ?? Csv.IsCsvPath(o.SourcePath);
        var throttle = new ProgressThrottle(progress);
        long kept = 0, removed = 0, index = 0;
        using var output = new OutputFile(o.OutputPath, TextEncodings.Resolve(o.Encoding, sniff.Encoding), o.Newline);
        using var lines = LineReader.Open(o.SourcePath, sniff.Encoding);
        using var records = new CsvRecordReader(lines);
        while (csv ? records.TryRead(out var line) : lines.TryRead(out line))
        {
            index++;
            if ((index & 0x3FF) == 0)
            {
                ct.ThrowIfCancellationRequested();
                throttle.Report(lines.Fraction, $"giữ {kept:N0} · bỏ {removed:N0}");
            }
            bool keep = (index == 1 && o.KeepHeader) || matcher.IsMatch(line.Text) == o.KeepMatching;
            if (!keep)
            {
                removed++;
                continue;
            }
            // Dòng cuối file nguồn (không có xuống dòng) có thể không phải dòng cuối được giữ.
            output.EndLineIfNeeded();
            output.WriteLine(line);
            kept++;
        }
        ct.ThrowIfCancellationRequested();
        output.Commit();
        throttle.Report(1, null, force: true);
        return new FilterResult(kept, removed);
    }
}
