using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

/// <summary>
/// Trang vừa khít cửa sổ khi đủ chỗ, cuộn được khi không đủ. Đặt thẳng Grid trong ScrollViewer thì hàng "*" mất tác
/// dụng (chiều cao vô hạn → danh sách không ảo hoá, giãn hết); không có ScrollViewer thì cửa sổ nhỏ ép danh sách về 0 và
/// cắt mất phần nhật ký (thấy ở 150% DPI, cửa sổ ~1000×600). Nên: chiều cao Grid = max(vùng nhìn thấy, chiều cao tối thiểu).
/// </summary>
internal static class ScrollFit
{
    public static void Attach(ScrollViewer viewer, FrameworkElement content, double minHeight)
    {
        void Fit() => content.Height = Math.Max(minHeight, viewer.ViewportHeight);
        viewer.SizeChanged += (_, _) => Fit();
        viewer.Loaded += (_, _) => Fit();
    }
}
