using ScreenCapture.Models;

namespace ScreenCapture.Tests;

public class FileNameTemplateTests : IDisposable
{
    private static readonly CaptureInfo Excel = new(new DateTime(2026, 10, 7, 10, 31, 10), CaptureMode.Region, "EXCEL", "Báo cáo: tháng 10?.xlsx - Excel");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ScFileName_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Default_template_uses_date_time_app()
    {
        Assert.Equal("2026-10-07_10-31-10_EXCEL", FileNameTemplate.Resolve(null, Excel, 1920, 1080, null));
        Assert.Equal("2026-10-07_10-31-10_EXCEL", FileNameTemplate.Resolve("   ", Excel, 1920, 1080, null));
    }

    [Fact]
    public void Tokens_size_mode_and_case_insensitive()
    {
        Assert.Equal("Vung_800x600_EXCEL", FileNameTemplate.Resolve("{MODE}_{size}_{App}", Excel, 800, 600, null));
    }

    [Fact]
    public void Forbidden_characters_are_replaced_and_backslash_makes_subfolder()
    {
        var path = FileNameTemplate.Resolve(@"{date}\{window}", Excel, 1, 1, null);
        var parts = path.Split(Path.DirectorySeparatorChar);
        Assert.Equal("2026-10-07", parts[0]);
        Assert.DoesNotContain(':', parts[1]);
        Assert.DoesNotContain('?', parts[1]);
        Assert.StartsWith("Báo cáo", parts[1]);
    }

    [Fact]
    public void Image_extension_typed_in_template_is_dropped()
    {
        Assert.Equal("2026-10-07_EXCEL", FileNameTemplate.Resolve("{date}_{app}.png", Excel, 1, 1, null));
    }

    [Fact]
    public void Missing_app_falls_back_to_desktop()
    {
        var info = new CaptureInfo(Excel.Time, CaptureMode.FullScreen, string.Empty, string.Empty);
        Assert.Equal("Desktop_Desktop_ToanManHinh", FileNameTemplate.Resolve("{app}_{window}_{mode}", info, 1, 1, null));
    }

    [Fact]
    public void Number_token_continues_after_highest_existing_file()
    {
        Assert.Equal("shot_001", FileNameTemplate.Resolve("shot_{n}", Excel, 1, 1, _folder)); // thư mục chưa có
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "shot_002.png"), "");
        File.WriteAllText(Path.Combine(_folder, "shot_007 (2).png"), "");
        File.WriteAllText(Path.Combine(_folder, "other_099.png"), "");
        Assert.Equal("shot_008", FileNameTemplate.Resolve("shot_{n}", Excel, 1, 1, _folder));
    }
}
