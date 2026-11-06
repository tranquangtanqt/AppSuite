using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScreenCapture.Models;
using Windows.Foundation;

namespace ScreenCapture.Views;

/// <summary>Ribbon hẹp hơn các nhóm: nhóm bên phải chuyển vào nút Thêm, nên thứ tự nhóm trong XAML là thứ tự ưu tiên.
/// Chuyển đúng control thật nên trạng thái, phím tắt và code dùng x:Name không đổi.</summary>
public sealed partial class EditorWindow
{
    /// <summary>Vị trí nhóm Màu &amp; Cỡ nét ở tab Trang chủ: sau Chọn, Vẽ hình.</summary>
    private const int ColorSizeHomeIndex = 2;

    /// <summary>Nút Thêm lúc đang ẩn chưa đo được.</summary>
    private const double MoreButtonWidth = 50;

    /// <summary>Nhóm đang nằm trong nút Thêm, kèm chỗ cũ để trả về đúng thứ tự.</summary>
    private readonly List<(FrameworkElement Group, Panel Home, int Index)> _ribbonOverflow = [];
    private bool _ribbonMoreHooked;

    private void RibbonBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width)
        {
            UpdateRibbonOverflow();
        }
    }

    private Panel VisibleRibbonPanel =>
        new Panel[] { HomeRibbonPanel, FileRibbonPanel, FormatRibbonPanel, RegionRibbonPanel, NumberStampRibbonPanel }
            .First(panel => panel.Visibility == Visibility.Visible);

    private void RestoreRibbonGroups()
    {
        for (var i = _ribbonOverflow.Count - 1; i >= 0; i--)
        {
            var (group, home, index) = _ribbonOverflow[i];
            RibbonMoreHost.Children.Remove(group);
            home.Children.Insert(Math.Min(index, home.Children.Count), group);
        }
        _ribbonOverflow.Clear();
    }

    private void UpdateRibbonOverflow()
    {
        if (RibbonBorder.ActualWidth <= 0)
        {
            return;
        }
        if (!_ribbonMoreHooked)
        {
            _ribbonMoreHooked = true;
            RibbonMoreHost.AddHandler(UIElement.TappedEvent, new TappedEventHandler(RibbonMoreHost_Tapped), handledEventsToo: true);
        }
        RestoreRibbonGroups();

        var panel = VisibleRibbonPanel;
        panel.Measure(new Size(double.PositiveInfinity, RibbonBorder.ActualHeight));
        var groups = panel.Children.OfType<FrameworkElement>().Where(g => g.Visibility == Visibility.Visible).ToList();
        var hidden = RibbonOverflow.HiddenCount(groups.Select(g => g.DesiredSize.Width).ToList(), RibbonBorder.ActualWidth, MoreButtonWidth);

        // Từ phải sang trái để chỗ cũ (Index) vẫn đúng khi trả về theo thứ tự ngược lại.
        foreach (var group in Enumerable.Reverse(groups).Take(hidden))
        {
            var index = panel.Children.IndexOf(group);
            panel.Children.RemoveAt(index);
            RibbonMoreHost.Children.Insert(0, group);
            _ribbonOverflow.Add((group, panel, index));
        }
        RibbonMoreButton.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Chọn công cụ / bấm lệnh trong nút Thêm thì đóng lại để vẽ được ngay; nút mở bảng riêng (màu, Con dấu, Xoay,
    /// Hiệu ứng) thì giữ.</summary>
    private void RibbonMoreHost_Tapped(object sender, TappedRoutedEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, RibbonMoreHost); element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase button)
            {
                if (button is not Button { Flyout: not null })
                {
                    DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => RibbonMoreFlyout.Hide());
                }
                return;
            }
        }
    }
}
