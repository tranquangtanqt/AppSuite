namespace ScreenCapture.Models;

/// <summary>Ribbon không đủ chỗ: tính số nhóm bên phải phải chuyển vào nút Thêm.</summary>
public static class RibbonOverflow
{
    /// <param name="widths">Bề rộng các nhóm đang hiện, từ trái sang phải.</param>
    /// <param name="available">Bề rộng ribbon.</param>
    /// <param name="moreButtonWidth">Bề rộng nút Thêm (chỉ tính khi có nhóm phải chuyển).</param>
    /// <returns>Số nhóm chuyển, tính từ bên phải; luôn giữ lại ít nhất 1 nhóm.</returns>
    public static int HiddenCount(IReadOnlyList<double> widths, double available, double moreButtonWidth)
    {
        var total = widths.Sum();
        if (total <= available)
        {
            return 0;
        }

        var hidden = 0;
        while (hidden < widths.Count - 1 && total + moreButtonWidth > available)
        {
            hidden++;
            total -= widths[widths.Count - hidden];
        }
        return hidden;
    }
}
