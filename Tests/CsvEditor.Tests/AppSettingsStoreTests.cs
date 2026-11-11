using CsvEditor.Services;

namespace CsvEditor.Tests;

/// <summary>Cài đặt giao diện (Data\Config\settings.json): lưu / đọc lại, file thiếu / hỏng / giá trị lạ → Theo Windows.</summary>
public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Missing_file_means_follow_windows() =>
        Assert.Equal(AppTheme.System, new AppSettingsStore(_dir.File("settings.json")).LoadTheme());

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void Saved_theme_is_read_back(AppTheme theme)
    {
        var path = Path.Combine(_dir.Path, "Config", "settings.json"); // thư mục chưa có - tự tạo
        new AppSettingsStore(path).SaveTheme(theme);
        Assert.Equal(theme, new AppSettingsStore(path).LoadTheme());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "Theme": "Purple" }""")]
    [InlineData("""{ "Theme": "7" }""")]
    [InlineData("null")]
    public void Broken_or_unknown_value_falls_back_to_windows(string content)
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, content);
        Assert.Equal(AppTheme.System, new AppSettingsStore(path).LoadTheme());
    }
}
