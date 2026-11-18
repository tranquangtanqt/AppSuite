using System.Diagnostics;
using DbTools.Core;
using DbTools.Models;
using Npgsql;

namespace DbTools.Services;

/// <summary>Mở kết nối PostgreSQL. Npgsql chạy ở chế độ "raw" (không tự tách câu / đổi tham số): DbTools tự tách câu nên
/// biết chính xác câu nào, dòng nào lỗi; câu được gửi nguyên văn (kể cả tên có $ như msta_arap_data_$s).</summary>
public static class Pg
{
    static Pg()
    {
        AppContext.SetSwitch("Npgsql.EnableSqlRewriting", false);
    }

    public static string ConnectionString(ConnectionProfile p, int timeoutSeconds = 15) => new NpgsqlConnectionStringBuilder
    {
        Host = p.Host.Trim(),
        Port = p.Port,
        Database = p.Database.Trim(),
        Username = p.Username.Trim(),
        Password = p.Password,
        ApplicationName = "AppSuite DbTools",
        Timeout = timeoutSeconds,
        CommandTimeout = 0,
        IncludeErrorDetail = true,
        Pooling = false,
    }.ConnectionString;

    /// <summary>Câu đặt search_path theo schema đã chọn (null = để mặc định của server / user). Tên schema luôn trong "..."
    /// (giữ đúng hoa / thường như trong danh sách schema của DB).</summary>
    public static string? SearchPathSql(ConnectionProfile p) => p.SchemaOrNull is { } schema
        ? $"SET search_path TO {QuoteIdent(schema)}{(p.IncludePublic && schema != "public" ? ", public" : string.Empty)}"
        : null;

