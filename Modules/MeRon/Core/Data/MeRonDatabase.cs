using Microsoft.Data.Sqlite;

namespace MeRon.Core.Data;

/// <summary>
/// File SQLite của module. Mặc định %LOCALAPPDATA%\AppSuite\MeRon\me_ron.db (dữ liệu người dùng, không nằm cạnh exe để
/// cập nhật app không mất dữ liệu); biến môi trường MERON_DB_PATH đổi chỗ (test, sandbox).
/// Mỗi thao tác mở 1 kết nối ngắn - SQLite mở rất nhanh và tránh giữ khoá file khi import / sao lưu.
/// </summary>
public sealed class MeRonDatabase
{
    public MeRonDatabase(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public string Folder => System.IO.Path.GetDirectoryName(Path)!;

    public static string DefaultPath =>
        Environment.GetEnvironmentVariable("MERON_DB_PATH") is { Length: > 0 } custom
            ? custom
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AppSuite", "MeRon", "me_ron.db");

    /// <summary>Mở kết nối (Pooling=False: đóng là nhả file ngay, cần khi chép file .db để sao lưu).</summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>Tạo thư mục + các bảng nếu chưa có (giữ nguyên dữ liệu đang có).</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Folder);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var table in Schema.BackupTables)
        {
            Execute(connection, table.CreateSql);
        }
        Execute(connection, Schema.MetaTableSql);
        foreach (var sql in Schema.IndexSql)
        {
            Execute(connection, sql);
        }
        Execute(connection, $"PRAGMA user_version = {Schema.Version};");
        transaction.Commit();
    }

    public long Count(string table)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    public string? GetMeta(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM MeRonMeta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string? value)
    {
        using var connection = Open();
        SetMeta(connection, key, value);
    }

    internal static void SetMeta(SqliteConnection connection, string key, string? value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO MeRonMeta(key, value) VALUES($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    internal static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Thời điểm ghi createdAt / updatedAt - cùng dạng DateTime.now().toIso8601String() của điện thoại.</summary>
    public static string Now() => DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture);

    public static class MetaKeys
    {
        public const string LastImportSource = "lastImportSource";
        public const string LastImportBackupName = "lastImportBackupName";
        public const string LastImportAt = "lastImportAt";

        /// <summary>Thư mục người dùng chọn lần trước ở trang Nhập dữ liệu (có thể là thư mục cha chứa nhiều bản backup).</summary>
        public const string LastPickedFolder = "lastPickedFolder";
    }
}
