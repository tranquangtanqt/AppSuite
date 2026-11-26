using System.Diagnostics;
using System.Text;
using DbTools.Core;
using DbTools.Models;
using Npgsql;

namespace DbTools.Services;

/// <summary>Kết quả 1 lần Chạy SQL: từng câu, bảng của câu trả về dòng cuối cùng, đã COMMIT chưa, lỗi ở dòng nào.</summary>
public sealed record QueryOutcome(IReadOnlyList<QueryStatementResult> Statements, QueryTable? Table, bool Committed, long DurationMs,
    string? Error = null, int? ErrorLine = null)
{
    public bool Success => Error is null;
}

/// <summary>
/// Chạy các câu của trang "Chạy SQL" trên 1 kết nối, trong 1 giao dịch: chỉ đọc = BEGIN READ ONLY ... ROLLBACK (PostgreSQL từ
/// chối mọi câu ghi: INSERT / UPDATE / DELETE / DDL / nextval...); cho phép ghi = BEGIN ... COMMIT, câu nào lỗi là ROLLBACK hết.
/// Giới hạn thời gian bằng statement_timeout (SET LOCAL - chỉ trong giao dịch này). Câu SELECT đọc qua cursor để chỉ lấy đủ số
/// dòng giới hạn.
/// </summary>
public static class PgQuery
{
    private const string Cursor = "dbtools_q";

