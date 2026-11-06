using ScreenCapture.Models;

namespace ScreenCapture.Tests;

public class RibbonOverflowTests
{
    [Fact]
    public void Du_cho_thi_khong_can_nut_Them() =>
        Assert.Equal(0, RibbonOverflow.HiddenCount([100, 200, 300], 600, 50));

    [Fact]
    public void Thieu_cho_thi_chuyen_nhom_ben_phai_va_tinh_ca_nut_Them()
    {
        // Bỏ nhóm 300 còn 300 + 50 nút Thêm = 350 vừa đủ.
        Assert.Equal(1, RibbonOverflow.HiddenCount([100, 200, 300], 350, 50));
        // 349 thì phải bỏ thêm nhóm 200.
        Assert.Equal(2, RibbonOverflow.HiddenCount([100, 200, 300], 349, 50));
    }

    [Fact]
    public void Vua_khit_khi_co_them_nut_van_phai_chuyen_du() =>
        Assert.Equal(1, RibbonOverflow.HiddenCount([100, 100, 100], 299, 50));

    [Fact]
    public void Luon_giu_lai_it_nhat_1_nhom() =>
        Assert.Equal(2, RibbonOverflow.HiddenCount([100, 200, 300], 10, 50));

    [Fact]
    public void Khong_co_nhom_nao_thi_khong_loi() =>
        Assert.Equal(0, RibbonOverflow.HiddenCount([], 100, 50));
}
