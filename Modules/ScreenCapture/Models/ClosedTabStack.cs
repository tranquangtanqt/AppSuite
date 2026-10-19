namespace ScreenCapture.Models;

/// <summary>
/// Các tab vừa đóng để mở lại (Ctrl+Shift+T, 2026-10-19) - như trình duyệt: mở lại tab đóng gần nhất trước, bấm tiếp thì lùi
/// dần. Giữ cả tab trong bộ nhớ (ảnh, hình vẽ, Undo, zoom) nên có giới hạn: tối đa <see cref="MaxCount"/> tab và
/// <see cref="MaxBytes"/> (Cài đặt > Phiên làm việc) - quá thì bỏ tab đóng lâu nhất. Chỉ sống trong lúc app chạy (không ghi
/// ra ổ).
/// </summary>
public sealed class ClosedTabStack<T> where T : class
{
    public const int DefaultMaxCount = 10;
    public const long DefaultMaxBytes = 300L * 1024 * 1024;

    private readonly List<Entry> _entries = []; // cuối = đóng gần nhất

    public sealed record Entry(T Item, int Index, long Bytes);

    /// <summary>0 = tắt: không giữ tab nào.</summary>
    public int MaxCount { get; set; } = DefaultMaxCount;
    public long MaxBytes { get; set; } = DefaultMaxBytes;

    /// <summary>Đổi giới hạn (Cài đặt > Phiên làm việc) - bỏ ngay các tab cũ nhất vượt giới hạn mới.</summary>
    public IReadOnlyList<T> ApplyLimits(int maxCount, long maxBytes)
    {
        MaxCount = Math.Max(0, maxCount);
        MaxBytes = Math.Max(1, maxBytes);
        var dropped = Trim();
        if (dropped.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return dropped;
    }

    /// <summary>Bỏ tab cũ nhất tới khi trong giới hạn; luôn giữ tab mới nhất (1 ảnh lớn hơn cả giới hạn bộ nhớ vẫn mở lại
    /// được) trừ khi tắt hẳn (<see cref="MaxCount"/> = 0).</summary>
    private List<T> Trim()
    {
        var dropped = new List<T>();
        while (_entries.Count > 0 && (_entries.Count > MaxCount || (_entries.Count > 1 && TotalBytes > MaxBytes)))
        {
            dropped.Add(_entries[0].Item);
            _entries.RemoveAt(0);
        }
        return dropped;
    }

    public int Count => _entries.Count;

    public long TotalBytes => _entries.Sum(e => e.Bytes);

    /// <summary>Đổi số tab (bật / tắt mục "Mở lại tab vừa đóng" ở menu).</summary>
    public event EventHandler? Changed;

    /// <summary>Tab đóng gần nhất (để ghi tên lên menu), null nếu trống.</summary>
    public T? Peek() => _entries.Count == 0 ? null : _entries[^1].Item;

    /// <param name="index">Vị trí tab lúc đóng - mở lại vào đúng chỗ đó.</param>
    /// <param name="bytes">Bộ nhớ ước tính của tab. 1 tab to hơn cả giới hạn vẫn giữ (chỉ mình nó).</param>
    /// <returns>Các tab bị bỏ vì quá giới hạn (bên gọi giải phóng nếu cần).</returns>
    public IReadOnlyList<T> Push(T item, int index, long bytes)
    {
        _entries.Add(new Entry(item, Math.Max(0, index), Math.Max(0, bytes)));
        var dropped = Trim();
        Changed?.Invoke(this, EventArgs.Empty);
        return dropped;
    }

    /// <summary>Lấy ra tab đóng gần nhất; null nếu trống.</summary>
    public Entry? Pop()
    {
        if (_entries.Count == 0)
        {
            return null;
        }
        var entry = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }
}
