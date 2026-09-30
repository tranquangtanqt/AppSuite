using System.Text;

namespace FileTools.Core;

public enum LineEnding
{
    /// <summary>Dòng cuối file không có ký tự xuống dòng.</summary>
    None,
    CrLf,
    Lf,
    Cr,
}

/// <summary>1 dòng (hoặc 1 bản ghi CSV) kèm đúng ký tự xuống dòng gốc của nó.</summary>
public readonly record struct TextLine(string Text, LineEnding Ending)
{
    public string EndingText => Ending.ToText();
}

public static class LineEndingExtensions
{
    public static string ToText(this LineEnding ending) => ending switch
    {
        LineEnding.CrLf => "\r\n",
        LineEnding.Lf => "\n",
        LineEnding.Cr => "\r",
        _ => string.Empty,
    };
}

/// <summary>
/// Đọc file text từng dòng, <b>giữ nguyên ký tự xuống dòng</b> (CRLF / LF / CR / không có) - khác
/// <see cref="StreamReader.ReadLine"/> vốn bỏ mất nó, trong khi tách / trích / nối cần ghi lại y như gốc.
/// Đọc kiểu stream (buffer 1 MB) nên RAM không tăng theo cỡ file; mở với FileShare.ReadWrite để đọc được cả log
/// đang được ghi. <see cref="ConsumedBytes"/> đếm chính xác số byte nguồn của các dòng đã trả về (tính lại qua
/// encoding) - dùng cho tiến độ và tách theo số phần.
/// </summary>
public sealed class LineReader : IDisposable
{
    private const int ByteBuffer = 1 << 20;
    private const int CharBuffer = 1 << 16;

    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly bool _leaveOpen;
    private readonly char[] _buffer = new char[CharBuffer];
    private readonly int[] _endingBytes;
    private int _pos;
    private int _len;

    public LineReader(Stream stream, Encoding encoding, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        Encoding = encoding;
        long start = stream.CanSeek ? stream.Position : 0;
        var preamble = encoding.GetPreamble();
        ConsumedBytes = start + (preamble.Length > 0 && StartsWith(stream, preamble) ? preamble.Length : 0);
        _reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, CharBuffer, leaveOpen: true);
        _endingBytes =
        [
            0,
            encoding.GetByteCount("\r\n"),
            encoding.GetByteCount("\n"),
            encoding.GetByteCount("\r"),
        ];
    }

    /// <summary>Mở file (tự nhận encoding nếu không truyền).</summary>
    public static LineReader Open(string path, Encoding? encoding = null) =>
        new(OpenRead(path), encoding ?? EncodingSniffer.Detect(path).Encoding);

    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, ByteBuffer, FileOptions.SequentialScan);

    public Encoding Encoding { get; }

    public long Length => _stream.CanSeek ? _stream.Length : 0;

    /// <summary>Số byte nguồn (kể cả BOM) của mọi dòng đã đọc.</summary>
    public long ConsumedBytes { get; private set; }

    /// <summary>Số dòng đã đọc.</summary>
    public long LinesRead { get; private set; }

    public double Fraction => Length > 0 ? Math.Min(1, (double)ConsumedBytes / Length) : 1;

    public bool TryRead(out TextLine line)
    {
        StringBuilder? pending = null;
        while (true)
        {
            if (_pos >= _len && !Fill())
            {
                if (pending is null)
                {
                    line = default;
                    return false;
                }
                line = Emit(pending.ToString(), LineEnding.None);
                return true;
            }

            var span = _buffer.AsSpan(_pos, _len - _pos);
            int i = span.IndexOfAny('\r', '\n');
            if (i < 0)
            {
                (pending ??= new StringBuilder()).Append(span);
                _pos = _len;
                continue;
            }

            string text = pending is null ? new string(span[..i]) : pending.Append(span[..i]).ToString();
            char c = span[i];
            _pos += i + 1;
            LineEnding ending = LineEnding.Lf;
            if (c == '\r')
            {
                // "\r" ở cuối buffer: phải xem ký tự đầu của buffer sau mới biết là CRLF hay CR.
                if (_pos >= _len)
                {
                    Fill();
                }
                if (_pos < _len && _buffer[_pos] == '\n')
                {
                    _pos++;
                    ending = LineEnding.CrLf;
                }
                else
                {
                    ending = LineEnding.Cr;
                }
            }
            line = Emit(text, ending);
            return true;
        }
    }

    private TextLine Emit(string text, LineEnding ending)
    {
        ConsumedBytes += Encoding.GetByteCount(text) + _endingBytes[(int)ending];
        LinesRead++;
        return new TextLine(text, ending);
    }

    private bool Fill()
    {
        _len = _reader.Read(_buffer, 0, _buffer.Length);
        _pos = 0;
        return _len > 0;
    }

    private static bool StartsWith(Stream stream, byte[] prefix)
    {
        if (!stream.CanSeek)
        {
            return false;
        }
        long at = stream.Position;
        var head = new byte[prefix.Length];
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = at;
        return read == prefix.Length && head.AsSpan().SequenceEqual(prefix);
    }

    public void Dispose()
    {
        _reader.Dispose();
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