    public static string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    /// <summary>Mở kết nối (không dùng pool) và đặt search_path theo schema của hồ sơ - mọi việc sau đó (kiểm tra catalog, chạy
    /// script, script xoá) cùng nhìn 1 schema. Chỉ có tác dụng trên kết nối này, không đổi cấu hình DB / user.</summary>
    public static async Task<NpgsqlConnection> OpenAsync(ConnectionProfile p, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(ConnectionString(p));
        try
        {
            await connection.OpenAsync(ct);
            if (SearchPathSql(p) is { } setPath)
            {
                await using var cmd = new NpgsqlCommand(setPath, connection);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Thử kết nối: trả "PostgreSQL 17.2 · user@db" hoặc ném lỗi.</summary>
    public static async Task<string> TestAsync(ConnectionProfile p, CancellationToken ct)
    {
        await using var connection = await OpenAsync(p, ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT current_user, current_database(), current_setting('server_version'), coalesce(current_schema(), ''), current_setting('search_path')", connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var schema = reader.GetString(3);
        return $"PostgreSQL {reader.GetString(2)} · {reader.GetString(0)}@{reader.GetString(1)} · "
            + (schema.Length > 0 ? $"tạo đối tượng ở schema {schema}" : "chưa có schema nào trong search_path")
            + $" (search_path: {reader.GetString(4)})";
    }

    /// <summary>Lỗi kết nối / SQL thành 1 dòng dễ đọc.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        PostgresException pg => $"{pg.SqlState}: {pg.MessageText}{(string.IsNullOrEmpty(pg.Detail) ? string.Empty : " - " + pg.Detail)}{(string.IsNullOrEmpty(pg.Hint) ? string.Empty : " (Gợi ý: " + pg.Hint + ")")}",
        NpgsqlException { InnerException: TimeoutException } => "Hết thời gian chờ kết nối - kiểm tra host / port / tường lửa.",
        NpgsqlException { InnerException: System.Net.Sockets.SocketException se } => $"Không kết nối được tới server ({se.Message}).",
        _ => ex.Message,
    };
}

/// <summary>Đọc catalog để Kiểm tra trước khi chạy - chỉ SELECT, không thay đổi DB.</summary>
public static class PgCatalog
{
    /// <summary>Các schema người dùng (bỏ pg_*, information_schema) - cho ô chọn schema.</summary>
    public static async Task<List<string>> ListSchemasAsync(ConnectionProfile p, CancellationToken ct)
    {
        var bare = p.Clone(p.Name);
        bare.Schema = string.Empty; // schema đang chọn có thể chưa tồn tại - không đặt search_path khi chỉ đọc danh sách
        await using var connection = await Pg.OpenAsync(bare, ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg\\_%' AND nspname <> 'information_schema' ORDER BY nspname", connection);
        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    /// <summary>Schema đã chọn có trong DB chưa (chưa có thì không chạy - không tự CREATE SCHEMA để tránh gõ nhầm tên).</summary>
    public static async Task<bool> SchemaExistsAsync(ConnectionProfile p, CancellationToken ct)
    {
        if (p.SchemaOrNull is not { } schema)
        {
            return true;
        }
        var bare = p.Clone(p.Name);
        bare.Schema = string.Empty;
        await using var connection = await Pg.OpenAsync(bare, ct);
        await using var cmd = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = $1)", connection);
        cmd.Parameters.Add(new NpgsqlParameter { Value = schema });
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    /// <summary>Trong các tên cho trước, tên nào đã có trong DB (bảng / view / sequence / index, hàm, trigger, ràng buộc, kiểu, schema).
    /// Chỉ xét current_schema() - nơi CREATE không ghi schema sẽ tạo đối tượng (schema đã chọn, hoặc schema đầu tiên có thật
    /// trong search_path mặc định). Bảng cùng tên ở public khi đã chọn schema khác không làm CREATE lỗi nên không tính.</summary>
    public static async Task<List<ExistingObject>> FindExistingAsync(ConnectionProfile p, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        var result = new List<ExistingObject>();
        if (names.Count == 0)
        {
            return result;
        }
        const string sql = """
            SELECT 'rel', c.relname, c.relkind::text, true FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = current_schema() AND c.relname = ANY($1)
            UNION ALL
            SELECT 'func', p.proname, '', true FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE n.nspname = current_schema() AND p.proname = ANY($1)
            UNION ALL
            SELECT 'trigger', t.tgname, c.relname, n.nspname = current_schema() FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid
             JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE NOT t.tgisinternal AND n.nspname = ANY(current_schemas(false)) AND t.tgname = ANY($1)
            UNION ALL
            SELECT 'constraint', con.conname, c.relname, n.nspname = current_schema() FROM pg_constraint con JOIN pg_class c ON c.oid = con.conrelid
             JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = ANY(current_schemas(false)) AND con.conname = ANY($1)
            UNION ALL
            SELECT 'type', t.typname, '', true FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
             WHERE n.nspname = current_schema() AND t.typname = ANY($1) AND t.typtype IN ('c', 'e', 'd', 'r')
               AND NOT EXISTS (SELECT 1 FROM pg_class c WHERE c.reltype = t.oid)
            UNION ALL
            SELECT 'schema', nspname, '', true FROM pg_namespace WHERE nspname = ANY($1)
            """;
        await using var connection = await Pg.OpenAsync(p, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter { Value = names.ToArray() });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var kind = reader.GetString(0);
            if (kind == "rel")
            {
                kind = reader.GetString(2) switch
                {
                    "r" or "p" => "table",
                    "v" => "view",
                    "m" => "materialized view",
                    "S" => "sequence",
                    "i" or "I" => "index",
                    _ => "relation",
                };
                result.Add(new ExistingObject(kind, reader.GetString(1), string.Empty));
            }
            else
            {
                result.Add(new ExistingObject(kind, reader.GetString(1), reader.GetString(2), !reader.IsDBNull(3) && reader.GetBoolean(3)));
            }
        }
        return result;
    }

    /// <summary>Bảng nào đang có dữ liệu (đếm tối đa <paramref name="cap"/> dòng mỗi bảng cho nhanh).</summary>
    public static async Task<Dictionary<string, long>> CountRowsAsync(ConnectionProfile p, IEnumerable<QualifiedName> tables, long cap, CancellationToken ct)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var connection = await Pg.OpenAsync(p, ct);
        foreach (var table in tables)
        {
            await using var exists = new NpgsqlCommand("SELECT to_regclass($1) IS NOT NULL", connection);
            exists.Parameters.Add(new NpgsqlParameter { Value = table.Sql });
            if (await exists.ExecuteScalarAsync(ct) is not true)
            {
                continue;
            }
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM (SELECT 1 FROM {table.Sql} LIMIT {cap}) x", connection);
            result[table.Key] = Convert.ToInt64(await count.ExecuteScalarAsync(ct));
        }
        return result;
    }
}

/// <summary>Sự kiện trong lúc chạy, để trang cập nhật từng dòng file + nhật ký.</summary>
public abstract record RunEvent;
public sealed record RunLog(string Message) : RunEvent;
public sealed record FileStarted(int Index) : RunEvent;
public sealed record FileFinished(int Index, TimeSpan Elapsed, int Statements) : RunEvent;
public sealed record FileFailed(int Index, int Line, string Message) : RunEvent;

public sealed record RunOutcome(bool Success, bool Committed, int FilesDone, string Error, TimeSpan Elapsed);

/// <summary>
/// Chạy cả lô trong 1 transaction: BEGIN → (script xoá nếu có) → từng câu của từng file → COMMIT. Bất kỳ câu nào lỗi (hoặc
/// bấm Huỷ) → ROLLBACK toàn bộ: DB y như trước khi chạy. Chạy thử = chạy hết rồi luôn ROLLBACK. PostgreSQL rollback được cả
/// CREATE / DROP / ALTER nên cách này an toàn cho script tạo đối tượng.
/// </summary>
public static class PgRunner
{
    public static async Task<RunOutcome> RunAsync(ConnectionProfile profile, IReadOnlyList<SqlScriptFile> files, DropScript? dropFirst,
        bool dryRun, IProgress<RunEvent> progress, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        var done = 0;
        NpgsqlConnection? connection = null;
        NpgsqlTransaction? tx = null;
        try
        {
            progress.Report(new RunLog($"Kết nối {profile.Username}@{profile.Target}..."));
            connection = await Pg.OpenAsync(profile, ct);
            connection.Notice += (_, e) => progress.Report(new RunLog($"    NOTICE: {e.Notice.MessageText}"));
            progress.Report(new RunLog($"Đã kết nối (PostgreSQL {connection.ServerVersion}), {profile.SchemaText}"
                + (Pg.SearchPathSql(profile) is { } setPath ? $": {setPath}" : string.Empty) + ". BEGIN"));
            tx = await connection.BeginTransactionAsync(ct);

            if (dropFirst is { Statements.Count: > 0 })
            {
                progress.Report(new RunLog($"Xoá đối tượng cũ: {dropFirst.Statements.Count} câu DROP"));
                foreach (var drop in dropFirst.Statements)
                {
                    try
                    {
                        await ExecuteAsync(connection, tx, drop.Sql, ct);
                    }
                    catch (PostgresException ex)
                    {
                        var message = $"Lỗi khi xoá đối tượng cũ ({drop.Sql}): {Pg.Describe(ex)}";
                        progress.Report(new RunLog("  ✗ " + message));
                        await RollbackAsync(tx, progress);
                        return new RunOutcome(false, false, 0, message, total.Elapsed);
                    }
                }
            }

            for (var index = 0; index < files.Count; index++)
            {
                var file = files[index];
                progress.Report(new FileStarted(index));
                var watch = Stopwatch.StartNew();
                foreach (var info in file.Statements)
                {
                    var statement = info.Statement;
                    try
                    {
                        await ExecuteAsync(connection, tx, statement.Text, ct);
                    }
                    catch (PostgresException ex)
                    {
                        var line = LineOf(statement, ex.Position);
                        var message = Pg.Describe(ex);
                        progress.Report(new FileFailed(index, line, message));
                        progress.Report(new RunLog($"  ✗ {file.RelativePath} dòng {line}: {message}"));
                        await RollbackAsync(tx, progress);
                        return new RunOutcome(false, false, done, $"{file.RelativePath} dòng {line}: {message}", total.Elapsed);
                    }
                }
                done++;
                progress.Report(new FileFinished(index, watch.Elapsed, file.Statements.Count));
                progress.Report(new RunLog($"  ✓ {file.RelativePath} ({file.Statements.Count} câu, {watch.Elapsed.TotalSeconds:0.00} giây)"));
            }

            if (dryRun)
            {
                await RollbackAsync(tx, progress);
                progress.Report(new RunLog("Chạy thử xong: mọi câu chạy được. Đã ROLLBACK - DB không thay đổi."));
                return new RunOutcome(true, false, done, string.Empty, total.Elapsed);
            }
            await tx.CommitAsync(CancellationToken.None);
            progress.Report(new RunLog("COMMIT - đã ghi vào DB."));
            return new RunOutcome(true, true, done, string.Empty, total.Elapsed);
        }
        catch (OperationCanceledException)
        {
            if (tx is not null)
            {
                await RollbackAsync(tx, progress);
            }
            progress.Report(new RunLog("Đã huỷ - DB không thay đổi."));
            return new RunOutcome(false, false, done, "Đã huỷ", total.Elapsed);
        }
        catch (Exception ex)
        {
            var message = Pg.Describe(ex);
            if (tx is not null)
            {
                await RollbackAsync(tx, progress);
            }
            progress.Report(new RunLog("✗ " + message));
            return new RunOutcome(false, false, done, message, total.Elapsed);
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection, tx) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task RollbackAsync(NpgsqlTransaction tx, IProgress<RunEvent> progress)
    {
        try
        {
            await tx.RollbackAsync(CancellationToken.None);
            progress.Report(new RunLog("ROLLBACK - mọi thay đổi của lần chạy này đã được huỷ."));
        }
        catch (Exception ex)
        {
            // Kết nối đứt giữa chừng: server tự huỷ transaction chưa COMMIT.
            progress.Report(new RunLog($"ROLLBACK không gửi được ({ex.Message}) - transaction chưa COMMIT nên server tự huỷ."));
        }
    }

    /// <summary>Vị trí lỗi PostgreSQL trả về (ký tự thứ mấy trong câu, từ 1) → số dòng trong file.</summary>
    public static int LineOf(SqlStatement statement, int position)
    {
        if (position <= 0)
        {
            return statement.StartLine;
        }
        var end = Math.Min(position - 1, statement.Text.Length);
        var newlines = 0;
        for (var i = 0; i < end; i++)
        {
            if (statement.Text[i] == '\n')
            {
                newlines++;
            }
        }
        return statement.StartLine + newlines;
    }
}
