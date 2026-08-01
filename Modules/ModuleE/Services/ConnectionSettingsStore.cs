using System;
using System.IO;
using System.Xml.Serialization;
using ModuleE.Models;

namespace ModuleE.Services;

/// <summary>
/// Persists both the PostgreSQL and Oracle connection forms (Data\Config\config.xml, next to the
/// module executable - same AppContext.BaseDirectory-relative convention as ModuleEDatabase's
/// Data\Database\ModuleE.db) so the settings dialog's two tabs can each pre-fill on the next launch.
/// </summary>
public sealed class ConnectionSettingsStore
{
    private static readonly XmlSerializer Serializer = new(typeof(DatabaseConnectionsConfig));

    public string ConfigPath { get; }

    public ConnectionSettingsStore()
    {
        var configDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Config");
        Directory.CreateDirectory(configDirectory);
        ConfigPath = Path.Combine(configDirectory, "config.xml");
    }

    /// <summary>Never returns null - individual Host/etc. fields are simply empty when nothing has
    /// been saved yet (first run) or the file is unreadable.</summary>
    public DatabaseConnectionsConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new DatabaseConnectionsConfig();
        }

        try
        {
            using var stream = File.OpenRead(ConfigPath);
            return (DatabaseConnectionsConfig?)Serializer.Deserialize(stream) ?? new DatabaseConnectionsConfig();
        }
        catch (Exception)
        {
            return new DatabaseConnectionsConfig();
        }
    }

    public void Save(DatabaseConnectionsConfig config)
    {
        using var stream = File.Create(ConfigPath);
        Serializer.Serialize(stream, config);
    }
}
