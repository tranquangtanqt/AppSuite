using CsvEditor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace CsvEditor.Views;

/// <summary>Panel xếp ngang, hết chỗ thì xuống hàng (WinUI không có sẵn): nhiều thẻ lọc ở cửa sổ hẹp không bị cắt mất.</summary>
public sealed class WrapPanel : Panel
{
    public double Spacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        var (_, width, height) = WrapLayout.Compute(Sizes(VisibleChildren()), availableSize.Width, Spacing);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = VisibleChildren();
        var (slots, _, _) = WrapLayout.Compute(Sizes(children), finalSize.Width, Spacing);
        for (var i = 0; i < children.Count; i++)
        {
            var slot = slots[i];
            children[i].Arrange(new Rect(slot.X, slot.Y, Math.Min(children[i].DesiredSize.Width, finalSize.Width), slot.RowHeight));
        }

        return finalSize;
    }

    private List<UIElement> VisibleChildren() => Children.Where(child => child.Visibility == Visibility.Visible).ToList();

    private static List<(double Width, double Height)> Sizes(List<UIElement> children) =>
        children.Select(child => ((double)child.DesiredSize.Width, (double)child.DesiredSize.Height)).ToList();
}
