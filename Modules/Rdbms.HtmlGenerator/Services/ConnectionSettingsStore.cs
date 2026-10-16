using System;
using System.IO;
using System.Xml.Serialization;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Persists both the PostgreSQL and Oracle connection forms (Data\Config\config.xml, next to the
/// module executable - same AppContext.BaseDirectory-relative convention as RdbmsHtmlGeneratorDatabase's
/// Data\Database\Rdbms.HtmlGenerator.db) so the settings dialog's two tabs can each pre-fill on the next launch.
/// </summary>
public sealed class ConnectionSettingsStore
{
    private static readonly XmlSerializer Serializer = new(typeof(DatabaseConnectionsConfig));

    public string ConfigPath { get; }

    /// <param name="configPath">null = Data\Config\config.xml cạnh exe; unit test truyền file tạm.</param>
    public ConnectionSettingsStore(string? configPath = null)
    {
        ConfigPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "Data", "Config", "config.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
    }

    /// <summary>Never returns null - individual Host/etc. fields are simply empty when nothing has
    /// been saved yet (first run) or the file is unreadable. Always has at least 1 profile (a pre-profile
    /// config.xml becomes profile "Mặc định" - see <see cref="ConnectionProfiles.Normalize"/>).</summary>
    public DatabaseConnectionsConfig Load()
    {
        DatabaseConnectionsConfig config;
        try
        {
            using var stream = File.OpenRead(ConfigPath);
            config = (DatabaseConnectionsConfig?)Serializer.Deserialize(stream) ?? new DatabaseConnectionsConfig();
        }
        catch (Exception)
        {
            config = new DatabaseConnectionsConfig(); // chưa có file / file hỏng
        }
        ConnectionProfiles.Normalize(config);
        return config;
    }

    public void Save(DatabaseConnectionsConfig config)
    {
        // Ghi file tạm rồi thay: đang ghi mà lỗi (ổ đầy...) thì config.xml cũ - có mọi hồ sơ - vẫn còn nguyên.
        var tempPath = ConfigPath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            Serializer.Serialize(stream, config);
        }
        File.Move(tempPath, ConfigPath, overwrite: true);
    }
}
