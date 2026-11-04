namespace CsvEditor.Services;

/// <summary>Tính vị trí các ô xếp từ trái sang phải, hết chỗ thì xuống hàng (dùng cho thẻ lọc phía trên bảng). Tách khỏi
/// <c>Views.WrapPanel</c> để test được không cần WinUI. Ô rộng hơn cả hàng vẫn nằm riêng 1 hàng, không xuống hàng thừa.</summary>
public static class WrapLayout
{
    public readonly record struct Slot(double X, double Y, double RowHeight);

    public static (Slot[] Slots, double Width, double Height) Compute(
        IReadOnlyList<(double Width, double Height)> items, double availableWidth, double spacing)
    {
        var slots = new Slot[items.Count];
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        var rowStart = 0;

        void EndRow(int end)
        {
            for (var i = rowStart; i < end; i++)
            {
                slots[i] = slots[i] with { RowHeight = rowHeight };
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            var (width, height) = items[i];
            if (i > rowStart && x + spacing + width > availableWidth)
            {
                EndRow(i);
                y += rowHeight + spacing;
                x = 0;
                rowHeight = 0;
                rowStart = i;
            }
            else if (i > rowStart)
            {
                x += spacing;
            }

            slots[i] = new Slot(x, y, 0);
            x += width;
            rowHeight = Math.Max(rowHeight, height);
            usedWidth = Math.Max(usedWidth, x);
        }

        EndRow(items.Count);
        return (slots, usedWidth, items.Count == 0 ? 0 : y + rowHeight);
    }
}
