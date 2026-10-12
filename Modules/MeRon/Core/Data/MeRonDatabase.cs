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

    /// <summary>Số bản sao an toàn giữ lại cho mỗi loại (cũ hơn thì xoá).</summary>
    public const int KeepSafetyCopies = 10;

    public string BackupsFolder => System.IO.Path.Combine(Folder, "Backups");

    /// <summary>
    /// Chép database hiện tại ra Backups\me_ron_<paramref name="reason"/>_yyyyMMdd_HHmmss.db trước thao tác thay / xoá dữ liệu
    /// (SQLite Online Backup - an toàn kể cả khi đang mở). Bỏ qua (null) nếu chưa có dữ liệu gì. Giữ
    /// <see cref="KeepSafetyCopies"/> bản gần nhất của mỗi loại. Lấy lại = đóng app, chép bản sao đè lên me_ron.db.
    /// </summary>
    public string? MakeSafetyCopy(string reason)
    {
        if (Schema.BackupTables.Sum(t => Count(t.Name)) == 0)
        {
            return null;
        }
        Directory.CreateDirectory(BackupsFolder);
        var target = System.IO.Path.Combine(BackupsFolder, $"me_ron_{reason}_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        using (var source = Open())
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        foreach (var old in new DirectoryInfo(BackupsFolder).GetFiles($"me_ron_{reason}_*.db")
                     .OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(KeepSafetyCopies))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
                // Bản cũ đang bị mở ở đâu đó - để lần sau dọn.
            }
        }
        return target;
    }

    /// <summary>
    /// "Xoá toàn bộ dữ liệu ở máy" (như điện thoại): xoá sạch 8 bảng nghiệp vụ trong 1 transaction, đặt lại bộ đếm id và
    /// thông tin lần nhập. Luôn chép bản sao an toàn trước. Trả về đường dẫn bản sao (null nếu vốn đã trống).
    /// </summary>
    public string? WipeAll()
    {
        var copy = MakeSafetyCopy("truoc-xoa");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var table in Schema.BackupTables)
        {
            Execute(connection, $"DELETE FROM {table.Name};");
        }
        Execute(connection, $"DELETE FROM sqlite_sequence WHERE name IN ({string.Join(", ", Schema.BackupTables.Select(t => $"'{t.Name}'"))});");
        Execute(connection,
            $"DELETE FROM MeRonMeta WHERE key IN ('{MetaKeys.LastImportSource}', '{MetaKeys.LastImportBackupName}', '{MetaKeys.LastImportAt}');");
        transaction.Commit();
        return copy;
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

        /// <summary>Thư mục lần trước xuất TSV (trong đó mỗi lần xuất tạo 1 thư mục con yyyyMMdd_HHmmss).</summary>
        public const string LastExportFolder = "lastExportFolder";
    }
}
