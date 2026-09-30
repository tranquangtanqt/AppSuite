using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;

namespace FileTools.Core;

/// <summary>Cấu trúc 1 file bảng: dấu phân cách (tự nhận hoặc chỉ định) + tên cột từ dòng đầu.</summary>
public sealed record CsvLayout(char Delimiter, IReadOnlyList<string> Columns, SniffResult Encoding)
{
    /// <param name="delimiter">null = tự nhận từ 50 bản ghi đầu.</param>
    public static CsvLayout Read(string path, char? delimiter = null, bool hasHeader = true)
    {
        var sniff = EncodingSniffer.Detect(path);
        var sample = Csv.ReadSample(path, 50);
        char d = delimiter ?? Csv.DetectDelimiter(sample, path);
        var first = sample.Count > 0 ? Csv.Split(sample[0], d) : [];
        var columns = hasHeader
            ? first.Select((name, i) => name.Length > 0 ? name : $"Cột {i + 1}").ToList()
            : first.Select((_, i) => $"Cột {i + 1}").ToList();
        return new CsvLayout(d, columns, sniff);
    }
}

public sealed record CsvTransformOptions
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    /// <summary>null = tự nhận.</summary>
    public char? InputDelimiter { get; init; }
    /// <summary>null = giữ như đầu vào.</summary>
    public char? OutputDelimiter { get; init; }
    /// <summary>Chỉ số cột (0-based) theo thứ tự ghi ra; null = mọi cột như cũ.</summary>
    public IReadOnlyList<int>? Columns { get; init; }
    /// <summary>Bọc mọi ô trong ngoặc kép (mặc định chỉ bọc khi cần).</summary>
    public bool QuoteAll { get; init; }
    public OutputEncoding Encoding { get; init; } = OutputEncoding.Utf8;
    public NewlineMode Newline { get; init; } = NewlineMode.Keep;
}

/// <param name="ShortRecords">Bản ghi thiếu cột được chọn (ô thiếu ghi rỗng).</param>
public sealed record CsvTransformResult(long Records, long ShortRecords, char InputDelimiter, char OutputDelimiter);

/// <summary>Ghi lại CSV: chọn / sắp xếp cột và / hoặc đổi dấu phân cách - lõi chung của 2 trang "Chọn / sắp cột" và
/// "Đổi dấu phân cách". Đọc theo bản ghi (ô nhiều dòng giữ nguyên), ghi lại có ngoặc kép khi cần.</summary>
public static class CsvTransformer
{
    public static CsvTransformResult Transform(CsvTransformOptions o, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var layout = CsvLayout.Read(o.SourcePath, o.InputDelimiter);
        char outDelim = o.OutputDelimiter ?? layout.Delimiter;
        var columns = o.Columns;
        if (columns is { Count: 0 })
        {
            throw new ArgumentException("Chưa chọn cột nào.");
        }
        var throttle = new ProgressThrottle(progress);
        long records = 0, shortRecords = 0;
        using var output = new OutputFile(o.OutputPath, TextEncodings.Resolve(o.Encoding, layout.Encoding.Encoding), o.Newline);
        using var reader = new CsvRecordReader(LineReader.Open(o.SourcePath, layout.Encoding.Encoding));
        while (reader.TryRead(out var record))
        {
            records++;
            if ((records & 0x3FF) == 0)
            {
                ct.ThrowIfCancellationRequested();
                throttle.Report(reader.Lines.Fraction, $"{records:N0} bản ghi");
            }
            var fields = Csv.Split(record.Text, layout.Delimiter);
            IEnumerable<string> picked = fields;
            if (columns is not null)
            {
                if (columns.Any(i => i >= fields.Count))
                {
                    shortRecords++;
                }
                picked = columns.Select(i => i < fields.Count ? fields[i] : string.Empty);
            }
            var text = o.QuoteAll
                ? string.Join(outDelim, picked.Select(f => "\"" + f.Replace("\"", "\"\"") + "\""))
                : Csv.Join(picked, outDelim);
            output.WriteLine(new TextLine(text, record.Ending));
        }
        ct.ThrowIfCancellationRequested();
        output.Commit();
        throttle.Report(1, null, force: true);
        return new CsvTransformResult(records, shortRecords, layout.Delimiter, outDelim);
    }
}

