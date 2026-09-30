using FileTools.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FileTools.Views;

/// <summary>Hàm nhỏ dùng trong x:Bind.</summary>
internal static class Ui
{
    private static readonly Brush Removed = new SolidColorBrush(ColorHelper.FromArgb(0x40, 0xE5, 0x3E, 0x3E));
    private static readonly Brush Added = new SolidColorBrush(ColorHelper.FromArgb(0x40, 0x2E, 0xA0, 0x43));
    private static readonly Brush Header = new SolidColorBrush(ColorHelper.FromArgb(0x30, 0x3A, 0x6E, 0xC8));
    private static readonly Brush None = new SolidColorBrush(Colors.Transparent);

    /// <summary>Làm mờ phần không dùng tới (vd danh sách cột khi đang "So cả dòng").</summary>
    public static double OpacityIf(bool active) => active ? 1.0 : 0.45;

    /// <summary>Nền dòng kết quả so sánh: đỏ = chỉ có ở A, xanh = chỉ có ở B (bán trong suốt - hợp cả nền sáng / tối).</summary>
    public static Brush DiffBackground(DiffLineKind kind, bool isHeader) =>
        isHeader ? Header : kind switch { DiffLineKind.Removed => Removed, DiffLineKind.Added => Added, _ => None };

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
