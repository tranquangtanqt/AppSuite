using System.Text;

namespace FileTools.Core;

/// <summary>Kết quả 1 lần đọc thêm: các dòng mới (đủ ký tự xuống dòng) + ghi chú nếu file bị xoá / xoay vòng.</summary>
public sealed record TailBatch(IReadOnlyList<string> Lines, string? Notice);

/// <summary>
/// Theo dõi file log đang được ghi (như <c>tail -f</c>). Mỗi lần <see cref="Poll"/> đọc phần byte mới từ vị trí đã đọc,
/// trả về các dòng đã hoàn chỉnh (dòng đang ghi dở - chưa có xuống dòng - được giữ lại tới lần sau). Mở file với
/// FileShare.ReadWrite | Delete nên không cản chương trình đang ghi log. File nhỏ lại (bị xoá nội dung / xoay vòng sang file
/// mới cùng tên) → đọc lại từ đầu và báo. Không dùng FileSystemWatcher: sự kiện của nó không đáng tin với file đang mở
/// ghi liên tục (nhiều chương trình chỉ flush, không đổi thời gian sửa) - đọc định kỳ theo độ dài file là chắc chắn nhất.
/// </summary>
public sealed class LogTailer(string path)
{
    private const int MaxReadPerPoll = 8 << 20;
    private readonly StringBuilder _partial = new();
    private Encoding? _encoding;
    private Decoder? _decoder;
    private long _position;
    /// <summary>Lần đọc trước kết thúc bằng CR - nếu lần này mở đầu bằng LF thì đó là nửa sau của CRLF, bỏ qua.</summary>
    private bool _skipLf;

    public string Path { get; } = path;

    /// <summary>Bắt đầu: trả về <paramref name="lastLines"/> dòng cuối hiện có, rồi theo dõi từ cuối file.</summary>
    public IReadOnlyList<string> Start(int lastLines)
    {
        var sniff = EncodingSniffer.Detect(Path);
        _encoding = sniff.Encoding;
        _decoder = _encoding.GetDecoder();
        _partial.Clear();
        var lines = new List<string>();
        using var stream = LineReader.OpenRead(Path);
        long start = LineExtractor.FindTailStart(stream, _encoding, Math.Max(0, lastLines));
        stream.Position = start;
        _position = start;
        var batch = ReadFrom(stream);
        lines.AddRange(batch);
        return lines;
    }

    public TailBatch Poll()
    {
        if (_encoding is null)
        {
            throw new InvalidOperationException("Chưa Start.");
        }
        if (!File.Exists(Path))
        {
            return new TailBatch([], "File không còn tồn tại - đang chờ file được tạo lại...");
        }
        using var stream = LineReader.OpenRead(Path);
        string? notice = null;
        if (stream.Length < _position)
        {
            // Bị cắt ngắn / xoay vòng: đọc lại từ đầu file mới.
            notice = $"File nhỏ lại ({_position:N0} → {stream.Length:N0} byte) - có thể đã xoay vòng, đọc lại từ đầu.";
            _position = 0;
            _partial.Clear();
            _skipLf = false;
            _decoder = _encoding.GetDecoder();
            var preamble = _encoding.GetPreamble();
            if (preamble.Length > 0 && stream.Length >= preamble.Length)
            {
                var head = new byte[preamble.Length];
                stream.ReadExactly(head);
                if (head.AsSpan().SequenceEqual(preamble))
                {
                    _position = preamble.Length;
                }
            }
        }
        stream.Position = _position;
        return new TailBatch(ReadFrom(stream), notice);
    }

    private List<string> ReadFrom(FileStream stream)
    {
        var lines = new List<string>();
        long available = stream.Length - _position;
        if (available <= 0)
        {
            return lines;
        }
        var bytes = new byte[(int)Math.Min(available, MaxReadPerPoll)];
        int read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        _position += read;
        var chars = new char[_decoder!.GetCharCount(bytes, 0, read, flush: false)];
        int n = _decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
        int start = 0;
        if (_skipLf && n > 0 && chars[0] == '\n')
        {
            start = 1;
        }
        if (n > 0)
        {
            _skipLf = false;
        }
        for (int i = start; i < n; i++)
        {
            char c = chars[i];
            if (c != '\n' && c != '\r')
            {
                continue;
            }
            _partial.Append(chars, start, i - start);
            // "\r\n" → 1 dòng; "\r" ở cuối khối có thể là nửa đầu của "\r\n" → nhớ để bỏ "\n" đầu khối sau.
            if (c == '\r' && i + 1 < n && chars[i + 1] == '\n')
            {
                i++;
            }
            else if (c == '\r' && i + 1 == n)
            {
                _skipLf = true;
            }
            lines.Add(_partial.ToString());
            _partial.Clear();
            start = i + 1;
        }
        _partial.Append(chars, start, n - start);
        return lines;
    }
}
