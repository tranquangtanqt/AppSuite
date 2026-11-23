namespace MeRon.Core;

/// <summary>
/// Chia danh sách dài thành trang (trang Lịch sử chỉnh sửa): <see cref="Page"/> tính từ 0, đã kẹp vào khoảng hợp lệ
/// (danh sách ngắn lại sau khi lọc / khôi phục thì về trang cuối còn có).
/// </summary>
public readonly record struct Paging(int Count, int Page, int Size)
{
    public static Paging Of(int count, int page, int size)
    {
        var pages = Math.Max(1, (count + size - 1) / size);
        return new Paging(count, Math.Clamp(page, 0, pages - 1), size);
    }

    public int Pages => Math.Max(1, (Count + Size - 1) / Size);

    public int Skip => Page * Size;

    public int Take => Math.Max(0, Math.Min(Size, Count - Skip));

    public bool HasPrevious => Page > 0;

    public bool HasNext => Page < Pages - 1;

    /// <summary>Vd "Trang 2/50 · dòng 101–200"; 1 trang thì rỗng (không cần thanh chuyển trang).</summary>
    public string Text => Pages <= 1 ? string.Empty : $"Trang {Page + 1}/{Pages} · dòng {VnFormat.Number(Skip + 1)}–{VnFormat.Number(Skip + Take)}";
}
