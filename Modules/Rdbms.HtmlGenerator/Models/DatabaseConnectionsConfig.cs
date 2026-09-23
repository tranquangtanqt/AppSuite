namespace Rdbms.HtmlGenerator.Models;

/// <summary>Root of Data\Config\config.xml - holds both the PostgreSQL and Oracle connection forms
/// (only one is normally filled in, but both persist independently so switching source in the
/// settings dialog doesn't lose the other tab's saved values).</summary>
public sealed class DatabaseConnectionsConfig
{
    public PostgresConnectionSettings Postgres { get; set; } = new();
    public OracleConnectionSettings Oracle { get; set; } = new();
}
