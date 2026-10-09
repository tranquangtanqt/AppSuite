namespace Rdbms.HtmlGenerator.Models;

/// <summary>Root of Data\Config\config.xml - holds every source's connection form (each persists
/// independently so switching source doesn't lose the other tabs' saved values), the last selected
/// source, and the "Cai dat" options. Elements missing from an older config.xml keep their defaults.</summary>
public sealed class DatabaseConnectionsConfig
{
    public DatabaseSourceType SelectedSource { get; set; } = DatabaseSourceType.Postgres;
    public PostgresConnectionSettings Postgres { get; set; } = new();
    public OracleConnectionSettings Oracle { get; set; } = new();
    public MySqlConnectionSettings MySql { get; set; } = new();
    public SqlServerConnectionSettings SqlServer { get; set; } = new();
    public AppOptions Options { get; set; } = new();
}
