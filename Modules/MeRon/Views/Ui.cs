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

    // Thẻ đơn (2026-10-15): vạch màu bên trái + nhãn tròn theo trạng thái (Đã lên đơn cam, Đã thanh toán xanh lá,
    // Huỷ xám); nền thẻ: Đã thanh toán xanh lá nhạt (alpha 0x38, người dùng chọn), Đã lên đơn trắng, Huỷ xám nhạt.
    private static readonly Brush ShippingSolid = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE8, 0x59, 0x0C));
    private static readonly Brush PaidSolid = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x2F, 0x9E, 0x44));
    private static readonly Brush CancelledSolid = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x86, 0x8E, 0x96));
    private static readonly Brush ShippingSoft = new SolidColorBrush(ColorHelper.FromArgb(0x26, 0xE8, 0x59, 0x0C));
    private static readonly Brush PaidSoft = new SolidColorBrush(ColorHelper.FromArgb(0x26, 0x2F, 0x9E, 0x44));
    private static readonly Brush CancelledSoft = new SolidColorBrush(ColorHelper.FromArgb(0x26, 0x86, 0x8E, 0x96));
    private static readonly Brush PaidTint = new SolidColorBrush(ColorHelper.FromArgb(0x38, 0x2F, 0x9E, 0x44));
    private static readonly Brush CancelledTint = new SolidColorBrush(ColorHelper.FromArgb(0x38, 0x86, 0x8E, 0x96));
    private static readonly Brush NoTint = new SolidColorBrush(Colors.Transparent);

    /// <summary>Lớp màu phủ lên nền thẻ đơn: nhìn lướt là biết đơn nào đã thanh toán.</summary>
    public static Brush StatusTint(OrderStatus status) => status switch
    {
        OrderStatus.Completed => PaidTint,
        OrderStatus.Cancelled => CancelledTint,
        _ => NoTint,
    };

    public static Brush StatusSolid(OrderStatus status) => status switch
    {
        OrderStatus.Completed => PaidSolid,
        OrderStatus.Cancelled => CancelledSolid,
        _ => ShippingSolid,
    };

    public static Brush StatusSoft(OrderStatus status) => status switch
    {
        OrderStatus.Completed => PaidSoft,
        OrderStatus.Cancelled => CancelledSoft,
        _ => ShippingSoft,
    };

    public static double DimIfCancelled(OrderStatus status) => status == OrderStatus.Cancelled ? 0.6 : 1.0;

    /// <summary>Nền thẻ đơn theo trạng thái: Đã thanh toán xanh lá, Huỷ đỏ, Đã lên đơn nền thẻ thường.</summary>
    public static Brush StatusBackground(OrderStatus status) => status switch
    {
        OrderStatus.Completed => Paid,
        OrderStatus.Cancelled => Cancelled,
        _ => (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
    };
}
