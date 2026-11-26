using DbTools.Core;
using DbTools.Models;
using Npgsql;

namespace DbTools.Services;

/// <summary>1 phiên đang kết nối tới server (pg_stat_activity).</summary>
public sealed record PgSession(int Pid, string Database, string User, string Application, string Client, string State,
    DateTime? BackendStart, DateTime? QueryStart, string Query, bool IsSelf)
{
    /// <summary>Câu hiện tại / transaction đã chạy bao lâu (giây, theo đồng hồ server). null = không có.</summary>
    public double? QuerySeconds { get; init; }
    public double? XactSeconds { get; init; }

    /// <summary>Đang chờ gì (pg_stat_activity.wait_event_type / wait_event), vd Lock / transactionid.</summary>
    public string WaitEventType { get; init; } = string.Empty;
    public string WaitEvent { get; init; } = string.Empty;

    /// <summary>Các PID đang chặn phiên này (pg_blocking_pids).</summary>
    public IReadOnlyList<int> BlockedBy { get; init; } = [];
}

/// <summary>1 khoá phiên đang chờ (pg_locks, granted = false).</summary>
public sealed record PgLockWait(string LockType, string Mode, string Relation, string Detail);

/// <summary>
/// Việc quản trị qua Npgsql: danh sách database, phiên kết nối / ngắt phiên, tạo database (trống hoặc copy bằng TEMPLATE).
/// Tạo / copy database chạy trên database bảo trì "postgres" - không được kết nối vào database nguồn khi copy bằng TEMPLATE.
/// </summary>
public static class PgAdmin
{
    /// <summary>Hồ sơ trỏ sang database khác trên cùng server (bỏ schema: không đặt search_path).</summary>
    public static ConnectionProfile On(ConnectionProfile p, string database)
    {
        var copy = p.Clone(p.Name);
        copy.Database = database;
        copy.Schema = string.Empty;
        return copy;
    }

    public static PgTarget Target(ConnectionProfile p, string? database = null) =>
        new(p.Host.Trim(), p.Port, p.Username.Trim(), (database ?? p.Database).Trim());

    /// <summary>Mở kết nối tới database bảo trì: "postgres", không có thì "template1".</summary>
    public static async Task<NpgsqlConnection> OpenMaintenanceAsync(ConnectionProfile p, CancellationToken ct)
    {
        try
        {
            return await Pg.OpenAsync(On(p, "postgres"), ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            return await Pg.OpenAsync(On(p, "template1"), ct);
        }
    }

    public static async Task<List<string>> ListDatabasesAsync(ConnectionProfile p, CancellationToken ct)
    {
        await using var c = await OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand("SELECT datname FROM pg_database WHERE NOT datistemplate AND datallowconn ORDER BY datname", c);
        var result = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(r.GetString(0));
        }
        return result;
    }

