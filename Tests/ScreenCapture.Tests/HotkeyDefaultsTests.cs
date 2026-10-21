using ScreenCapture.Models;

namespace ScreenCapture.Tests;

public class HotkeyDefaultsTests
{
    [Fact]
    public void Moi_thao_tac_deu_co_phim_mac_dinh()
    {
        var defaults = AppSettings.DefaultHotkeys();

        Assert.All(Enum.GetValues<HotkeyAction>(), action => Assert.Single(defaults, h => h.Action == action));
    }

    [Fact]
    public void Phim_mac_dinh_khong_trung_nhau()
    {
        var enabled = AppSettings.DefaultHotkeys().Where(h => h.IsEnabled).Select(h => h.Describe()).ToList();

        Assert.Equal(enabled.Count, enabled.Distinct().Count());
    }

    [Fact]
    public void Chup_lay_chu_mac_dinh_Ctrl_Alt_T()
    {
        var binding = AppSettings.DefaultHotkeys().Single(h => h.Action == HotkeyAction.RegionText);

        Assert.Equal("Ctrl+Alt+T", binding.Describe());
        Assert.Equal((uint)'T', HotkeyBinding.VirtualKeyOf(binding.Key));
    }

    [Fact]
    public void Cai_dat_ban_cu_thieu_Chup_lay_chu_thi_bo_sung_mac_dinh()
    {
        // settings.json từ bản trước 2026-10-21: chưa có RegionText, người dùng đã đổi phím Vùng chọn.
        var settings = new AppSettings
        {
            Hotkeys = [new() { Action = HotkeyAction.Region, Ctrl = true, Key = "F9" }],
        };

        var binding = settings.GetHotkey(HotkeyAction.RegionText);

        Assert.Equal("Ctrl+Alt+T", binding.Describe());
        Assert.Contains(settings.Hotkeys, h => h.Action == HotkeyAction.RegionText);
        Assert.Equal("Ctrl+F9", settings.GetHotkey(HotkeyAction.Region).Describe()); // phím đã đổi giữ nguyên
    }
}
