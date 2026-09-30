namespace FileTools.Core;

public sealed record MergeOptions
{
    public required IReadOnlyList<string> Files { get; init; }
    public required string OutputPath { get; init; }
    public OutputEncoding Encoding { get; init; } = OutputEncoding.Utf8;
    public NewlineMode Newline { get; init; } = NewlineMode.Keep;
    /// <summary>Chèn 1 dòng tên file trước nội dung mỗi file.</summary>
    public bool FileNameHeader { get; init; }
    /// <summary>{0} = tên file.</summary>
    public string FileNameHeaderFormat { get; init; } = "===== {0} =====";
    /// <summary>Chèn 1 dòng trống giữa 2 file.</summary>
    public bool BlankLineBetween { get; init; }
    /// <summary>CSV: bỏ dòng tiêu đề của file thứ 2 trở đi (nếu giống tiêu đề file đầu).</summary>
    public bool CsvHeaderOnce { get; init; }
}

public sealed record MergeResult(int Files, long Lines, long BytesWritten, IReadOnlyList<string> Notes);

/// <summary>
/// Nối nội dung nhiều file thành 1: mỗi file tự nhận encoding, ghi ra 1 encoding thống nhất; luôn có xuống dòng
/// giữa 2 file (file trước không kết thúc bằng xuống dòng thì tự thêm). Đọc/ghi kiểu stream.
/// </summary>
public static class FileMerger
{
    public static MergeResult Merge(MergeOptions options, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var outputFull = Path.GetFullPath(options.OutputPath);
        var files = options.Files.Where(f => !string.Equals(Path.GetFullPath(f), outputFull, StringComparison.OrdinalIgnoreCase)).ToList();
        var notes = new List<string>();
        if (files.Count < options.Files.Count)
        {
            notes.Add("Bỏ qua file đầu ra nằm trong danh sách nguồn.");
        }

        var sniffs = files.Select(EncodingSniffer.Detect).ToList();
        long totalBytes = Math.Max(1, files.Sum(f => new FileInfo(f).Length));
        long doneBytes = 0;
        var throttle = new ProgressThrottle(progress);
        var encoding = TextEncodings.Resolve(options.Encoding, sniffs.FirstOrDefault()?.Encoding);

        using var output = new OutputFile(options.OutputPath, encoding, options.Newline);
        string? firstHeader = null;
        for (int i = 0; i < files.Count; i++)
        {
            var name = Path.GetFileName(files[i]);
            if (sniffs[i].Warning is { } warning)
            {
                notes.Add($"{name}: {warning}");
            }
            if (i > 0)
            {
                output.EndLineIfNeeded();
                if (options.BlankLineBetween)
                {
                    output.WriteLine(string.Empty);
                }
            }
            if (options.FileNameHeader)
            {
                output.WriteLine(string.Format(options.FileNameHeaderFormat, name));
            }

            using var lines = LineReader.Open(files[i], sniffs[i].Encoding);
            using var records = new CsvRecordReader(lines);
            bool first = true;
            while (options.CsvHeaderOnce ? records.TryRead(out var line) : lines.TryRead(out line))
            {
                if ((lines.LinesRead & 0x3FF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    throttle.Report((doneBytes + lines.ConsumedBytes) / (double)totalBytes, name);
                }
                if (first && options.CsvHeaderOnce)
                {
                    first = false;
                    if (i == 0)
                    {
                        firstHeader = line.Text;
                    }
                    else if (line.Text == firstHeader)
                    {
                        continue;
                    }
                    else
                    {
                        notes.Add($"{name}: dòng tiêu đề khác file đầu - giữ lại.");
                    }
                }
                output.WriteLine(line);
            }
            doneBytes += new FileInfo(files[i]).Length;
        }
        ct.ThrowIfCancellationRequested();
        long written = output.LinesWritten, bytes = output.BytesWritten;
        output.Commit();
        throttle.Report(1, null, force: true);
        return new MergeResult(files.Count, written, bytes, notes);
    }
}
