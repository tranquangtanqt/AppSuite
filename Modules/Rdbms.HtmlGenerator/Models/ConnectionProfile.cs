namespace Rdbms.HtmlGenerator.Models;

/// <summary>
/// 1 hồ sơ kết nối có tên (vd dev / test / prod): nguồn đang chọn + thông tin kết nối của cả 4 nguồn (như hộp thoại
/// Thiết lập kết nối có 4 tab - đổi nguồn trong hồ sơ không mất giá trị của tab khác). Lưu trong Data\Config\config.xml.
/// Plain mutable properties - required by XmlSerializer.
/// </summary>
public sealed class ConnectionProfile
{
    public string Name { get; set; } = string.Empty;
    public DatabaseSourceType Source { get; set; } = DatabaseSourceType.Postgres;
    public PostgresConnectionSettings Postgres { get; set; } = new();
    public OracleConnectionSettings Oracle { get; set; } = new();
    public MySqlConnectionSettings MySql { get; set; } = new();
    public SqlServerConnectionSettings SqlServer { get; set; } = new();

    /// <summary>Tên database / service của nguồn đang chọn (rỗng nếu chưa nhập).</summary>
    public string DatabaseName => Source switch
    {
        DatabaseSourceType.Oracle => Oracle.ConnectBySid ? Oracle.Sid : Oracle.ServiceName,
        DatabaseSourceType.MySql => MySql.Database,
        DatabaseSourceType.SqlServer => SqlServer.Database,
        _ => Postgres.Database,
    };

    /// <summary>Bản sao độc lập (Thêm hồ sơ = sao chép hồ sơ đang chọn rồi sửa).</summary>
    public ConnectionProfile Clone(string name)
    {
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ConnectionProfile));
        using var writer = new System.IO.StringWriter();
        serializer.Serialize(writer, this);
        using var reader = new System.IO.StringReader(writer.ToString());
        var copy = (ConnectionProfile)serializer.Deserialize(reader)!;
        copy.Name = name;
        return copy;
    }
}
