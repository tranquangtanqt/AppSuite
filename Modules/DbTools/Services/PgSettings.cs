using DbTools.Core;
using DbTools.Models;
using Npgsql;

namespace DbTools.Services;

/// <summary>Toàn bộ cấu hình server: các thông số + giá trị đặt riêng theo database / user.</summary>
public sealed record ServerConfig(IReadOnlyList<PgSetting> Settings, IReadOnlyList<SettingOverride> Overrides, bool Superuser, string Version);

/// <summary>
/// Đọc / đổi cấu hình server PostgreSQL (trang Cấu hình server). Đọc: pg_settings + current_setting() + pg_db_role_setting.
/// Đổi (GHI lên server, người dùng đã xác nhận): ALTER SYSTEM SET / RESET (mỗi câu tự commit - không chạy được trong giao dịch)
/// rồi pg_reload_conf(). sourcefile / sourceline chỉ superuser hoặc pg_read_all_settings thấy.
/// </summary>
public static class PgSettings
{
    public static async Task<ServerConfig> LoadAsync(ConnectionProfile p, CancellationToken ct)
    {
        await using var c = await PgAdmin.OpenMaintenanceAsync(p, ct);
        return await LoadAsync(c, ct);
    }

    private static async Task<ServerConfig> LoadAsync(NpgsqlConnection c, CancellationToken ct)
    {
        const string sql = """
            SELECT name, coalesce(current_setting(name, true), setting, ''), coalesce(setting, ''), coalesce(unit, ''),
                   coalesce(category, ''), coalesce(short_desc, ''), coalesce(extra_desc, ''), context, vartype, coalesce(source, ''),
                   coalesce(sourcefile, ''), sourceline, coalesce(boot_val, ''), coalesce(min_val, ''), coalesce(max_val, ''),
                   coalesce(enumvals, '{}'::text[]), coalesce(pending_restart, false)
              FROM pg_settings ORDER BY name
            """;
        var settings = new List<PgSetting>();
        await using (var cmd = new NpgsqlCommand(sql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                settings.Add(new PgSetting(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                    r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10), r.IsDBNull(11) ? null : r.GetInt32(11),
                    r.GetString(12), r.GetString(13), r.GetString(14), r.GetFieldValue<string[]>(15), r.GetBoolean(16)));
            }
        }
        // pending_restart không đáng tin ở kết nối mới trên server Windows (backend không fork từ postmaster, không mang cờ này):
        // đọc thêm pg_file_settings (đọc lại file ngay lúc hỏi) - dòng cuối của 1 thông số có applied = false, không lỗi cú pháp = đã
        // ghi giá trị mới nhưng chưa áp được (cần khởi động lại). Chỉ superuser / pg_read_all_settings đọc được.
        const string fileSql = """
            SELECT DISTINCT ON (name) name, applied, coalesce(error, '') IN ('', 'setting could not be applied'), coalesce(sourcefile, '') LIKE '%postgresql.auto.conf'
              FROM pg_file_settings ORDER BY name, seqno DESC
            """;
        try
        {
            var notApplied = new HashSet<string>(StringComparer.Ordinal);
            var autoConf = new HashSet<string>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand(fileSql, c))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    if (!r.GetBoolean(1) && r.GetBoolean(2))
                    {
                        notApplied.Add(r.GetString(0));
                    }
                    if (r.GetBoolean(3))
                    {
                        autoConf.Add(r.GetString(0));
                    }
                }
            }
            // Đọc được file thì tin file (cờ pending_restart cũ cũng có thể còn treo sau khi đã trả giá trị về như cũ).
            for (var i = 0; i < settings.Count; i++)
            {
                var pending = settings[i].NeedsRestart && notApplied.Contains(settings[i].Name);
                settings[i] = settings[i] with { PendingRestart = pending, InAutoConf = autoConf.Contains(settings[i].Name) };
            }
        }
        catch (PostgresException)
        {
            // Không có quyền: chỉ dựa vào pending_restart.
        }
        const string overridesSql = """
            SELECT coalesce(d.datname, ''), coalesce(ro.rolname, ''), cfg
              FROM pg_db_role_setting s
              LEFT JOIN pg_database d ON d.oid = s.setdatabase
              LEFT JOIN pg_roles ro ON ro.oid = s.setrole
              CROSS JOIN LATERAL unnest(s.setconfig) cfg
            """;
        var overrides = new List<SettingOverride>();
        try
        {
            await using var cmd = new NpgsqlCommand(overridesSql, c);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var cfg = r.GetString(2);
                var eq = cfg.IndexOf('=');
                if (eq > 0)
                {
                    overrides.Add(new SettingOverride(cfg[..eq], cfg[(eq + 1)..], r.GetString(0), r.GetString(1)));
                }
            }
        }
        catch (PostgresException)
        {
            // Không có quyền đọc pg_db_role_setting: bỏ qua phần đặt riêng.
        }
        await using var info = new NpgsqlCommand("SELECT rolsuper, current_setting('server_version') FROM pg_roles WHERE rolname = current_user", c);
        await using var ir = await info.ExecuteReaderAsync(ct);
        await ir.ReadAsync(ct);
        return new ServerConfig(settings, overrides, ir.GetBoolean(0), ir.GetString(1));
    }

    /// <summary>GHI: chạy các câu ALTER SYSTEM rồi pg_reload_conf(); đọc lại bằng kết nối mới (server nạp cấu hình bất đồng bộ).
    /// Câu nào lỗi thì dừng ở đó (câu trước đã ghi) - trả số câu đã chạy kèm lỗi.</summary>
    public static async Task<(ServerConfig After, int Done, Exception? Error)> ApplyAsync(ConnectionProfile p, IReadOnlyList<string> statements,
        CancellationToken ct)
    {
        var done = 0;
        Exception? error = null;
        await using (var c = await PgAdmin.OpenMaintenanceAsync(p, ct))
        {
            try
            {
                foreach (var sql in statements)
                {
                    await using var cmd = new NpgsqlCommand(sql, c);
                    await cmd.ExecuteNonQueryAsync(ct);
                    done++;
                }
            }
            catch (PostgresException ex)
            {
                error = ex;
            }
            if (done > 0)
            {
                await using var reload = new NpgsqlCommand("SELECT pg_reload_conf()", c);
                await reload.ExecuteScalarAsync(ct);
            }
        }
        await Task.Delay(done > 0 ? 800 : 0, ct);
        await using var after = await PgAdmin.OpenMaintenanceAsync(p, ct);
        return (await LoadAsync(after, ct), done, error);
    }
}
