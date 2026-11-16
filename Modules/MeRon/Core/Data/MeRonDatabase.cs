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
        foreach (var sql in EditTriggerSql())
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
    public string? MakeSafetyCopy(string reason) => CopyBeforeActions ? MakeSafetyCopy(reason, KeepSafetyCopies, DateTime.Now) : null;

    /// <summary>Cài đặt "Tự sao lưu khi thao tác" (mặc định bật): tắt thì Nhập / Xoá toàn bộ / Dọn / khôi phục lần dọn không chép
    /// bản sao (<see cref="MakeSafetyCopy(string)"/> trả null). Khôi phục 1 bản sao lưu (<see cref="RestoreCopy"/>) vẫn luôn chép.</summary>
    public bool CopyBeforeActions { get; set; } = true;

    /// <summary>Loại bản sao tự chép mỗi ngày khi mở app (Backups\me_ron_hang-ngay_yyyyMMdd_HHmmss.db).</summary>
    public const string DailyCopyReason = "hang-ngay";

    /// <summary>Số bản sao hằng ngày giữ lại mặc định (khoảng 2 tuần mở app) - đổi ở Cài đặt (MenuSettings.DailyBackupKeep).</summary>
    public const int KeepDailyCopies = 14;

    /// <summary>
    /// Sao lưu hằng ngày (gọi khi mở app): hôm nay chưa có bản <see cref="DailyCopyReason"/> nào thì chép 1 bản, giữ
    /// <paramref name="keep"/> bản gần nhất. Đã có bản hôm nay / database trống → null.
    /// </summary>
    public string? MakeDailyCopy(DateTime now, int keep = KeepDailyCopies)
    {
        if (Directory.Exists(BackupsFolder)
            && Directory.EnumerateFiles(BackupsFolder, $"me_ron_{DailyCopyReason}_{now:yyyyMMdd}_*.db").Any())
        {
            PruneCopies(DailyCopyReason, keep); // vừa giảm số bản ở Cài đặt
            return null;
        }
        return MakeSafetyCopy(DailyCopyReason, keep, now);
    }

    private string? MakeSafetyCopy(string reason, int keep, DateTime now)
    {
        if (Schema.BackupTables.Sum(t => Count(t.Name)) == 0)
        {
            return null;
        }
        Directory.CreateDirectory(BackupsFolder);
        var target = System.IO.Path.Combine(BackupsFolder, $"me_ron_{reason}_{now:yyyyMMdd_HHmmss}.db");
        using (var source = Open())
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }
        PruneCopies(reason, keep);
        return target;
    }

    /// <summary>Loại bản sao chép trước khi Khôi phục 1 bản sao lưu (để lấy lại được dữ liệu ngay trước đó).</summary>
    public const string BeforeRestoreReason = "truoc-lay-lai";

    /// <summary>Các bản sao trong Backups (mọi loại), mới nhất trước. File tên lạ bỏ qua.</summary>
    public IReadOnlyList<BackupCopy> ListCopies()
    {
        if (!Directory.Exists(BackupsFolder))
        {
            return [];
        }
        var copies = new List<BackupCopy>();
        foreach (var file in new DirectoryInfo(BackupsFolder).GetFiles("me_ron_*.db"))
        {
            if (BackupCopy.TryParse(file.Name) is { } parsed)
            {
                copies.Add(parsed with { Path = file.FullName, Size = file.Length });
            }
        }
        return [.. copies.OrderByDescending(c => c.Time).ThenBy(c => c.Reason, StringComparer.Ordinal)];
    }
    /// <summary>Đếm đơn / khách trong 1 bản sao (để chọn đúng bản); file hỏng / không phải database MeRon → null.</summary>
    public static (long Orders, long Customers)? CountIn(string copyPath)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = copyPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT (SELECT COUNT(*) FROM Orders), (SELECT COUNT(*) FROM Users)";
            using var reader = command.ExecuteReader();
            return reader.Read() ? (reader.GetInt64(0), reader.GetInt64(1)) : null;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    /// <summary>
    /// Khôi phục: thay TOÀN BỘ database hiện tại bằng 1 bản sao (SQLite Online Backup - cả 8 bảng + thông tin nhập / xuất lúc đó).
    /// Trước khi thay luôn chép dữ liệu hiện tại ra bản <see cref="BeforeRestoreReason"/> (lấy lại được). Bản sao không đọc được
    /// như database MeRon → lỗi, không động tới dữ liệu. Trả đường dẫn bản chép trước khi thay.
    /// </summary>
    public string? RestoreCopy(string copyPath, DateTime now)
    {
        if (CountIn(copyPath) is null)
        {
            throw new InvalidDataException("Bản sao không đọc được (hỏng hoặc không phải dữ liệu MeRon) - dữ liệu hiện tại giữ nguyên.");
        }
        var before = MakeSafetyCopy(BeforeRestoreReason, KeepSafetyCopies, now);
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = copyPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
               }.ToString()))
        using (var destination = Open())
        {
            source.Open();
            source.BackupDatabase(destination);
        }
        EnsureCreated(); // bản sao cũ (trước khi thêm bảng / trigger mới) → bổ sung cho đủ
        return before;
    }

    /// <summary>Chỉ giữ <paramref name="keep"/> bản gần nhất của 1 loại (xoá bản cũ hơn).</summary>
    public void PruneCopies(string reason, int keep)
    {
        if (!Directory.Exists(BackupsFolder))
        {
            return;
        }
        foreach (var old in new DirectoryInfo(BackupsFolder).GetFiles($"me_ron_{reason}_*.db")
                     .OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(keep))
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
        ResetUnsyncedEdits(connection); // đã xoá sạch (có bản sao) → không còn gì để mất khi nhập
        transaction.Commit();
        return copy;
    }

    // ---- Thay đổi trên máy tính chưa xuất (cảnh báo trước khi nhập đè) ----

    /// <summary>
    /// Mỗi bảng nghiệp vụ có trigger sau INSERT / UPDATE / DELETE: tăng bộ đếm <see cref="MetaKeys.UnsyncedEdits"/> (số dòng đã
    /// thêm / sửa / xoá), ghi lần sửa đầu (<see cref="MetaKeys.UnsyncedSince"/>) và cuối (<see cref="MetaKeys.LastEditAt"/>).
    /// Bắt mọi đường ghi (form, Thanh toán toàn bộ, xoá...) mà không phải sửa từng chỗ. Nhập backup tự đặt
    /// <see cref="MetaKeys.EditTrackingOff"/> trong transaction để không đếm các dòng vừa nạp.
    /// </summary>
    internal static IEnumerable<string> EditTriggerSql()
    {
        const string now = "strftime('%Y-%m-%dT%H:%M:%S', 'now', 'localtime')";
        foreach (var table in Schema.BackupTables)
        {
            foreach (var op in new[] { "INSERT", "UPDATE", "DELETE" })
            {
                yield return $"""
                    CREATE TRIGGER IF NOT EXISTS meron_edit_{table.Name}_{op.ToLowerInvariant()} AFTER {op} ON {table.Name}
                    WHEN (SELECT value FROM MeRonMeta WHERE key = '{MetaKeys.EditTrackingOff}') IS NULL
                    BEGIN
                        INSERT INTO MeRonMeta(key, value) VALUES ('{MetaKeys.UnsyncedEdits}', '1')
                            ON CONFLICT(key) DO UPDATE SET value = CAST(value AS INTEGER) + 1;
                        INSERT INTO MeRonMeta(key, value) VALUES ('{MetaKeys.UnsyncedSince}', {now}) ON CONFLICT(key) DO NOTHING;
                        INSERT INTO MeRonMeta(key, value) VALUES ('{MetaKeys.LastEditAt}', {now})
                            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                    END;
                    """;
            }
        }
    }

    /// <summary>Số dòng đã thêm / sửa / xoá trên máy này kể từ lần nhập / xuất / xoá toàn bộ gần nhất (0 = không có gì để mất).</summary>
    public UnsyncedEdits GetUnsyncedEdits()
    {
        var count = long.TryParse(GetMeta(MetaKeys.UnsyncedEdits), out var c) ? c : 0;
        return new UnsyncedEdits(count, ParseTime(GetMeta(MetaKeys.UnsyncedSince)), ParseTime(GetMeta(MetaKeys.LastEditAt)));
    }

    /// <summary>Gọi sau khi xuất bộ backup (TSV / Google Drive): mọi thay đổi đã nằm trong bản xuất.</summary>
    public void ResetUnsyncedEdits()
    {
        using var connection = Open();
        ResetUnsyncedEdits(connection);
    }

    internal static void ResetUnsyncedEdits(SqliteConnection connection) =>
        Execute(connection, $"DELETE FROM MeRonMeta WHERE key IN ('{MetaKeys.UnsyncedEdits}', '{MetaKeys.UnsyncedSince}', '{MetaKeys.LastEditAt}');");

    private static DateTime? ParseTime(string? value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time) ? time : null;

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

        /// <summary>Link / id folder Google Drive để tải bộ backup lên (trống = folder riêng "MeRon backup").</summary>
        public const string DriveUploadFolder = "driveUploadFolder";

        /// <summary>Số dòng thêm / sửa / xoá trên máy tính chưa xuất (trigger tăng; nhập / xuất / xoá toàn bộ thì xoá).</summary>
        public const string UnsyncedEdits = "unsyncedEdits";
        public const string UnsyncedSince = "unsyncedSince";
        public const string LastEditAt = "lastEditAt";

        /// <summary>Có mục này (chỉ trong transaction nhập backup) → trigger không đếm.</summary>
        public const string EditTrackingOff = "editTrackingOff";
    }
}

