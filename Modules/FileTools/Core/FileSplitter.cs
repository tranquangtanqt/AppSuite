namespace FileTools.Core;

public enum SplitMode
{
    /// <summary>Mỗi phần tối đa N byte (cắt ở cuối dòng).</summary>
    BySize,
    /// <summary>Mỗi phần N dòng dữ liệu.</summary>
    ByLines,
    /// <summary>Chia đều thành N phần (theo dung lượng nguồn).</summary>
    ByParts,
}

public sealed record SplitOptions
{
    public required string SourcePath { get; init; }
    public required string OutputFolder { get; init; }
    public SplitMode Mode { get; init; } = SplitMode.BySize;
    public long SizeBytes { get; init; } = 100L * 1024 * 1024;
    public long Lines { get; init; } = 1_000_000;
    public int Parts { get; init; } = 2;
    /// <summary>Lặp dòng đầu (tiêu đề CSV) ở đầu mọi phần.</summary>
    public bool RepeatHeader { get; init; }
    /// <summary>Đọc theo bản ghi CSV (không cắt giữa 1 ô nhiều dòng). Mặc định: file .csv/.tsv.</summary>
    public bool? CsvRecords { get; init; }
    public OutputEncoding Encoding { get; init; } = OutputEncoding.Utf8;
    public NewlineMode Newline { get; init; } = NewlineMode.Keep;
}

public sealed record SplitPart(string Path, long Lines, long Bytes);

public sealed record SplitResult(IReadOnlyList<SplitPart> Parts, long Lines, string? Warning);

/// <summary>Tách 1 file lớn thành nhiều phần, không bao giờ cắt ngang 1 dòng (hay 1 bản ghi CSV).
/// Tên phần: <c>ten.part001.ext</c>.</summary>
public static class FileSplitter
{
    public static SplitResult Split(SplitOptions options, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var sniff = EncodingSniffer.Detect(options.SourcePath);
        var encoding = TextEncodings.Resolve(options.Encoding, sniff.Encoding);
        bool csv = options.CsvRecords ?? Csv.IsCsvPath(options.SourcePath);
        long sourceLength = new FileInfo(options.SourcePath).Length;
        int digits = Math.Max(3, EstimateParts(options, sourceLength).ToString().Length);
        var baseName = Path.GetFileNameWithoutExtension(options.SourcePath);
        var ext = Path.GetExtension(options.SourcePath);
        Directory.CreateDirectory(options.OutputFolder);

        var parts = new List<SplitPart>();
        var created = new List<string>();
        var throttle = new ProgressThrottle(progress);
        using var lines = LineReader.Open(options.SourcePath, sniff.Encoding);
        using var records = new CsvRecordReader(lines);
        OutputFile? current = null;
        long partData = 0, totalData = 0, consumedBefore = 0;
        TextLine? header = null;

        try
        {
            while (csv ? records.TryRead(out var line) : lines.TryRead(out line))
            {
                if ((lines.LinesRead & 0x3FF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    throttle.Report(lines.Fraction, $"Phần {parts.Count + 1}");
                }
                if (options.RepeatHeader && header is null)
                {
                    header = line;
                    continue;
                }

                long before = consumedBefore;
                consumedBefore = lines.ConsumedBytes;
                if (current is not null && partData > 0 && ShouldRoll(options, current, partData, line, before, parts.Count + 1, sourceLength))
                {
                    parts.Add(Finish(current, partData));
                    current = null;
                }
                if (current is null)
                {
                    var path = Path.Combine(options.OutputFolder, $"{baseName}.part{(parts.Count + 1).ToString().PadLeft(digits, '0')}{ext}");
                    current = new OutputFile(path, encoding, options.Newline);
                    created.Add(path);
                    partData = 0;
                    if (header is { } h)
                    {
                        current.WriteLine(h.Ending == LineEnding.None ? h with { Ending = LineEnding.CrLf } : h);
                    }
                }
                current.WriteLine(line);
                partData++;
                totalData++;
            }
            ct.ThrowIfCancellationRequested();
            if (current is not null)
            {
                parts.Add(Finish(current, partData));
                current = null;
            }
        }
        catch
        {
            // Huỷ / lỗi: xoá cả các phần đã xong - bộ phần thiếu dễ bị tưởng là đủ.
            current?.Dispose();
            foreach (var path in created)
            {
                TryDelete(path);
            }
            throw;
        }

        throttle.Report(1, null, force: true);
        string? warning = parts.Count == 0
            ? "File không có dòng dữ liệu nào - không tạo phần nào."
            : sniff.Warning;
        return new SplitResult(parts, totalData, warning);
    }

    private static bool ShouldRoll(SplitOptions o, OutputFile current, long partData, in TextLine next, long consumedBefore, int partNo, long sourceLength) => o.Mode switch
    {
        SplitMode.ByLines => partData >= Math.Max(1, o.Lines),
        SplitMode.BySize => current.BytesWritten + current.MeasureLine(next) > Math.Max(1, o.SizeBytes),
        // Phần k kết thúc khi đã đọc qua k/N dung lượng nguồn; phần cuối nhận hết phần còn lại.
        _ => partNo < o.Parts && consumedBefore >= sourceLength * partNo / Math.Max(1, o.Parts),
    };

    private static SplitPart Finish(OutputFile file, long dataLines)
    {
        var part = new SplitPart(file.Path, dataLines, file.BytesWritten);
        file.Commit();
        return part;
    }

    private static long EstimateParts(SplitOptions o, long length) => o.Mode switch
    {
        SplitMode.BySize => length / Math.Max(1, o.SizeBytes) + 1,
        SplitMode.ByParts => o.Parts,
        _ => 999,
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
