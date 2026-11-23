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
/// Đọc nhật ký server PostgreSQL qua SQL (không cần quyền vào máy server): cấu hình log (pg_settings), danh sách file
/// (pg_ls_logdir - superuser / pg_monitor), nội dung (pg_read_binary_file - superuser / pg_read_server_files; đọc dạng byte
/// rồi giải mã UTF-8 ở máy này nên file có byte lạ vẫn đọc được). Chỉ đọc, không đổi gì trên server.
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
