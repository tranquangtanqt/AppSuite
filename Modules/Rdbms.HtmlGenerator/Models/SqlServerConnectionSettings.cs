namespace Rdbms.HtmlGenerator.Models;

/// <summary>
/// SQL Server connection info entered in the settings dialog's "SQL Server" tab, persisted alongside the
/// other sources in Data\Config\config.xml. Plain mutable properties - required by XmlSerializer.
/// </summary>
public sealed class SqlServerConnectionSettings
{
    /// <summary>Server name or IP. "server\INSTANCE" connects to a named instance via SQL Browser and
    /// ignores <see cref="Port"/>.</summary>
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1433;
    public string Database { get; set; } = string.Empty;

    /// <summary>True = Windows Authentication (current Windows user), Username/Password ignored.</summary>
    public bool UseWindowsAuthentication { get; set; }

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional schema filter (vd dbo). Blank = every user schema.</summary>
    public string Schema { get; set; } = string.Empty;
}
