using System.Collections.Generic;

namespace Rdbms.HtmlGenerator.Models;

/// <summary>Root of Data\Config\config.xml - the named connection profiles (dev / test / prod...), the selected
/// profile, and the "Cài đặt" options. Elements missing from an older config.xml keep their defaults.
/// <see cref="Services.ConnectionProfiles.Normalize"/> turns a pre-profile config.xml (1 set of settings per source at
/// the root) into a single profile "Mặc định".</summary>
public sealed class DatabaseConnectionsConfig
{
    public List<ConnectionProfile> Profiles { get; set; } = [];
    public string SelectedProfile { get; set; } = string.Empty;
    public AppOptions Options { get; set; } = new();

    // ----- config.xml cũ (trước khi có hồ sơ, 2026-10-16): chỉ đọc để chuyển sang hồ sơ "Mặc định", ghi lại là null
    // (XmlSerializer bỏ qua phần tử null) -----
    public DatabaseSourceType SelectedSource { get; set; } = DatabaseSourceType.Postgres;
    public PostgresConnectionSettings? Postgres { get; set; }
    public OracleConnectionSettings? Oracle { get; set; }
    public MySqlConnectionSettings? MySql { get; set; }
    public SqlServerConnectionSettings? SqlServer { get; set; }
}
