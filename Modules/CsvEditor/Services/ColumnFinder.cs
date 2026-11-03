using CsvEditor.Models;

namespace CsvEditor.Services;

/// <summary>"Đi tới cột" (Ctrl+Shift+G): tìm cột theo 1 phần tên, không phân biệt hoa thường. Thứ tự: trùng hẳn tên → cột
/// ở vị trí đó nếu gõ toàn số (1, 2… - số thứ tự cột như trong khung Xem dọc) → tên bắt đầu bằng chữ đã gõ → tên chứa chữ
/// đã gõ (cùng nhóm thì theo thứ tự cột).</summary>
public static class ColumnFinder
{
    /// <summary>Chỉ số (từ 0) các cột khớp <paramref name="query"/>, cột khớp nhất trước. Rỗng nếu không cột nào khớp.</summary>
    public static IReadOnlyList<int> Find(IReadOnlyList<CsvColumn> columns, string query)
    {
        var text = query.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var position = int.TryParse(text, out var number) && number >= 1 && number <= columns.Count ? number - 1 : -1;
        return columns
            .Select((column, index) => (index, rank: Rank(column.Name, text, index == position)))
            .Where(m => m.rank >= 0)
            .OrderBy(m => m.rank)
            .ThenBy(m => m.index)
            .Select(m => m.index)
            .ToList();
    }

    private static int Rank(string name, string text, bool isTypedPosition) =>
        string.Equals(name, text, StringComparison.OrdinalIgnoreCase) ? 0
        : isTypedPosition ? 1
        : name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 2
        : name.Contains(text, StringComparison.OrdinalIgnoreCase) ? 3
        : -1;
}