public sealed record CsvSplitOptions
{
    public required string SourcePath { get; init; }
    public required string OutputFolder { get; init; }
    public required int Column { get; init; }
    public char? Delimiter { get; init; }
    /// <summary>Dòng đầu là tiêu đề: không tính là dữ liệu, lặp ở đầu mọi file.</summary>
    public bool HasHeader { get; init; } = true;
    /// <summary>Quá số giá trị khác nhau này thì dừng (thường là chọn nhầm cột mã / số tiền).</summary>
    public int MaxFiles { get; init; } = 5000;
    public OutputEncoding Encoding { get; init; } = OutputEncoding.Utf8;
    public NewlineMode Newline { get; init; } = NewlineMode.Keep;
}

public sealed record CsvSplitPart(string Value, string Path, long Records);

public sealed record CsvSplitResult(IReadOnlyList<CsvSplitPart> Parts, long Records);

/// <summary>
/// Tách CSV theo giá trị 1 cột: mỗi giá trị 1 file (<c>ten_&lt;giá trị&gt;.csv</c>), giữ thứ tự bản ghi, lặp dòng tiêu đề.
/// Chỉ mở tối đa <see cref="MaxOpen"/> file cùng lúc - file ít dùng nhất được đóng (ghi xong) và mở lại để ghi tiếp khi
/// cần, nên cột có hàng nghìn giá trị không làm cạn handle / RAM. Huỷ / lỗi: xoá mọi file đã tạo.
/// </summary>
public static class CsvColumnSplitter
{
    public const int MaxOpen = 64;

    private sealed class Part(string value, string path)
    {
        public string Value { get; } = value;
        public string Path { get; } = path;
        public OutputFile? Writer;
        public long Records;
        public long LastUse;
    }

    public static CsvSplitResult Split(CsvSplitOptions o, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var layout = CsvLayout.Read(o.SourcePath, o.Delimiter, o.HasHeader);
        var encoding = TextEncodings.Resolve(o.Encoding, layout.Encoding.Encoding);
        Directory.CreateDirectory(o.OutputFolder);
        var baseName = Path.GetFileNameWithoutExtension(o.SourcePath);
        var ext = Path.GetExtension(o.SourcePath);
        var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var open = new List<Part>();
        var throttle = new ProgressThrottle(progress);
        TextLine? header = null;
        long records = 0, tick = 0;

        try
        {
            using var reader = new CsvRecordReader(LineReader.Open(o.SourcePath, layout.Encoding.Encoding));
            while (reader.TryRead(out var record))
            {
                if (o.HasHeader && header is null)
                {
                    header = record.Ending == LineEnding.None ? record with { Ending = LineEnding.CrLf } : record;
                    continue;
                }
                records++;
                if ((records & 0x3FF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    throttle.Report(reader.Lines.Fraction, $"{parts.Count:N0} giá trị");
                }
                var fields = Csv.Split(record.Text, layout.Delimiter);
                var value = o.Column < fields.Count ? fields[o.Column] : string.Empty;
                if (!parts.TryGetValue(value, out var part))
                {
                    if (parts.Count >= o.MaxFiles)
                    {
                        throw new InvalidOperationException($"Cột này có hơn {o.MaxFiles:N0} giá trị khác nhau - có lẽ chọn nhầm cột (mã, số tiền...). Đã dừng, không tạo file nào.");
                    }
                    part = new Part(value, Path.Combine(o.OutputFolder, UniqueName(baseName, value, ext, usedNames)));
                    parts[value] = part;
                }
                if (part.Writer is null)
                {
                    if (open.Count >= MaxOpen)
                    {
                        var oldest = open.MinBy(p => p.LastUse)!;
                        oldest.Writer!.Commit();
                        oldest.Writer = null;
                        open.Remove(oldest);
                    }
                    bool fresh = part.Records == 0;
                    part.Writer = new OutputFile(part.Path, encoding, o.Newline, append: !fresh, bufferSize: 1 << 16);
                    open.Add(part);
                    if (fresh && header is { } h)
                    {
                        part.Writer.WriteLine(h);
                    }
                }
                part.LastUse = ++tick;
                // Bản ghi cuối file nguồn không có xuống dòng có thể không phải bản ghi cuối của file giá trị đó.
                part.Writer.EndLineIfNeeded();
                part.Writer.WriteLine(record);
                part.Records++;
            }
            ct.ThrowIfCancellationRequested();
            foreach (var part in open)
            {
                part.Writer!.Commit();
                part.Writer = null;
            }
        }
        catch
        {
            foreach (var part in parts.Values)
            {
                part.Writer?.Dispose();
                try
                {
                    File.Delete(part.Path);
                }
                catch (IOException)
                {
                }
            }
            throw;
        }

        throttle.Report(1, null, force: true);
        var list = parts.Values.Select(p => new CsvSplitPart(p.Value, p.Path, p.Records)).OrderByDescending(p => p.Records).ToList();
        return new CsvSplitResult(list, records);
    }

    /// <summary>Tên file an toàn cho 1 giá trị: bỏ ký tự cấm, cắt ngắn; trùng (Windows không phân biệt hoa thường: "A" và
    /// "a") thì thêm hậu tố _2, _3...</summary>
    private static string UniqueName(string baseName, string value, string ext, HashSet<string> used)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        if (clean.Length == 0)
        {
            clean = "(trong)";
        }
        if (clean.Length > 80)
        {
            clean = clean[..80];
        }
        var name = $"{baseName}_{clean}{ext}";
        for (int n = 2; !used.Add(name); n++)
        {
            name = $"{baseName}_{clean}_{n}{ext}";
        }
        return name;
    }
}

