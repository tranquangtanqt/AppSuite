using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenCapture.Models;

namespace ScreenCapture.Services;

/// <summary>Đọc/ghi <see cref="AppSettings"/> ở Data\Config\settings.json cạnh exe - cùng quy ước
/// AppContext.BaseDirectory-relative với ConnectionSettingsStore của Rdbms.HtmlGenerator.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ConfigPath { get; } = Path.Combine(AppContext.BaseDirectory, "Data", "Config", "settings.json");

    /// <summary>Không bao giờ trả null - lần chạy đầu hoặc file hỏng thì dùng mặc định.</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath), JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // File hỏng → mặc định; lần Save sau ghi đè.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