/// <summary>Thay đổi trên máy tính chưa xuất: số dòng, lần sửa đầu / cuối.</summary>
public sealed record UnsyncedEdits(long Count, DateTime? Since, DateTime? Last)
{
    public bool Any => Count > 0;

    /// <summary>Câu cảnh báo dùng chung (trang Nhập, nhắc xuất ở trang Đặt hàng); không có thay đổi → rỗng.</summary>
    public string Describe() => !Any
        ? string.Empty
        : $"Trên máy tính có {VnFormat.Number(Count)} dòng dữ liệu đã thêm / sửa / xoá chưa xuất" +
          (Since is { } since ? $" (từ {since:dd/MM/yyyy HH:mm}" + (Last is { } last && last - since >= TimeSpan.FromMinutes(1) ? $", lần cuối {last:dd/MM/yyyy HH:mm})." : ").") : ".");

    /// <summary>Câu ngắn cho dòng nhắc 1 hàng ở trang Đặt hàng (câu đầy đủ = Describe, để ở tooltip); không có thay đổi → rỗng.</summary>
    public string ShortText() => !Any
        ? string.Empty
        : $"{VnFormat.Number(Count)} dòng thay đổi chưa xuất" + (Since is { } since ? $" (từ {since:dd/MM HH:mm})" : string.Empty) +
          " · nhập backup sẽ đè mất";
}

