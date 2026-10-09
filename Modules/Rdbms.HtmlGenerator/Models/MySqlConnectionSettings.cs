namespace Rdbms.HtmlGenerator.Models;

/// <summary>
/// MySQL / MariaDB connection info entered in the settings dialog's "MySQL" tab, persisted alongside the
/// other sources in Data\Config\config.xml. No Schema field: in MySQL a schema IS the database, so
/// <see cref="Database"/> is also the import filter. Plain mutable properties - required by XmlSerializer.
/// </summary>
public sealed class MySqlConnectionSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
