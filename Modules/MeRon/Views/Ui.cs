using MeRon.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MeRon.Views;

/// <summary>Hàm nhỏ dùng trong x:Bind.</summary>
internal static class Ui
{
    // Bán trong suốt như điện thoại (green / red withOpacity 0.4) - hợp cả nền sáng lẫn tối.
    private static readonly Brush Paid = new SolidColorBrush(ColorHelper.FromArgb(0x55, 0x2E, 0xA0, 0x43));
    private static readonly Brush Cancelled = new SolidColorBrush(ColorHelper.FromArgb(0x55, 0xE5, 0x3E, 0x3E));

    public static bool HasText(string? value) => !string.IsNullOrEmpty(value);

    public static bool Not(bool value) => !value;

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfNot(bool value) => VisibleIf(!value);

    public static Visibility VisibleIfText(string? value) => VisibleIf(!string.IsNullOrEmpty(value));

    /// <summary>Tìm phần tử con theo x:Name (vd ô trong 1 dòng của ItemsControl) để đưa con trỏ tới.</summary>
    public static FrameworkElement? FindByName(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } element && n == name)
            {
                return element;
            }
            if (FindByName(child, name) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>Nền thẻ đơn theo trạng thái: Đã thanh toán xanh lá, Huỷ đỏ, Đã lên đơn nền thẻ thường.</summary>
    public static Brush StatusBackground(OrderStatus status) => status switch
    {
        OrderStatus.Completed => Paid,
        OrderStatus.Cancelled => Cancelled,
        _ => (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
    };
}
