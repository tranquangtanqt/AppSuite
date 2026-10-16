using ScreenCapture.Models;

namespace ScreenCapture.Tests;

public class TabTitleTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 14, 0, 0);

    [Theory]
    [InlineData("2026-10-16 10 01 46", "10:01:46")]          // chụp hôm nay: chỉ giờ
    [InlineData("2026-10-16 10 01 46 (2)", "10:01:46 (2)")]  // 2 lần chụp cùng giây: giữ hậu tố
    [InlineData("2026-10-15 16 22 25", "15/10 16:22")]       // ngày khác cùng năm
    [InlineData("2025-12-31 23 59 59", "31/12/25 23:59")]    // năm khác
    public void Capture_titles_are_shortened(string title, string expected) =>
        Assert.Equal(expected, TabTitle.Short(title, Now));

    [Theory]
    [InlineData("bao_cao_thang")]          // mở từ file
    [InlineData("Ảnh mới (5)")]            // ảnh mới
    [InlineData("2026-10-16 10-01-46")]    // tên file tự lưu, không phải tên tab chụp
    [InlineData("2026-13-40 10 01 46")]    // không phải ngày hợp lệ
    public void Other_titles_are_kept(string title) =>
        Assert.Equal(title, TabTitle.Short(title, Now));

    [Fact]
    public void Yesterday_becomes_date_after_midnight()
    {
        const string title = "2026-10-16 23 50 00";
        Assert.Equal("23:50:00", TabTitle.Short(title, Now));
        Assert.Equal("16/10 23:50", TabTitle.Short(title, Now.AddDays(1)));
    }
}
