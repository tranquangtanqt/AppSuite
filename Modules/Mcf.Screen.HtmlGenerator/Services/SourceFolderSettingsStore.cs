using System.Xml.Serialization;
using Mcf.Screen.HtmlGenerator.Models;

namespace Mcf.Screen.HtmlGenerator.Services;

/// <summary>Persists the chosen source folder (Data\Config\config.xml, next to the module executable)
/// so it auto-fills on the next launch - same pattern as Rdbms.HtmlGenerator's ConnectionSettingsStore.</summary>
public sealed class SourceFolderSettingsStore
{
    private static readonly XmlSerializer Serializer = new(typeof(SourceFolderSettings));

    public string ConfigPath { get; }

    public SourceFolderSettingsStore()
    {
        var configDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Config");
        Directory.CreateDirectory(configDirectory);
        ConfigPath = Path.Combine(configDirectory, "config.xml");
    }

    public SourceFolderSettings Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new SourceFolderSettings();
        }

        try
        {
            using var stream = File.OpenRead(ConfigPath);
            return (SourceFolderSettings?)Serializer.Deserialize(stream) ?? new SourceFolderSettings();
        }
        catch (Exception)
        {
            return new SourceFolderSettings();
        }
    }

    public void Save(SourceFolderSettings settings)
    {
        using var stream = File.Create(ConfigPath);
        Serializer.Serialize(stream, settings);
    }
}
