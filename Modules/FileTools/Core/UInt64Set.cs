namespace FileTools.Core;

/// <summary>
/// Tập số 64-bit gọn cho bỏ dòng trùng: địa chỉ mở, dò tuyến tính, 8 byte / ô (HashSet&lt;ulong&gt; tốn ~20 byte / phần tử
/// và lúc mở rộng giữ cả mảng cũ lẫn mới → 26 triệu dòng khác nhau làm máy RAM thấp hết bộ nhớ). Cấp sẵn đủ chỗ theo số
/// phần tử ước tính để không phải mở rộng giữa chừng; vượt ước tính thì vẫn mở rộng ×1,5. Giá trị 0 dành cho "ô trống"
/// nên được đổi thành 1 (mã băm 64-bit trùng thêm 1 giá trị - không đáng kể).
/// </summary>
public sealed class UInt64Set
{
    private const double MaxLoad = 0.75;
    private ulong[] _slots;
    private long _limit;

    public UInt64Set(long expected)
    {
        _slots = new ulong[SizeFor(Math.Max(16, expected))];
        _limit = (long)(_slots.LongLength * MaxLoad);
    }

    public long Count { get; private set; }

    /// <summary>Số byte bộ nhớ đang dùng cho các ô.</summary>
    public long MemoryBytes => _slots.LongLength * sizeof(ulong);

    /// <summary>Thêm; false nếu đã có.</summary>
    public bool Add(ulong value)
    {
        if (value == 0)
        {
            value = 1;
        }
        if (Count >= _limit)
        {
            Grow();
        }
        if (!Insert(_slots, value))
        {
            return false;
        }
        Count++;
        return true;
    }

    private static bool Insert(ulong[] slots, ulong value)
    {
        long mask = slots.LongLength;
        long i = (long)(value % (ulong)mask);
        while (true)
        {
            ulong current = slots[i];
            if (current == 0)
            {
                slots[i] = value;
                return true;
            }
            if (current == value)
            {
                return false;
            }
            if (++i == mask)
            {
                i = 0;
            }
        }
    }

    private void Grow()
    {
        var bigger = new ulong[SizeFor((long)(Count * 1.5))];
        foreach (var v in _slots)
        {
            if (v != 0)
            {
                Insert(bigger, v);
            }
        }
        _slots = bigger;
        _limit = (long)(_slots.LongLength * MaxLoad);
    }

    /// <summary>Số ô cho <paramref name="count"/> phần tử ở tải ≤ 75%, làm tròn lên số lẻ (phân bố modulo đều hơn).</summary>
    private static long SizeFor(long count) => (long)(count / MaxLoad) + 1 | 1;
}
