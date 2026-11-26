using DbTools.Core;

namespace DbTools.Tests;

/// <summary>Nhật ký server: lọc Từ / Đến giờ, chép tên bảng của câu INSERT / UPDATE / DELETE (2026-11-26).</summary>
public class LogTimeAndTablesTests
{
    private static readonly DateTime Ref = new(2026, 11, 26, 14, 9, 48);

    private static LogTimeBound? B(string text)
    {
        Assert.True(LogTimeBound.TryParse(text, Ref, out var b), text);
        return b;
    }

    [Theory]
    [InlineData("14:02", "2026-11-26 14:02:00", "2026-11-26 14:03:00")]
    [InlineData(" 9:05 ", "2026-11-26 09:05:00", "2026-11-26 09:06:00")]
    [InlineData("14:02:30", "2026-11-26 14:02:30", "2026-11-26 14:02:31")]
    [InlineData("11-25 23:59", "2026-11-25 23:59:00", "2026-11-26 00:00:00")]
    [InlineData("2026-11-24 08:00:05", "2026-11-24 08:00:05", "2026-11-24 08:00:06")]
    [InlineData("2026/11/24  8:00", "2026-11-24 08:00:00", "2026-11-24 08:01:00")]
    [InlineData("2026-11-24", "2026-11-24 00:00:00", "2026-11-25 00:00:00")]
    [InlineData("11-3", "2026-11-03 00:00:00", "2026-11-04 00:00:00")]
    public void Parses_time_bounds(string text, string start, string end)
    {
        var b = B(text)!;
        Assert.Equal(DateTime.Parse(start), b.Start);
        Assert.Equal(DateTime.Parse(end), b.End);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("14h02")]
    [InlineData("hôm qua")]
    [InlineData("2026-13-01 10:00")]
    [InlineData("1 2 3")]
    public void Rejects_bad_times(string text) => Assert.False(LogTimeBound.TryParse(text, Ref, out _));

    [Fact]
    public void Empty_means_no_bound() => Assert.Null(B("  "));

    [Fact]
    public void Entry_time_reads_seconds_and_milliseconds()
    {
        Assert.Equal(new DateTime(2026, 11, 26, 14, 2, 29), LogTimeBound.ParseEntryTime("2026-11-26 14:02:29 +07"));
        Assert.Equal(new DateTime(2026, 11, 26, 14, 2, 29, 123), LogTimeBound.ParseEntryTime("2026-11-26 14:02:29.123 +07"));
        Assert.Null(LogTimeBound.ParseEntryTime(""));
    }

    [Fact]
    public void Filter_from_includes_the_mark_and_to_includes_the_whole_minute()
    {
        var entries = new[] { "2026-11-26 14:01:59.999 +07", "2026-11-26 14:02:00 +07", "2026-11-26 14:02:59.500 +07", "2026-11-26 14:03:00 +07", "" }
            .Select(t => new LogEntry { Time = t, Kind = LogKind.Insert }).ToList();
        var all = new HashSet<LogKind> { LogKind.Insert };
        var f = new LogFilter(all, From: B("14:02"), To: B("14:02"));
        Assert.Equal([false, true, true, false, false], entries.Select(f.Matches));
        var onlyFrom = new LogFilter(all, From: B("14:02:59"));
        Assert.Equal([false, false, true, true, false], entries.Select(onlyFrom.Matches));
        Assert.Equal([true, true, true, true, true], entries.Select(new LogFilter(all).Matches)); // không lọc giờ: cả dòng không có giờ
    }

    [Theory]
    [InlineData("-- FC_INSERT_MCF_DB_SESSION_MAP\n  INSERT INTO FCT_DB_SESSION_MAP (a, b) VALUES ($1, $2)", "INSERT", "FCT_DB_SESSION_MAP")]
    [InlineData("insert into public.fct_context(pid, c) select 1, 'INSERT INTO x'", "INSERT", "public.fct_context")]
    [InlineData("UPDATE ONLY \"Mã \"\"A\"\"\" SET n = 1", "UPDATE", "Mã \"A\"")]
    [InlineData("DELETE FROM s.\"T\" WHERE id IN (SELECT id FROM u)", "DELETE", "s.T")]
    [InlineData("MERGE INTO dich d USING nguon n ON d.id = n.id WHEN MATCHED THEN UPDATE SET v = n.v", "MERGE", "dich")]
    [InlineData("WITH x AS (DELETE FROM cu RETURNING *) INSERT INTO lich_su SELECT * FROM x", "INSERT", "lich_su")]
    [InlineData("/* UPDATE gia */ WITH RECURSIVE r(n) AS (SELECT 1) UPDATE t SET n = (SELECT max(n) FROM r)", "UPDATE", "t")]
    public void Table_of_write_statements(string sql, string verb, string table)
    {
        var (v, tables) = LogTables.Of(sql);
        Assert.Equal(verb, v);
        Assert.Equal([table], tables);
    }

    [Fact]
    public void Truncate_lists_every_table_and_reads_are_ignored()
    {
        Assert.Equal(["a", "s.b"], LogTables.Of("TRUNCATE TABLE ONLY a, s.b RESTART IDENTITY").Tables);
        Assert.Empty(LogTables.Of("SELECT * FROM t FOR UPDATE").Tables);
        Assert.Empty(LogTables.Of("WITH x AS (SELECT 1) SELECT * FROM x").Tables);
        Assert.Empty(LogTables.Of("SAVEPOINT JDBC_SAVEPOINT_30").Tables);
        Assert.Empty(LogTables.Of("DO $$ BEGIN INSERT INTO t VALUES (1); END $$").Tables);
    }

    [Fact]
    public void Copies_in_order_and_distinct()
    {
        LogEntry E(string time, LogKind kind, string sql) => new() { Time = "2026-11-26 " + time + " +07", Kind = kind, Sql = sql };
        var entries = new[]
        {
            E("14:02:33", LogKind.Insert, "INSERT INTO FCT_DB_SESSION_MAP VALUES (1)"),
            E("14:02:33", LogKind.Select, "SELECT 1"),
            E("14:02:34", LogKind.Update, "update fct_db_session_map set a = 1"),
            E("14:02:35", LogKind.Delete, "TRUNCATE x, FCT_DB_SESSION_MAP"),
            E("14:02:36", LogKind.Insert, "INSERT INTO y VALUES (1)"),
        };
        var uses = LogTables.InOrder(entries);
        Assert.Equal("14:02:33\tINSERT\tFCT_DB_SESSION_MAP\r\n14:02:34\tUPDATE\tfct_db_session_map\r\n14:02:35\tTRUNCATE\tx\r\n"
            + "14:02:35\tTRUNCATE\tFCT_DB_SESSION_MAP\r\n14:02:36\tINSERT\ty\r\n", LogTables.InOrderText(uses));
        Assert.Equal("FCT_DB_SESSION_MAP\tINSERT, UPDATE, TRUNCATE\t3\r\nx\tTRUNCATE\t1\r\ny\tINSERT\t1\r\n", LogTables.DistinctText(uses));
    }
}