/// <summary>1 bản sao trong Backups: me_ron_<see cref="Reason"/>_yyyyMMdd_HHmmss.db.</summary>
public sealed record BackupCopy(string Reason, DateTime Time, string Path = "", long Size = 0)
{
    /// <summary>Tên file đúng mẫu → bản sao (chưa có Path / Size); không đúng → null.</summary>
    public static BackupCopy? TryParse(string fileName)
    {
        const string prefix = "me_ron_";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            || fileName.Length < prefix.Length + 1 + 16 + 3)
        {
            return null;
        }
        var stamp = fileName[^18..^3]; // yyyyMMdd_HHmmss
        var reason = fileName[prefix.Length..^19];
        return reason.Length > 0 && fileName[^19] == '_'
               && DateTime.TryParseExact(stamp, "yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out var time)
            ? new BackupCopy(reason, time)
            : null;
    }

    /// <summary>Tên loại cho người dùng.</summary>
    public string Label => Reason switch
    {
        MeRonDatabase.DailyCopyReason => "Hằng ngày",
        "truoc-import" => "Trước khi Nhập dữ liệu",
        "truoc-xoa" => "Trước khi Xoá toàn bộ",
        "truoc-don" => "Trước khi Dọn dữ liệu cũ",
        "truoc-khoi-phuc" => "Trước khi khôi phục lần dọn",
        MeRonDatabase.BeforeRestoreReason => "Trước khi lấy lại bản sao",
        _ => Reason,
    };
}
