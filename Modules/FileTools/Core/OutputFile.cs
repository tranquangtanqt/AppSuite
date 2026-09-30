using System.Text;

namespace FileTools.Core;

/// <summary>Ký tự xuống dòng của file đầu ra.</summary>
public enum NewlineMode
{
    /// <summary>Giữ như dòng nguồn.</summary>
    Keep,
    CrLf,
    Lf,
}

/// <summary>
/// Ghi 1 file đầu ra an toàn: ghi vào "&lt;tên&gt;.partial" rồi <see cref="Commit"/> mới đổi tên thành file thật -
/// huỷ giữa chừng / lỗi thì <see cref="Dispose"/> xoá file dở, không để lại file thiếu nội dung trông như đúng.
/// Đếm <see cref="BytesWritten"/> (kể cả BOM) để tách theo dung lượng.
/// </summary>
public sealed class OutputFile : IDisposable
{
    private readonly string _partialPath;
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly NewlineMode _newline;
    private readonly CountingFallback _fallback;
    private bool _committed;
    private bool _disposed;

    /// <param name="append">Ghi tiếp vào file đã có (đã <see cref="Commit"/> trước đó): file được đổi tên lại thành
    /// .partial trong lúc ghi, không ghi lại BOM. Dùng khi tách theo giá trị cột phải đóng bớt file đang mở.</param>
    /// <param name="bufferSize">Buffer của FileStream - nhỏ lại khi mở nhiều file cùng lúc.</param>
    public OutputFile(string path, Encoding encoding, NewlineMode newline = NewlineMode.Keep, bool append = false, int bufferSize = 1 << 20)
    {
        Path = path;
        // Bản sao có bộ đếm ký tự không biểu diễn được (vd chữ Việt → Shift-JIS) - vẫn ghi "?" như mặc định
        // nhưng báo được cho người dùng thay vì âm thầm mất chữ.
        // Encoding (đo số byte) và bản của StreamWriter (đếm) tách riêng - GetByteCount cũng gọi fallback, dùng chung
        // thì 1 ký tự bị đếm nhiều lần.
        _fallback = new CountingFallback();
        var counted = (Encoding)encoding.Clone();
        counted.EncoderFallback = _fallback;
        var measure = (Encoding)encoding.Clone();
        measure.EncoderFallback = new EncoderReplacementFallback("?");
        Encoding = measure;
        encoding = counted;
        _newline = newline;
        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }
        _partialPath = path + ".partial";
        bool resume = append && File.Exists(path);
        if (resume)
        {
            File.Move(path, _partialPath, overwrite: true);
        }
        _stream = new FileStream(_partialPath, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize);
        // StreamWriter chỉ ghi BOM khi đứng ở đầu file - ghi tiếp thì không lặp BOM.
        _writer = new StreamWriter(_stream, encoding, Math.Min(bufferSize, 1 << 16));
        BytesWritten = resume ? _stream.Length : encoding.GetPreamble().Length;
        EndsWithNewline = resume;
    }

    public string Path { get; }
    public Encoding Encoding { get; }
    public long BytesWritten { get; private set; }
    public long LinesWritten { get; private set; }

    /// <summary>Số ký tự không có trong encoding đầu ra (đã ghi thành "?"). Chỉ đầy đủ sau <see cref="Commit"/> (ký tự
    /// được mã hoá khi xả buffer).</summary>
    public long LostChars => _fallback.Count;

    /// <summary>Số dòng có ký tự xuống dòng bị đổi (CRLF → LF...).</summary>
    public long EndingsChanged { get; private set; }

    /// <summary>Dòng cuối đã ghi có ký tự xuống dòng chưa (false khi chưa ghi gì).</summary>
    public bool EndsWithNewline { get; private set; }

    /// <summary>Kiểu xuống dòng gặp gần nhất - dùng khi phải tự thêm xuống dòng (vd nối file mà file trước
    /// không kết thúc bằng xuống dòng).</summary>
    public LineEnding LastEnding { get; private set; } = LineEnding.CrLf;

    /// <summary>Số byte 1 dòng sẽ chiếm khi ghi (để quyết định sang phần mới trước khi ghi).</summary>
    public long MeasureLine(in TextLine line) =>
        Encoding.GetByteCount(line.Text) + Encoding.GetByteCount(Resolve(line.Ending).ToText());

    public void WriteLine(in TextLine line)
    {
        var ending = Resolve(line.Ending);
        if (ending != line.Ending)
        {
            EndingsChanged++;
        }
        Write(line.Text);
        Write(ending.ToText());
        LinesWritten++;
        EndsWithNewline = ending != LineEnding.None;
        if (ending != LineEnding.None)
        {
            LastEnding = ending;
        }
    }

    /// <summary>Ghi 1 dòng chữ với kiểu xuống dòng mặc định của file (dòng tiêu đề tự chèn, dòng phân cách...).</summary>
    public void WriteLine(string text) => WriteLine(new TextLine(text, LastEnding));

    /// <summary>Thêm xuống dòng nếu dòng cuối đã ghi chưa có.</summary>
    public void EndLineIfNeeded()
    {
        if (LinesWritten > 0 && !EndsWithNewline)
        {
            var ending = Resolve(LastEnding);
            Write(ending.ToText());
            EndsWithNewline = true;
        }
    }

    private LineEnding Resolve(LineEnding source) => source == LineEnding.None ? LineEnding.None : _newline switch
    {
        NewlineMode.CrLf => LineEnding.CrLf,
        NewlineMode.Lf => LineEnding.Lf,
        _ => source,
    };

    private void Write(string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        _writer.Write(text);
        BytesWritten += Encoding.GetByteCount(text);
    }

    /// <summary>Ghi xong: đóng file và đổi tên từ .partial thành tên thật (ghi đè nếu đã có).</summary>
    public void Commit()
    {
        _writer.Flush();
        _writer.Dispose();
        File.Move(_partialPath, Path, overwrite: true);
        _committed = true;
        _disposed = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            _writer.Dispose();
        }
        catch (IOException)
        {
        }
        if (!_committed)
        {
            try
            {
                File.Delete(_partialPath);
            }
            catch (IOException)
            {
            }
        }
    }
}

/// <summary>Giống fallback mặc định (thay bằng "?") nhưng đếm số ký tự bị thay.</summary>
internal sealed class CountingFallback : EncoderFallback
{
    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public override int MaxCharCount => 1;

    public override EncoderFallbackBuffer CreateFallbackBuffer() => new Buffer(this);

    private sealed class Buffer(CountingFallback owner) : EncoderFallbackBuffer
    {
        private int _remaining;

        public override int Remaining => _remaining;

        public override bool Fallback(char charUnknown, int index)
        {
            Interlocked.Increment(ref owner._count);
            _remaining = 1;
            return true;
        }

        public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index)
        {
            Interlocked.Increment(ref owner._count);
            _remaining = 1;
            return true;
        }

        public override char GetNextChar()
        {
            if (_remaining == 0)
            {
                return '\0';
            }
            _remaining--;
            return '?';
        }

        public override bool MovePrevious()
        {
            if (_remaining == 1)
            {
                return false;
            }
            _remaining++;
            return true;
        }

        public override void Reset() => _remaining = 0;
    }
}
