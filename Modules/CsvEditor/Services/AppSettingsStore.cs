using System.Text.Json;

namespace CsvEditor.Services;

/// <summary>Giao diện: theo chế độ của Windows, luôn sáng, luôn tối.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Cài đặt của CsvEditor ở Data\Config\settings.json cạnh exe (cùng chỗ với recent-files.json). Hiện chỉ có giao
/// diện. File hỏng / giá trị lạ thì dùng mặc định (Theo Windows); ghi lỗi (thư mục chỉ đọc...) thì bỏ qua.</summary>
public sealed class AppSettingsStore
{
    private readonly string _configPath;

    public AppSettingsStore(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "Data", "Config", "settings.json");
    }

    public AppTheme LoadTheme()
    {
        try
        {
            if (File.Exists(_configPath)
                && JsonSerializer.Deserialize<Stored>(File.ReadAllText(_configPath)) is { Theme: { } text }
                && Enum.TryParse<AppTheme>(text, out var theme) && Enum.IsDefined(theme))
            {
                return theme;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return AppTheme.System;
    }

    public void SaveTheme(AppTheme theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(new Stored { Theme = theme.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Stored
    {
        public string? Theme { get; set; }
    }
}