public sealed record DedupeOptions
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    /// <summary>So theo các cột này (0-based); null = cả dòng.</summary>
    public IReadOnlyList<int>? KeyColumns { get; init; }
    public bool IgnoreCase { get; init; }
    /// <summary>Bỏ khoảng trắng đầu / cuối mỗi ô (hoặc cả dòng) trước khi so.</summary>
    public bool Trim { get; init; }
    /// <summary>Dòng đầu là tiêu đề - luôn giữ, không đem so.</summary>
    public bool HasHeader { get; init; } = true;
    /// <summary>Đọc theo bản ghi CSV (mặc định: file .csv/.tsv hoặc khi so theo cột).</summary>
    public bool? CsvRecords { get; init; }
    public char? Delimiter { get; init; }
    public OutputEncoding Encoding { get; init; } = OutputEncoding.Utf8;
    public NewlineMode Newline { get; init; } = NewlineMode.Keep;
}

/// <param name="DuplicateSamples">Số thứ tự (trong file nguồn) của vài bản ghi trùng đầu tiên.</param>
public sealed record DedupeResult(long Kept, long Removed, IReadOnlyList<long> DuplicateSamples);

/// <summary>
/// Bỏ dòng / bản ghi trùng, giữ lần xuất hiện đầu tiên, giữ nguyên thứ tự. Chỉ lưu mã băm 64-bit (XxHash3) của khoá thay
/// vì cả dòng, trong <see cref="UInt64Set"/> (~11 byte / dòng): 26 triệu dòng khác nhau ≈ 300 MB. Xác suất 2 dòng khác nhau trùng mã băm ở 25 triệu
/// dòng ≈ 2·10⁻⁵ - chấp nhận được cho công cụ dọn dữ liệu.
/// </summary>
public static class CsvDeduplicator
{
    private const int MaxSamples = 10;

