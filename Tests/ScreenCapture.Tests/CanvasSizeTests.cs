using ScreenCapture.Models;

namespace ScreenCapture.Tests;

/// <summary>Kích thước SKXamlCanvas không làm ảnh nhoè ở màn hình phóng to (2026-11-17).</summary>
public class CanvasSizeTests
{
    [Theory]
    [InlineData(332, 1.5, 222)]   // 332 px ở 150%: 221,333 DIP bị cắt còn 331 px → lấy 333 px = 222 DIP
    [InlineData(432, 1.5, 288)]   // đã tròn: giữ nguyên
    [InlineData(1514, 1.5, 1010)] // màn hình Sandbox
    [InlineData(100, 1.25, 80)]
    [InlineData(101, 1.25, 84)]   // 105 px
    [InlineData(331.5, 1.0, 332)]
    [InlineData(300, 1.75, 172)]  // 301 px = 172 DIP
    public void So_DIP_nguyen_du_chua_noi_dung(double physical, double scale, double expected)
    {
        double dip = CanvasSize.ExactDip(physical, scale);

        Assert.Equal(expected, dip);
        Assert.True((int)(dip * (float)scale) >= Math.Ceiling(physical - 0.001), "bitmap phải đủ số pixel cần");
        Assert.Equal(dip * scale, (int)(dip * (float)scale)); // nhân lại tròn → bitmap khớp 1:1, không kéo giãn
    }
}
