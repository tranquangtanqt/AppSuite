using System.Text;
using DbTools.Core;
using DbTools.Models;
using Npgsql;

namespace DbTools.Services;

/// <summary>1 file trong thư mục log của server (pg_ls_logdir).</summary>
public sealed record ServerLogFile(string Name, long Size, DateTime Modified, bool IsCurrent);

/// <summary>Đoạn log đã đọc: nội dung (UTF-8), đọc từ byte nào, file dài bao nhiêu.</summary>
public sealed record ServerLogChunk(string Text, long Offset, long FileSize);

/// <summary>
/// Theo dõi (trang Nhật ký server): giữ 1 kết nối mở, mỗi ~2 giây hỏi file server đang ghi + dung lượng file đang đọc,
/// đọc phần mới ghi thêm. Không mở kết nối mới mỗi lần (log_connections bật thì mỗi lần mở cũng thành 1 dòng log).
/// </summary>
public sealed class ServerLogFollower : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;

    private ServerLogFollower(NpgsqlConnection connection) => _connection = connection;

    public static async Task<ServerLogFollower> OpenAsync(ConnectionProfile p, CancellationToken ct) =>
        new(await PgAdmin.OpenMaintenanceAsync(p, ct));

    /// <summary>Tên file server đang ghi (log dạng stderr) + dung lượng file <paramref name="fileName"/> (null = không còn).</summary>
    public async Task<(string Current, long? Size)> StatAsync(string fileName, CancellationToken ct)
    {
        const string sql = """
            SELECT regexp_replace(coalesce(pg_current_logfile('stderr'), ''), '^.*[/\\]', ''),
                   (pg_stat_file(current_setting('log_directory') || '/' || $1, true)).size
            """;
        await using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.Add(new NpgsqlParameter { Value = fileName });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return (r.GetString(0), r.IsDBNull(1) ? null : r.GetInt64(1));
    }

    /// <summary>Đọc <paramref name="length"/> byte từ <paramref name="offset"/> (giải mã UTF-8 ở máy này).</summary>
    public async Task<string> ReadAsync(string fileName, long offset, long length, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_read_binary_file(current_setting('log_directory') || '/' || $1, $2, $3)", _connection) { CommandTimeout = 120 };
        cmd.Parameters.Add(new NpgsqlParameter { Value = fileName });
        cmd.Parameters.Add(new NpgsqlParameter { Value = offset });
        cmd.Parameters.Add(new NpgsqlParameter { Value = length });
        return Encoding.UTF8.GetString((byte[])(await cmd.ExecuteScalarAsync(ct))!);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

/// <summary>
/// Đọc nhật ký server PostgreSQL qua SQL (không cần quyền vào máy server): cấu hình log (pg_settings), danh sách file
/// (pg_ls_logdir - superuser / pg_monitor), nội dung (pg_read_binary_file - superuser / pg_read_server_files; đọc dạng byte
/// rồi giải mã UTF-8 ở máy này nên file có byte lạ vẫn đọc được). Chỉ đọc, trừ <see cref="ApplyAsync"/> (nút Đổi cấu hình log:
/// ALTER SYSTEM + pg_reload_conf, người dùng đã xác nhận).
/// </summary>
public static class PgServerLog
{
    public static async Task<LogServerInfo> InfoAsync(ConnectionProfile p, CancellationToken ct)
    {
        await using var c = await PgAdmin.OpenMaintenanceAsync(p, ct);
        var settings = new Dictionary<string, string>();
        await using (var cmd = new NpgsqlCommand("SELECT name, setting FROM pg_settings WHERE name = ANY($1)", c))
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = LogConfig.Names });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                settings[r.GetString(0)] = r.IsDBNull(1) ? string.Empty : r.GetString(1);
            }
        }
        const string roles = """
            SELECT rolsuper, pg_has_role(current_user, 'pg_monitor', 'member'), pg_has_role(current_user, 'pg_read_server_files', 'member')
              FROM pg_roles WHERE rolname = current_user
            """;
        await using var rc = new NpgsqlCommand(roles, c);
        await using var rr = await rc.ExecuteReaderAsync(ct);
        await rr.ReadAsync(ct);
        return new LogServerInfo(settings, rr.GetBoolean(0), rr.GetBoolean(1), rr.GetBoolean(2));
    }

    /// <summary>4 thông số log đổi được (LogConfigEdit.Names) kèm nguồn / file đặt nó. sourcefile chỉ superuser /
    /// pg_read_all_settings thấy (không thấy thì Về mặc định không biết dòng nào do ALTER SYSTEM đặt).</summary>
    public static async Task<Dictionary<string, LogSetting>> EditableSettingsAsync(ConnectionProfile p, CancellationToken ct)
    {
        await using var c = await PgAdmin.OpenMaintenanceAsync(p, ct);
        return await ReadEditableAsync(c, ct);
    }

    private static async Task<Dictionary<string, LogSetting>> ReadEditableAsync(NpgsqlConnection c, CancellationToken ct)
    {
        const string sql = "SELECT name, setting, source, coalesce(sourcefile, ''), coalesce(boot_val, '') FROM pg_settings WHERE name = ANY($1)";
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = LogConfigEdit.Names });
        var result = new Dictionary<string, LogSetting>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result[r.GetString(0)] = new LogSetting(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4));
        }
        return result;
    }

    /// <summary>GHI lên server: chạy các câu ALTER SYSTEM (mỗi câu tự commit - ALTER SYSTEM không chạy trong giao dịch) rồi
    /// pg_reload_conf(); đọc lại bằng kết nối mới (server áp cấu hình mới bất đồng bộ) để báo giá trị thật đang áp.</summary>
    public static async Task<Dictionary<string, LogSetting>> ApplyAsync(ConnectionProfile p, IReadOnlyList<string> statements, CancellationToken ct)
    {
        await using (var c = await PgAdmin.OpenMaintenanceAsync(p, ct))
        {
            foreach (var sql in statements)
            {
                await using var cmd = new NpgsqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await using var reload = new NpgsqlCommand("SELECT pg_reload_conf()", c);
            await reload.ExecuteScalarAsync(ct);
        }
        Dictionary<string, LogSetting> after = [];
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(300, ct);
            await using var c = await PgAdmin.OpenMaintenanceAsync(p, ct);
            after = await ReadEditableAsync(c, ct);
            if (statements.All(s => Applied(s, after)))
            {
                break;
            }
        }
        return after;
    }

    /// <summary>Câu SET đã thấy ở pg_settings chưa (RESET thì không biết trước giá trị - coi như đã áp).</summary>
    private static bool Applied(string statement, Dictionary<string, LogSetting> settings)
    {
        var m = System.Text.RegularExpressions.Regex.Match(statement, @"^ALTER SYSTEM SET (\w+) = '?([^']*)'?$");
        return !m.Success || (settings.TryGetValue(m.Groups[1].Value, out var s) && s.Setting == m.Groups[2].Value);
    }

    /// <summary>File log, mới sửa nhất trước; đánh dấu file server đang ghi (pg_current_logfile).</summary>
    public static async Task<List<ServerLogFile>> ListFilesAsync(ConnectionProfile p, CancellationToken ct)
    {
        const string sql = """
            SELECT name, size, modification, name = regexp_replace(coalesce(pg_current_logfile(), ''), '^.*[/\\]', '')
              FROM pg_ls_logdir() ORDER BY modification DESC, name
            """;
        await using var c = await PgAdmin.OpenMaintenanceAsync(p, ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        var result = new List<ServerLogFile>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new ServerLogFile(r.GetString(0), r.GetInt64(1), r.GetDateTime(2).ToLocalTime(), r.GetBoolean(3)));
        }
        return result;
    }

    /// <summary>Đọc <paramref name="maxBytes"/> byte cuối của file (null = cả file). Đường dẫn = log_directory + tên file
    /// (log_directory tương đối thì pg_read_binary_file tự tính từ thư mục dữ liệu).</summary>
    public static async Task<ServerLogChunk> ReadAsync(ConnectionProfile p, string fileName, long? maxBytes, CancellationToken ct)
    {
        const string sql = """
            WITH f AS (SELECT current_setting('log_directory') || '/' || $1 AS path)
            SELECT (pg_stat_file(f.path)).size, f.path FROM f
            """;
        await using var c = await PgAdmin.OpenMaintenanceAsync(p, ct);
        long size;
        string path;
        await using (var cmd = new NpgsqlCommand(sql, c))
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = fileName });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            size = r.GetInt64(0);
            path = r.GetString(1);
        }
        var offset = maxBytes is { } max && size > max ? size - max : 0;
        await using var read = new NpgsqlCommand("SELECT pg_read_binary_file($1, $2, $3)", c) { CommandTimeout = 300 };
        read.Parameters.Add(new NpgsqlParameter { Value = path });
        read.Parameters.Add(new NpgsqlParameter { Value = offset });
        read.Parameters.Add(new NpgsqlParameter { Value = size - offset });
        var bytes = (byte[])(await read.ExecuteScalarAsync(ct))!;
        return new ServerLogChunk(Encoding.UTF8.GetString(bytes), offset, size);
    }
}