    public static DedupeResult Dedupe(DedupeOptions o, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        bool byColumns = o.KeyColumns is { Count: > 0 };
        bool csv = o.CsvRecords ?? (byColumns || Csv.IsCsvPath(o.SourcePath));
        var sniff = EncodingSniffer.Detect(o.SourcePath);
        char delimiter = byColumns ? CsvLayout.Read(o.SourcePath, o.Delimiter).Delimiter : ',';
        // So cả dòng: số khoá ≈ số dòng → cấp sẵn đủ chỗ (khỏi mở rộng giữa chừng). So theo cột: số giá trị khác nhau
        // thường ít hơn nhiều → bắt đầu nhỏ, lớn dần khi cần.
        long expected = byColumns ? 1_000_000 : EstimateLines(o.SourcePath);
        UInt64Set seen;
        try
        {
            seen = new UInt64Set(expected);
        }
        catch (OutOfMemoryException)
        {
            throw NotEnoughMemory(expected);
        }
        var samples = new List<long>();
        var throttle = new ProgressThrottle(progress);
        long index = 0, kept = 0, removed = 0;
        using var output = new OutputFile(o.OutputPath, TextEncodings.Resolve(o.Encoding, sniff.Encoding), o.Newline);
        using var lines = LineReader.Open(o.SourcePath, sniff.Encoding);
        using var records = new CsvRecordReader(lines);
        var key = new StringBuilder();
        while (csv ? records.TryRead(out var record) : lines.TryRead(out record))
        {
            index++;
            if ((index & 0x3FF) == 0)
            {
                ct.ThrowIfCancellationRequested();
                throttle.Report(lines.Fraction, $"giữ {kept:N0} · trùng {removed:N0}");
            }
            if (!(index == 1 && o.HasHeader))
            {
                bool added;
                try
                {
                    added = seen.Add(Hash(BuildKey(key, record.Text, o, byColumns, delimiter)));
                }
                catch (OutOfMemoryException)
                {
                    throw NotEnoughMemory(seen.Count);
                }
                if (!added)
                {
                    removed++;
                    if (samples.Count < MaxSamples)
                    {
                        samples.Add(index);
                    }
                    continue;
                }
            }
            output.EndLineIfNeeded();
            output.WriteLine(record);
            kept++;
        }
        ct.ThrowIfCancellationRequested();
        output.Commit();
        throttle.Report(1, null, force: true);
        return new DedupeResult(kept, removed, samples);
    }

    private static InvalidOperationException NotEnoughMemory(long keys) => new(
        $"Không đủ RAM để nhớ ~{keys:N0} dòng khác nhau (≈ {keys * 11 / 1_048_576:N0} MB). Thử so theo cột, hoặc tách file trước rồi bỏ trùng từng phần.");

    /// <summary>Ước số dòng từ dung lượng file và độ dài dòng trung bình của 64 KB đầu (+10%).</summary>
    internal static long EstimateLines(string path)
    {
        long size = new FileInfo(path).Length;
        var buffer = new byte[64 * 1024];
        int read;
        using (var stream = LineReader.OpenRead(path))
        {
            read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        }
        int newlines = buffer.AsSpan(0, read).Count((byte)'\n');
        double bytesPerLine = newlines > 0 ? read / (double)newlines : 100;
        return (long)(size / bytesPerLine * 1.1) + 16;
    }

    private static string BuildKey(StringBuilder sb, string text, DedupeOptions o, bool byColumns, char delimiter)
    {
        if (!byColumns)
        {
            var whole = o.Trim ? text.Trim() : text;
            return o.IgnoreCase ? whole.ToUpperInvariant() : whole;
        }
        var fields = Csv.Split(text, delimiter);
        sb.Clear();
        foreach (int i in o.KeyColumns!)
        {
            var f = i < fields.Count ? fields[i] : string.Empty;
            if (o.Trim)
            {
                f = f.Trim();
            }
            // Ký tự \u0001 làm vách ngăn: ("a","bc") và ("ab","c") không thành cùng 1 khoá.
            sb.Append(o.IgnoreCase ? f.ToUpperInvariant() : f).Append('\u0001');
        }
        return sb.ToString();
    }

    private static ulong Hash(string key) => XxHash3.HashToUInt64(MemoryMarshal.AsBytes(key.AsSpan()));
}
