using ScreenCapture.Models;

namespace ScreenCapture.Tests;

/// <summary>Ảnh ghim nổi trên màn hình (2026-11-10): zoom, độ mờ, chỗ đặt lúc mới ghim.</summary>
public class PinGeometryTests
{
    private static readonly PinRect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void Wheel_zooms_by_10_percent_and_stops_at_100_when_crossing_it()
    {
        Assert.Equal(1.1, PinGeometry.NextZoom(1, 120), 3);
        Assert.Equal(1 / 1.1, PinGeometry.NextZoom(1, -120), 3);
        Assert.Equal(1, PinGeometry.NextZoom(0.95, 120));   // 0.95 → 1.045 đi qua 100% → dừng ở 100%
        Assert.Equal(1, PinGeometry.NextZoom(1.05, -120));
        Assert.Equal(PinGeometry.MaxZoom, PinGeometry.NextZoom(3.9, 120));
        Assert.Equal(PinGeometry.MinZoom, PinGeometry.NextZoom(0.105, -120));
        Assert.Equal(0.5, PinGeometry.NextZoom(0.5, 0));
    }

    [Fact]
    public void Ctrl_wheel_changes_opacity_by_10_between_20_and_100()
    {
        Assert.Equal(90, PinGeometry.NextOpacity(100, -120));
        Assert.Equal(100, PinGeometry.NextOpacity(100, 120));
        Assert.Equal(PinGeometry.MinOpacity, PinGeometry.NextOpacity(20, -120));
        Assert.Equal(50, PinGeometry.NextOpacity(40, 360));
    }

    [Fact]
    public void Window_size_includes_the_border()
    {
        Assert.Equal((402, 302), PinGeometry.WindowSize(400, 300, 1));
        Assert.Equal((202, 152), PinGeometry.WindowSize(400, 300, 0.5));
        Assert.Equal((3, 3), PinGeometry.WindowSize(1, 1, 0.1)); // không nhỏ hơn 1 px ảnh
    }

    [Fact]
    public void Resize_keeps_the_point_under_the_cursor_still()
    {
        var window = new PinRect(100, 100, 200, 100);
        var bigger = PinGeometry.ResizeAround(window, 150, 125, 400, 200); // con trỏ ở 1/4 ngang, 1/4 dọc
        Assert.Equal(new PinRect(50, 75, 400, 200), bigger);
        Assert.Equal(new PinRect(100, 100, 200, 100), PinGeometry.ResizeAround(window, 100, 100, 200, 100));
    }

    [Fact]
    public void A_small_capture_is_pinned_exactly_over_the_captured_region()
    {
        var (window, zoom) = PinGeometry.Initial(400, 300, new PinRect(500, 200, 400, 300), WorkArea);
        Assert.Equal(1, zoom);
        Assert.Equal(new PinRect(499, 199, 402, 302), window); // viền 1 px nằm ngoài ảnh
    }

    [Fact]
    public void A_full_screen_capture_is_pinned_half_size_in_the_middle()
    {
        var (window, zoom) = PinGeometry.Initial(1920, 1080, new PinRect(0, 0, 1920, 1080), WorkArea);
        Assert.Equal(520.0 / 1080, zoom, 6); // vừa nửa vùng làm việc (chiều cao 1040 / 2 = 520 quyết định)
        Assert.Equal(924 + 2, window.Width);
        Assert.Equal(520 + 2, window.Height);
        Assert.Equal((1920 - window.Width) / 2, window.X);
        Assert.Equal((1040 - window.Height) / 2, window.Y);
    }

    [Fact]
    public void Unknown_position_is_centered_at_actual_size_when_it_fits()
    {
        var (window, zoom) = PinGeometry.Initial(600, 400, null, new PinRect(1920, 0, 1600, 860)); // màn hình thứ 2
        Assert.Equal(1, zoom);
        Assert.Equal(new PinRect(1920 + (1600 - 602) / 2, (860 - 402) / 2, 602, 402), window);
    }

    [Fact]
    public void Pin_after_capture_counts_as_an_after_capture_action()
    {
        var settings = new AppSettings { OpenEditorAfterCapture = false, PinAfterCapture = true };
        Assert.True(settings.HasAfterCaptureAction);
        Assert.False(new AppSettings().PinAfterCapture); // mặc định tắt
    }
}
