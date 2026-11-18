using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DbTools.Views;

/// <summary>
/// Trang vừa khít cửa sổ khi đủ chỗ, cuộn được khi không đủ. Đặt thẳng Grid trong ScrollViewer thì hàng "*" mất tác
/// dụng (chiều cao vô hạn → danh sách không ảo hoá, giãn hết); không có ScrollViewer thì cửa sổ nhỏ ép danh sách về 0 và
/// cắt mất phần nhật ký. Nên: chiều cao Grid = max(chiều cao ScrollViewer, chiều cao tối thiểu).
/// </summary>
internal static class ScrollFit
{
    public static void Attach(ScrollViewer viewer, FrameworkElement content, double minHeight)
    {
        viewer.SizeChanged += (_, e) => content.Height = Math.Max(minHeight, e.NewSize.Height);
        viewer.Loaded += (_, _) => content.Height = Math.Max(minHeight, viewer.ActualHeight);
    }
}
