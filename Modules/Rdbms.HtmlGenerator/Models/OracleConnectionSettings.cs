namespace Rdbms.HtmlGenerator.Models;

/// <summary>
/// Oracle connection info entered in the settings dialog's "Oracle" tab, persisted alongside
/// <see cref="PostgresConnectionSettings"/> in Data\Config\config.xml. Plain mutable properties +
/// parameterless constructor - required by XmlSerializer.
/// </summary>
public sealed class OracleConnectionSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1521;

    /// <summary>True = connect using <see cref="Sid"/> (legacy connect descriptor), false (default) =
    /// connect using <see cref="ServiceName"/> (EZ Connect "host:port/serviceName").</summary>
    public bool ConnectBySid { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public string Sid { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional schema/owner filter. Blank = defaults to Username (Oracle's own convention -
    /// a user's default schema is that same user).</summary>
    public string Schema { get; set; } = string.Empty;
}
