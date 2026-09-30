namespace FileTools.Core;

public sealed record FileReport
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public required SniffResult Encoding { get; init; }
    public long Lines { get; init; }
    public long EmptyLines { get; init; }
    public long CrLf { get; init; }
    public long Lf { get; init; }
    public long Cr { get; init; }
    public bool EndsWithNewline { get; init; }
    public long LongestLineLength { get; init; }
    public long LongestLineNumber { get; init; }
    /// <summary>Có dấu phân cách CSV (file .csv/.tsv hoặc đoán được từ mẫu); null = không phải bảng.</summary>
    public char? Delimiter { get; init; }
    public int HeaderColumns { get; init; }
    public long Records { get; init; }
    /// <summary>Số bản ghi có số cột khác dòng tiêu đề + vài số thứ tự bản ghi đầu tiên bị lệch.</summary>
    public long MismatchedRecords { get; init; }
    public IReadOnlyList<long> MismatchSamples { get; init; } = [];
    public IReadOnlyList<string> HeaderNames { get; init; } = [];

    public string NewlineStyle => ((CrLf > 0 ? 1 : 0) + (Lf > 0 ? 1 : 0) + (Cr > 0 ? 1 : 0)) switch
    {
        0 => "Không có xuống dòng (1 dòng)",
        1 when CrLf > 0 => "CRLF (Windows)",
        1 when Lf > 0 => "LF (Unix)",
        1 => "CR (Mac cũ)",
        _ => $"Lẫn lộn: CRLF {CrLf:N0} · LF {Lf:N0} · CR {Cr:N0}",
    };
}

/// <summary>Thông tin file trong 1 lượt đọc stream: số dòng, kiểu xuống dòng, dòng dài nhất, encoding; file dạng bảng
/// thêm dấu phân cách, số cột và các bản ghi lệch số cột so với dòng tiêu đề.</summary>
public static class FileInspector
{
    private const int MaxSamples = 10;

    public static FileReport Inspect(string path, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var sniff = EncodingSniffer.Detect(path);
        long size = new FileInfo(path).Length;
        var sample = Csv.ReadSample(path, 50);
        // Là bảng khi: đuôi .csv/.tsv, hoặc 10 dòng đầu đều tách được thành nhiều cột bằng cùng 1 dấu phân cách.
        char guess = Csv.DetectDelimiter(sample, path);
        char? delimiter = Csv.IsCsvPath(path) || (sample.Count > 1 && sample.Take(10).All(l => Csv.CountFields(l, guess) > 1)) ? guess : null;
        var header = delimiter is { } dl && sample.Count > 0 ? Csv.Split(sample[0], dl) : [];

        var throttle = new ProgressThrottle(progress);
        long lines = 0, empty = 0, crlf = 0, lf = 0, cr = 0, longest = 0, longestAt = 0;
        long records = 0, mismatched = 0;
        var samples = new List<long>();
        bool endsWithNewline = false;
        // Đếm bản ghi CSV ngay trong lượt đọc dòng (1 lượt thay vì 2): mang trạng thái "đang trong ngoặc kép" qua các
        // dòng - bản ghi chỉ kết thúc ở cuối dòng khi ngoặc kép đã đóng (cùng quy tắc với CsvRecordReader).
        bool inQuotes = false;
        int fields = 1;
        char delim = delimiter ?? '\0';
        using (var reader = LineReader.Open(path, sniff.Encoding))
        {
            while (reader.TryRead(out var line))
            {
                lines++;
                if ((lines & 0x3FF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    throttle.Report(reader.Fraction, $"{lines:N0} dòng");
                }
                var text = line.Text;
                if (text.Length == 0)
                {
                    empty++;
                }
                if (text.Length > longest)
                {
                    longest = text.Length;
                    longestAt = lines;
                }
                switch (line.Ending)
                {
                    case LineEnding.CrLf: crlf++; break;
                    case LineEnding.Lf: lf++; break;
                    case LineEnding.Cr: cr++; break;
                }
                endsWithNewline = line.Ending != LineEnding.None;

                if (delimiter is null)
                {
                    continue;
                }
                foreach (char c in text)
                {
                    if (c == '"')
                    {
                        inQuotes = !inQuotes;
                    }
                    else if (c == delim && !inQuotes)
                    {
                        fields++;
                    }
                }
                if (inQuotes)
                {
                    continue;
                }
                records++;
                if (fields != header.Count)
                {
                    mismatched++;
                    if (samples.Count < MaxSamples)
                    {
                        samples.Add(records);
                    }
                }
                fields = 1;
            }
        }
        if (delimiter is not null && inQuotes)
        {
            // Ngoặc kép không đóng tới cuối file: vẫn tính là 1 bản ghi (lỗi).
            records++;
            mismatched++;
            if (samples.Count < MaxSamples)
            {
                samples.Add(records);
            }
        }
        throttle.Report(1, null, force: true);

        return new FileReport
        {
            Path = path,
            Size = size,
            Encoding = sniff,
            Lines = lines,
            EmptyLines = empty,
            CrLf = crlf,
            Lf = lf,
            Cr = cr,
            EndsWithNewline = endsWithNewline,
            LongestLineLength = longest,
            LongestLineNumber = longestAt,
            Delimiter = delimiter,
            HeaderColumns = header.Count,
            HeaderNames = header,
            Records = records,
            MismatchedRecords = mismatched,
            MismatchSamples = samples,
        };
    }
}
