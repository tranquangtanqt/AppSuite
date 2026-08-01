namespace ModuleE.Models;

/// <summary>
/// PostgreSQL connection info entered in the settings dialog and persisted to Data\Config\config.xml.
/// Plain mutable properties + parameterless constructor - required by XmlSerializer, unlike the
/// required/init-only records used elsewhere in this module.
/// </summary>
public sealed class PostgresConnectionSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional schema filter. Blank = import every non-system schema.</summary>
    public string Schema { get; set; } = string.Empty;
}