    public static async Task<QueryOutcome> RunAsync(ConnectionProfile p, IReadOnlyList<QueryStatement> statements, bool allowWrite,
        int maxRows, int timeoutSeconds, CancellationToken ct, Action<string>? notice = null)
    {
        var total = Stopwatch.StartNew();
        await using var c = await Pg.OpenAsync(p, ct);
        if (notice is not null)
        {
            c.Notice += (_, e) => notice($"{e.Notice.Severity}: {e.Notice.MessageText}");
        }
        await ExecAsync(c, allowWrite ? "BEGIN" : "BEGIN READ ONLY", ct);
        if (timeoutSeconds > 0)
        {
            await ExecAsync(c, $"SET LOCAL statement_timeout = {timeoutSeconds * 1000L}", ct);
        }
        var results = new List<QueryStatementResult>();
        QueryTable? table = null;
        foreach (var s in statements)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (s.Cursor)
                {
                    table = await ReadCursorAsync(c, s.Text, maxRows, ct);
                    results.Add(new(s.Line, "SELECT", table.Rows.Count, sw.ElapsedMilliseconds));
                }
                else
                {
                    var (kind, rows, t) = await ExecuteAsync(c, s.Text, maxRows, ct);
                    table = t ?? table;
                    results.Add(new(s.Line, kind, rows, sw.ElapsedMilliseconds));
                }
            }
            catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or InvalidOperationException)
            {
                var error = ex is OperationCanceledException ? "Đã huỷ" : Describe(ex, timeoutSeconds);
                results.Add(new(s.Line, KindOf(s.Text), null, sw.ElapsedMilliseconds, error));
                await RollbackAsync(c);
                return new(results, table, false, total.ElapsedMilliseconds, error, s.Line);
            }
        }
        if (allowWrite)
        {
            await ExecAsync(c, "COMMIT", ct);
        }
        else
        {
            await RollbackAsync(c);
        }
        return new(results, table, allowWrite, total.ElapsedMilliseconds);
    }

    /// <summary>Lỗi hết giờ (57014 do statement_timeout) ghi rõ giới hạn.</summary>
    private static string Describe(Exception ex, int timeoutSeconds) =>
        ex is PostgresException { SqlState: "57014" } pg && pg.MessageText.Contains("statement timeout", StringComparison.OrdinalIgnoreCase)
            ? $"Quá {timeoutSeconds} giây (giới hạn ở trang Cài đặt) - đã dừng câu này."
            : Pg.Describe(ex);

    private static async Task RollbackAsync(NpgsqlConnection c)
    {
        try
        {
            if (c.State == System.Data.ConnectionState.Open)
            {
                await ExecAsync(c, "ROLLBACK", CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Kết nối hỏng: đóng kết nối là server tự rollback.
        }
    }

    private static async Task ExecAsync(NpgsqlConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>SELECT qua cursor: FETCH giới hạn + 1 dòng để biết còn dòng không (0 = lấy hết).</summary>
    private static async Task<QueryTable> ReadCursorAsync(NpgsqlConnection c, string sql, int maxRows, CancellationToken ct)
    {
        await ExecAsync(c, QuickQuery.DeclareCursor(sql, Cursor), ct);
        // Không CLOSE trong finally: câu lỗi / bị huỷ thì giao dịch đã hỏng, CLOSE sẽ lỗi 25P02 và che mất lỗi thật
        // (ROLLBACK sau đó tự đóng cursor).
        List<string> columns, types;
        var rows = new List<string?[]>();
        await using (var cmd = new NpgsqlCommand(maxRows > 0 ? $"FETCH FORWARD {maxRows + 1} FROM {Cursor}" : $"FETCH ALL FROM {Cursor}", c))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            (columns, types) = Columns(reader);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(Row(reader));
            }
        }
        await ExecAsync(c, $"CLOSE {Cursor}", ct);
        var truncated = maxRows > 0 && rows.Count > maxRows;
        if (truncated)
        {
            rows.RemoveAt(rows.Count - 1);
        }
        return new QueryTable(columns, types, rows, truncated, sql, true);
    }

    /// <summary>Câu khác: chạy thẳng; có trả dòng (RETURNING, SHOW, EXPLAIN, WITH...) thì giữ tối đa giới hạn, vẫn đếm hết.</summary>
    private static async Task<(string Kind, long? Rows, QueryTable? Table)> ExecuteAsync(NpgsqlConnection c, string sql, int maxRows, CancellationToken ct)
    {
        await using var batch = new NpgsqlBatch(c) { BatchCommands = { new NpgsqlBatchCommand(sql) } };
        QueryTable? table = null;
        long count = 0;
        await using (var reader = await batch.ExecuteReaderAsync(ct))
        {
            if (reader.FieldCount > 0)
            {
                var (columns, types) = Columns(reader);
                var rows = new List<string?[]>();
                while (await reader.ReadAsync(ct))
                {
                    count++;
                    if (maxRows <= 0 || rows.Count < maxRows)
                    {
                        rows.Add(Row(reader));
                    }
                }
                table = new QueryTable(columns, types, rows, rows.Count < count, sql, false);
            }
        }
        var command = batch.BatchCommands[0];
        var kind = command.StatementType switch
        {
            StatementType.Select => "SELECT",
            StatementType.Insert => "INSERT",
            StatementType.Update => "UPDATE",
            StatementType.Delete => "DELETE",
            StatementType.Merge => "MERGE",
            StatementType.Copy => "COPY",
            _ => KindOf(sql),
        };
        long? affected = table is not null ? count
            : command.StatementType is StatementType.Insert or StatementType.Update or StatementType.Delete or StatementType.Merge
                or StatementType.Select or StatementType.Copy
                ? command.RecordsAffected
                : null;
        return (kind, affected, table);
    }

    /// <summary>Từ đầu câu viết hoa (CREATE, ALTER, DO...) - để ghi ở thẻ Thông báo.</summary>
    private static string KindOf(string sql)
    {
        var tokens = SqlLexer.Tokenize(sql);
        return tokens.Count > 0 && tokens[0].Kind == SqlTokenKind.Word ? tokens[0].Value.ToUpperInvariant() : "SQL";
    }

    private static (List<string> Names, List<string> Types) Columns(NpgsqlDataReader reader)
    {
        var names = new List<string>(reader.FieldCount);
        var types = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            names.Add(reader.GetName(i));
            types.Add(reader.GetDataTypeName(i));
        }
        return (names, types);
    }

    private static string?[] Row(NpgsqlDataReader reader)
    {
        var row = new string?[reader.FieldCount];
        for (var i = 0; i < row.Length; i++)
        {
            row[i] = Value(reader, i);
        }
        return row;
    }

    private static string? Value(NpgsqlDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
        {
            return null;
        }
        try
        {
            // date: Npgsql mặc định trả DateTime (0 giờ) → đọc DateOnly để không hiện "00:00:00".
            return reader.GetDataTypeName(i) == "date"
                ? QuickQuery.Format(reader.GetFieldValue<DateOnly>(i))
                : QuickQuery.Format(reader.GetValue(i));
        }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException or OverflowException or ArgumentException)
        {
            return $"(không đọc được giá trị kiểu {reader.GetDataTypeName(i)} - thêm ::text trong câu SELECT)";
        }
    }

    /// <summary>Xuất CSV (UTF-8 có BOM) đủ mọi dòng của bảng kết quả: còn dòng chưa đọc thì chạy lại câu đó trong giao dịch chỉ đọc
    /// (không bao giờ ghi DB) và ghi dần ra file; đã đọc hết thì ghi luôn từ bộ nhớ. Trả số dòng đã ghi.</summary>
    public static async Task<long> ExportCsvAsync(ConnectionProfile p, QueryTable table, string path, int timeoutSeconds, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        long count = 0;
        try
        {
            await using (var w = new StreamWriter(temp, false, new UTF8Encoding(true)))
            {
                await w.WriteAsync(CsvLine(table.Columns));
                if (!table.Truncated)
                {
                    foreach (var row in table.Rows)
                    {
                        await w.WriteAsync(CsvLine(row));
                        count++;
                    }
                }
                else
                {
                    await using var c = await Pg.OpenAsync(p, ct);
                    await ExecAsync(c, "BEGIN READ ONLY", ct);
                    if (timeoutSeconds > 0)
                    {
                        await ExecAsync(c, $"SET LOCAL statement_timeout = {timeoutSeconds * 1000L}", ct);
                    }
                    try
                    {
                        if (table.Cursor)
                        {
                            await ExecAsync(c, QuickQuery.DeclareCursor(table.Sql, Cursor), ct);
                        }
                        while (true)
                        {
                            await using var cmd = new NpgsqlCommand(table.Cursor ? $"FETCH FORWARD 5000 FROM {Cursor}" : table.Sql, c);
                            await using var reader = await cmd.ExecuteReaderAsync(ct);
                            var got = 0;
                            while (await reader.ReadAsync(ct))
                            {
                                await w.WriteAsync(CsvLine(Row(reader)));
                                got++;
                            }
                            count += got;
                            if (!table.Cursor || got == 0)
                            {
                                break;
                            }
                        }
                    }
                    finally
                    {
                        await RollbackAsync(c);
                    }
                }
            }
        }
        catch
        {
            File.Delete(temp); // huỷ / lỗi giữa chừng: không để lại file dở
            throw;
        }
        File.Move(temp, path, overwrite: true);
        return count;
    }

    private static string CsvLine(IEnumerable<string?> values) => string.Join(',', values.Select(v => MockOutput.CsvField(v, false))) + "\r\n";
}
