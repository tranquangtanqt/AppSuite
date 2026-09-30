using System.Text;

namespace FileTools.Core;

public enum ExtractMode
{
    /// <summary>N dòng đầu.</summary>
    Head,
    /// <summary>Từ dòng X tới dòng Y (đánh số từ 1, gồm cả 2 đầu).</summary>
    Range,
    /// <summary>N dòng cuối.</summary>
    Tail,
}

public sealed record ExtractOptions
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public ExtractMode Mode { get; init; } = ExtractMode.Head;
    /// <summary>Số dòng cho Head / Tail.</summary>
    public long Count { get; init; } = 1000;
    public long From { get; init; } = 1;
    public long To { get; init; } = 1000;
    /// <summary>Range / Tail không bắt đầu từ dòng 1: chèn thêm dòng 1 (tiêu đề CSV) lên đầu.</summary>
    public bool IncludeHeader { get; init; }
    public OutputEncoding Encoding { get; init; } = OutputEncoding.Utf8;
    public NewlineMode Newline { get; init; } = NewlineMode.Keep;
}

/// <param name="FirstLineNumber">Số thứ tự (trong file nguồn) của dòng đầu tiên đã trích; null với Tail (không
/// đếm cả file nên không biết).</param>
public sealed record ExtractResult(long Lines, long? FirstLineNumber, string Encoding);

/// <summary>
/// Trích 1 đoạn dòng ra file (thường là file tạm để mở xem). Head / Range đọc từ đầu và <b>dừng ngay</b> khi đủ;
/// Tail <b>đọc ngược từ cuối file</b> theo khối 64 KB tìm đủ N ký tự xuống dòng rồi mới đọc xuôi phần đó - file vài GB
/// vẫn gần như tức thì. Tail đếm theo LF (CRLF / LF); file chỉ dùng CR (Mac cũ) coi như 1 dòng.
/// </summary>
public static class LineExtractor
{
    private const int TailBlock = 64 * 1024;

    public static ExtractResult Extract(ExtractOptions o, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var sniff = EncodingSniffer.Detect(o.SourcePath);
        var encoding = TextEncodings.Resolve(o.Encoding, sniff.Encoding);
        var throttle = new ProgressThrottle(progress);
        using var output = new OutputFile(o.OutputPath, encoding, o.Newline);
        long? firstNumber;

        if (o.Mode == ExtractMode.Tail)
        {
            firstNumber = null;
            if (o.IncludeHeader)
            {
                using var head = LineReader.Open(o.SourcePath, sniff.Encoding);
                if (head.TryRead(out var header))
                {
                    WriteForced(output, header);
                }
            }
            using var stream = LineReader.OpenRead(o.SourcePath);
            long start = FindTailStart(stream, sniff.Encoding, Math.Max(0, o.Count), ct);
            // Tail dòng 1 đã là header rồi thì bỏ để không lặp.
            bool skipFirst = o.IncludeHeader && start <= PreambleLength(stream, sniff.Encoding);
            stream.Position = start;
            using var lines = new LineReader(stream, WithoutPreamble(sniff.Encoding, start), leaveOpen: true);
            while (lines.TryRead(out var line))
            {
                if (skipFirst && lines.LinesRead == 1)
                {
                    continue;
                }
                output.WriteLine(line);
            }
        }
        else
        {
            long from = o.Mode == ExtractMode.Head ? 1 : Math.Max(1, o.From);
            long to = o.Mode == ExtractMode.Head ? o.Count : Math.Max(from, o.To);
            firstNumber = from;
            using var lines = LineReader.Open(o.SourcePath, sniff.Encoding);
            while (lines.LinesRead < to && lines.TryRead(out var line))
            {
                long n = lines.LinesRead;
                if ((n & 0x3FF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    throttle.Report(n < from ? lines.Fraction : (double)(n - from) / Math.Max(1, to - from + 1));
                }
                if (n == 1 && o.IncludeHeader && from > 1)
                {
                    WriteForced(output, line);
                }
                if (n >= from)
                {
                    output.WriteLine(line);
                }
            }
            if (lines.LinesRead < from)
            {
                firstNumber = null;
            }
        }

        ct.ThrowIfCancellationRequested();
        long written = output.LinesWritten;
        output.Commit();
        throttle.Report(1, null, force: true);
        return new ExtractResult(written, firstNumber, TextEncodings.Describe(encoding));
    }

    /// <summary>Ghi dòng tiêu đề, luôn kèm xuống dòng (file 1 dòng thì dòng đó không có).</summary>
    private static void WriteForced(OutputFile output, TextLine line) =>
        output.WriteLine(line.Ending == LineEnding.None ? line with { Ending = LineEnding.CrLf } : line);

    /// <summary>Vị trí byte bắt đầu của <paramref name="count"/> dòng cuối. Ký tự xuống dòng ở tận cùng file không
    /// tính (nó kết thúc dòng cuối chứ không mở dòng mới).</summary>
    public static long FindTailStart(Stream stream, Encoding encoding, long count, CancellationToken ct = default)
    {
        long dataStart = PreambleLength(stream, encoding);
        if (count <= 0)
        {
            return stream.Length;
        }
        var (unit, lf) = NewlineBytes(encoding);
        long end = dataStart + (stream.Length - dataStart) / unit * unit;
        var buffer = new byte[TailBlock];
        long found = 0;
        bool atFileEnd = true;
        long pos = end;
        while (pos > dataStart)
        {
            ct.ThrowIfCancellationRequested();
            long blockStart = Math.Max(dataStart, pos - TailBlock);
            int size = (int)(pos - blockStart);
            stream.Position = blockStart;
            stream.ReadExactly(buffer, 0, size);
            for (int i = size - unit; i >= 0; i -= unit)
            {
                if (buffer.AsSpan(i, unit).SequenceEqual(lf))
                {
                    if (atFileEnd && blockStart + i + unit == end)
                    {
                        atFileEnd = false;
                        continue;
                    }
                    if (++found == count)
                    {
                        return blockStart + i + unit;
                    }
                }
                atFileEnd = false;
            }
            pos = blockStart;
        }
        return dataStart;
    }

    private static (int Unit, byte[] Lf) NewlineBytes(Encoding encoding) => encoding switch
    {
        UnicodeEncoding u when u.CodePage == 1201 => (2, [0x00, 0x0A]),
        UnicodeEncoding => (2, [0x0A, 0x00]),
        // UTF-8 / Shift-JIS / ASCII: byte 0x0A không bao giờ nằm trong 1 ký tự nhiều byte.
        _ => (1, [0x0A]),
    };

    private static long PreambleLength(Stream stream, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0 || stream.Length < preamble.Length)
        {
            return 0;
        }
        var head = new byte[preamble.Length];
        long at = stream.Position;
        stream.Position = 0;
        stream.ReadExactly(head);
        stream.Position = at;
        return head.AsSpan().SequenceEqual(preamble) ? preamble.Length : 0;
    }

    /// <summary>Đọc từ giữa file: encoding không được đòi BOM (StreamReader sẽ coi đó là nội dung).</summary>
    private static Encoding WithoutPreamble(Encoding encoding, long start) => start == 0 ? encoding : encoding switch
    {
        UTF8Encoding => new UTF8Encoding(false),
        UnicodeEncoding u => new UnicodeEncoding(u.CodePage == 1201, false),
        _ => encoding,
    };
}