    public static async Task<bool> DatabaseExistsAsync(ConnectionProfile p, string database, CancellationToken ct)
    {
        await using var c = await OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = $1)", c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = database });
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    /// <summary>Bản chính của server (vd 17) - chọn / cảnh báo pg_dump.</summary>
    public static async Task<int> ServerMajorAsync(ConnectionProfile p, CancellationToken ct)
    {
        await using var c = await OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand(PgSql.ServerVersion, c);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) / 10000;
    }

    /// <summary>Phiên của client (bỏ tiến trình nền của server). <paramref name="database"/> null = mọi database.</summary>
    public static async Task<List<PgSession>> ListSessionsAsync(ConnectionProfile p, string? database, CancellationToken ct)
    {
        const string sql = """
            SELECT pid, coalesce(datname, ''), coalesce(usename, ''), coalesce(application_name, ''),
                   coalesce(host(client_addr), 'local'), coalesce(state, ''), backend_start, query_start,
                   left(coalesce(query, ''), 2000), pid = pg_backend_pid(),
                   CASE WHEN state = 'active' THEN extract(epoch FROM clock_timestamp() - query_start)::float8 END,
                   extract(epoch FROM clock_timestamp() - xact_start)::float8,
                   coalesce(wait_event_type, ''), coalesce(wait_event, ''), pg_blocking_pids(pid)
              FROM pg_stat_activity
             WHERE backend_type = 'client backend' AND ($1::text IS NULL OR datname = $1)
             ORDER BY datname, backend_start
            """;
        await using var c = await OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)database ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        var result = new List<PgSession>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new PgSession(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetDateTime(6).ToLocalTime(), r.IsDBNull(7) ? null : r.GetDateTime(7).ToLocalTime(),
                r.GetString(8), r.GetBoolean(9))
            {
                QuerySeconds = r.IsDBNull(10) ? null : r.GetDouble(10),
                XactSeconds = r.IsDBNull(11) ? null : r.GetDouble(11),
                WaitEventType = r.GetString(12),
                WaitEvent = r.GetString(13),
                BlockedBy = r.GetFieldValue<int[]>(14),
            });
        }
        return result;
    }

    /// <summary>Ngắt các phiên (pg_terminate_backend; trừ chính phiên này). Trả số phiên đã ngắt. Không đủ quyền (không phải
    /// superuser / pg_signal_backend, phiên của user khác) → PostgresException.</summary>
    public static async Task<int> TerminateAsync(ConnectionProfile p, IReadOnlyCollection<int> pids, CancellationToken ct)
    {
        if (pids.Count == 0)
        {
            return 0;
        }
        await using var c = await OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FILTER (WHERE pg_terminate_backend(pid)) FROM unnest($1::int[]) AS t(pid) WHERE pid <> pg_backend_pid()", c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = pids.ToArray() });
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>Huỷ câu đang chạy của các phiên (pg_cancel_backend; trừ chính phiên này): phiên vẫn kết nối, transaction đang
    /// mở bị rollback. Phiên không có câu đang chạy (rảnh, rảnh trong transaction) thì không có gì để huỷ. Trả số phiên đã gửi
    /// được tín hiệu.</summary>
    public static async Task<int> CancelAsync(ConnectionProfile p, IReadOnlyCollection<int> pids, CancellationToken ct)
    {
        if (pids.Count == 0)
        {
            return 0;
        }
        await using var c = await OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FILTER (WHERE pg_cancel_backend(pid)) FROM unnest($1::int[]) AS t(pid) WHERE pid <> pg_backend_pid()", c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = pids.ToArray() });
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>Khoá phiên <paramref name="pid"/> đang chờ. Đọc trong chính database của phiên đó (tên bảng regclass chỉ đúng
    /// trong database chứa bảng).</summary>
    public static async Task<List<PgLockWait>> LockWaitsAsync(ConnectionProfile p, string database, int pid, CancellationToken ct)
    {
        const string sql = """
            SELECT locktype, mode,
                   CASE WHEN relation IS NOT NULL AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                        THEN relation::regclass::text ELSE '' END,
                   concat_ws(' ', CASE WHEN transactionid IS NOT NULL THEN 'transaction ' || transactionid END,
                                  CASE WHEN virtualxid IS NOT NULL THEN 'virtualxid ' || virtualxid END,
                                  CASE WHEN locktype = 'tuple' THEN 'dòng ' || page || '/' || tuple END)
              FROM pg_locks WHERE pid = $1 AND NOT granted
            """;
        await using var c = database.Length > 0 ? await Pg.OpenAsync(On(p, database), ct) : await OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = pid });
        var result = new List<PgLockWait>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new PgLockWait(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }
        return result;
    }

    /// <summary>Ngắt mọi phiên đang kết nối vào 1 database (trừ phiên này). Trả số phiên đã ngắt.</summary>
    public static async Task<int> TerminateDatabaseAsync(ConnectionProfile p, string database, CancellationToken ct)
    {
        var sessions = await ListSessionsAsync(p, database, ct);
        return await TerminateAsync(p, sessions.Where(s => !s.IsSelf).Select(s => s.Pid).ToList(), ct);
    }

    /// <summary>CREATE DATABASE trống từ template0 (sạch, hợp để restore dump). Owner = user của hồ sơ.</summary>
    public static async Task CreateDatabaseAsync(ConnectionProfile p, string database, CancellationToken ct)
    {
        await using var c = await OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand(PgSql.CreateDatabase(database), c);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Copy database cùng server: CREATE DATABASE mới TEMPLATE nguồn - chép nguyên khối file, rất nhanh; nguồn không
    /// được có phiên nào khác đang kết nối (lỗi 55006 object_in_use).</summary>
    public static async Task CopyDatabaseAsync(ConnectionProfile p, string source, string target, CancellationToken ct)
    {
        await using var c = await OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand(PgSql.CopyDatabase(source, target), c) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Hai hồ sơ cùng 1 server (host + port) → copy được bằng TEMPLATE.</summary>
    public static bool SameServer(ConnectionProfile a, ConnectionProfile b) =>
        string.Equals(a.Host.Trim(), b.Host.Trim(), StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;
}
