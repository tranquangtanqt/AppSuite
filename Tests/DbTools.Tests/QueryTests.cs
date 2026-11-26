using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>Trang Chạy SQL - phần không cần DB: chọn đoạn, tách câu, chặn lệnh giao dịch, định dạng ô, chép, lịch sử.</summary>
public class QuickQueryTests
{
    [Fact]
    public void Pick_uses_selection_and_counts_lines_before_it()
    {
        var all = "SELECT 1;\r\nSELECT 2;\rSELECT 3;";
        Assert.Equal((all, 0), QuickQuery.Pick(all, "", 0));
        Assert.Equal((all, 0), QuickQuery.Pick(all, "  \r", 3));
        var start = all.IndexOf("SELECT 3", StringComparison.Ordinal);
        Assert.Equal(("SELECT 3;", 2), QuickQuery.Pick(all, "SELECT 3;", start));

        var plan = QuickQuery.Plan("SELECT 3;", 2);
        Assert.Equal(3, Assert.Single(plan.Statements).Line);
    }

    [Fact]
    public void Plan_splits_like_psql_and_marks_cursor_statements()
    {
        var plan = QuickQuery.Plan("""
            \encoding UTF-8
            SELECT ';' AS a;
            -- chú thích; không tách
            WITH x AS (SELECT 1) SELECT * FROM x;
            INSERT INTO t VALUES (1) RETURNING id;
            DO $$ BEGIN RAISE NOTICE 'a;b'; END $$;
            values (1), (2);
            TABLE t;
            SELECT * INTO t2 FROM t;
            """);
        Assert.Empty(plan.Rejected);
        Assert.Single(plan.Skipped, s => s.Contains("\\encoding", StringComparison.Ordinal) && s.StartsWith("Dòng 1:", StringComparison.Ordinal));
        Assert.Equal([true, false, false, false, true, true, false], plan.Statements.Select(s => s.Cursor));
        Assert.Equal(2, plan.Statements[0].Line);
    }

    [Theory]
    [InlineData("BEGIN", "BEGIN / START TRANSACTION")]
    [InlineData("begin transaction isolation level serializable", "BEGIN / START TRANSACTION")]
    [InlineData("START TRANSACTION READ WRITE", "BEGIN / START TRANSACTION")]
    [InlineData("COMMIT", "COMMIT")]
    [InlineData("end", "COMMIT")]
    [InlineData("ROLLBACK", "ROLLBACK")]
    [InlineData("abort", "ROLLBACK")]
    [InlineData("SET TRANSACTION READ WRITE", "SET TRANSACTION")]
    [InlineData("SET SESSION CHARACTERISTICS AS TRANSACTION READ WRITE", "SET TRANSACTION")]
    [InlineData("PREPARE TRANSACTION 'x'", "PREPARE TRANSACTION")]
    [InlineData("/* x */ commit", "COMMIT")]
    [InlineData("ROLLBACK TO SAVEPOINT s1", null)]
    [InlineData("SAVEPOINT s1", null)]
    [InlineData("SET search_path TO a", null)]
    [InlineData("START", null)]
    [InlineData("DO $$ BEGIN COMMIT; END $$", null)]
    [InlineData("SELECT 'commit'", null)]
    public void Transaction_control_is_detected(string sql, string? expected) => Assert.Equal(expected, QuickQuery.TransactionControl(sql));

    [Fact]
    public void Plan_handles_textbox_carriage_returns()
    {
        // TextBox của WinUI giữ xuống dòng bằng \r: chú thích -- không được nuốt câu sau, dòng phải đúng.
        var plan = QuickQuery.Plan("SELECT 1; -- chú thích\rDELETE FROM t;\r\rSELECT 'a\rb';");
        Assert.Equal([1, 2, 4], plan.Statements.Select(s => s.Line));
        Assert.StartsWith("DELETE", plan.Statements[1].Text);
        Assert.Equal("SELECT 'a\nb'", plan.Statements[2].Text.TrimEnd());
    }

    [Fact]
    public void Plan_rejects_commit_with_its_line()
    {
        var plan = QuickQuery.Plan("SELECT 1;\nCOMMIT;\nINSERT INTO t VALUES (1);", 10);
        Assert.Equal(2, plan.Statements.Count);
        Assert.StartsWith("Dòng 12: COMMIT", Assert.Single(plan.Rejected));
    }

    [Fact]
    public void Format_matches_postgres_text()
    {
        Assert.Null(QuickQuery.Format(null));
        Assert.Null(QuickQuery.Format(DBNull.Value));
        Assert.Equal("true", QuickQuery.Format(true));
        Assert.Equal("12.5", QuickQuery.Format(12.5m));
        Assert.Equal("0.1", QuickQuery.Format(0.1));
        Assert.Equal("2026-11-26", QuickQuery.Format(new DateOnly(2026, 11, 26)));
        Assert.Equal("2026-11-26 00:00:00", QuickQuery.Format(new DateTime(2026, 11, 26)));
        Assert.Equal("2026-11-26 08:05:03.25", QuickQuery.Format(new DateTime(2026, 11, 26, 8, 5, 3, 250)));
        Assert.Equal("2026-11-26 08:05:03+09", QuickQuery.Format(new DateTimeOffset(2026, 11, 26, 8, 5, 3, TimeSpan.FromHours(9))));
        Assert.Equal("2026-11-26 08:05:03+05:30", QuickQuery.Format(new DateTimeOffset(2026, 11, 26, 8, 5, 3, new TimeSpan(5, 30, 0))));
        Assert.Equal("13:45:00", QuickQuery.Format(new TimeOnly(13, 45)));
        Assert.Equal("01:30:00", QuickQuery.Format(TimeSpan.FromMinutes(90)));
        Assert.Equal("2.01:00:00", QuickQuery.Format(TimeSpan.FromHours(49)));
        Assert.Equal("\\x00ff", QuickQuery.Format(new byte[] { 0, 255 }));
        Assert.Equal("{1,2,NULL}", QuickQuery.Format(new int?[] { 1, 2, null }));
        Assert.Equal("{a,\"b c\",\"x,y\",\"\"}", QuickQuery.Format(new[] { "a", "b c", "x,y", "" }));
        var utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.StartsWith(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), QuickQuery.Format(utc));
    }

    [Fact]
    public void Tsv_quotes_cells_excel_would_split()
    {
        var tsv = QuickQuery.ToTsv(["a", "b"], [["1", null], ["x\ty", "dòng 1\ndòng \"2\""]]);
        Assert.Equal("a\tb\r\n1\t\r\n\"x\ty\"\t\"dòng 1\ndòng \"\"2\"\"\"\r\n", tsv);
        Assert.Equal("1\t2\r\n", QuickQuery.ToTsv(null, [["1", "2"]]));
    }

    [Fact]
    public void Cell_text_widths_and_file_name()
    {
        Assert.Equal("NULL", QuickQuery.CellText(null));
        Assert.Equal("a ⏎…", QuickQuery.CellText("a\r\nb"));
        Assert.Equal(500 + " ⏎…".Length, QuickQuery.CellText(new string('x', 600)).Length);
        var widths = QuickQuery.ColumnWidths(["id", "ghi_chu"], [["1", new string('x', 200)], ["22", null]]);
        Assert.Equal(60, widths[0]);
        Assert.Equal(420, widths[1]);
        Assert.Equal("dev_a_20261126-093005.csv", QuickQuery.ExportFileName("dev a", new DateTime(2026, 11, 26, 9, 30, 5)));
        Assert.Equal("ketqua_20261126-093005.csv", QuickQuery.ExportFileName(" ", new DateTime(2026, 11, 26, 9, 30, 5)));
        Assert.Equal("x_y_20261126-093005.csv", QuickQuery.ExportFileName("x/y", new DateTime(2026, 11, 26, 9, 30, 5)));
    }

    [Fact]
    public void Statement_result_describes_itself()
    {
        Assert.Equal("Dòng 3 · SELECT · 1,234 dòng · 12 ms", new QueryStatementResult(3, "SELECT", 1234, 12).Describe().Replace('.', ','));
        Assert.Equal("Dòng 1 · CREATE · 5 ms", new QueryStatementResult(1, "CREATE", null, 5).Describe());
        Assert.Contains("LỖI: 42P01", new QueryStatementResult(1, "SELECT", null, 5, "42P01: x").Describe());
    }

    [Fact]
    public void Production_asks_only_when_writing()
    {
        Assert.Null(ProductionGuard.ForQuery(true, false, "prod_db", "p"));
        Assert.Null(ProductionGuard.ForQuery(false, true, "dev_db", "d"));
        Assert.Equal("prod_db", ProductionGuard.ForQuery(true, true, "prod_db", "p"));
        Assert.Equal("p", ProductionGuard.ForQuery(true, true, " ", "p"));
    }

    [Fact]
    public void History_moves_repeats_to_top_caps_and_filters()
    {
        using var temp = new TempFolder();
        var store = new QueryHistoryStore(temp.Path);
        store.Add(new QueryRecord { Sql = "SELECT 1", Target = "h:1/a", Profile = "dev" });
        store.Add(new QueryRecord { Sql = "SELECT 2", Target = "h:1/a", Profile = "dev" });
        var list = store.Add(new QueryRecord { Sql = "SELECT 1\r\n", Target = "h:1/a", Profile = "dev", Rows = 1 });
        Assert.Equal(["SELECT 1\r\n", "SELECT 2"], list.Select(r => r.Sql));
        list = store.Add(new QueryRecord { Sql = "SELECT 1", Target = "h:1/b", Profile = "test" });
        Assert.Equal(3, list.Count); // server khác → giữ riêng

        Assert.Equal(2, QueryHistoryStore.Filter(list, "DEV").Count());
        Assert.Single(QueryHistoryStore.Filter(list, "select test"));
        Assert.Single(QueryHistoryStore.Filter(list, "2"));
        Assert.Single(QueryHistoryStore.Filter(list, "/b"));
        Assert.Equal(3, QueryHistoryStore.Filter(list, "  ").Count());

        for (var i = 0; i < QueryHistoryStore.MaxRecords + 5; i++)
        {
            store.Add(new QueryRecord { Sql = "SELECT " + i, Target = "x" });
        }
        var loaded = store.Load();
        Assert.Equal(QueryHistoryStore.MaxRecords, loaded.Count);
        Assert.Equal("SELECT " + (QueryHistoryStore.MaxRecords + 4), loaded[0].Sql);
        store.Clear();
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Settings_keep_query_options_in_range()
    {
        var s = new AppSettings();
        Assert.Equal(1000, s.QueryMaxRows);
        Assert.Equal(60, s.QueryTimeoutSeconds);
        s.QueryTimeoutSeconds = -5;
        Assert.Equal(0, s.QueryTimeoutSeconds);
        s.QueryTimeoutSeconds = 99999;
        Assert.Equal(3600, s.QueryTimeoutSeconds);
    }
}

/// <summary>Chạy SQL trên PostgreSQL thật (cần DBTOOLS_TEST_PG): chỉ đọc, ghi, giới hạn dòng, lỗi rollback, huỷ, hết giờ, xuất CSV.</summary>
public sealed class PgQueryIntegrationTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");
    private readonly string _db = "dbtools_query_" + Guid.NewGuid().ToString("N")[..10];
    private ConnectionProfile _p = new();

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        var b = new NpgsqlConnectionStringBuilder(Server);
        var admin = new ConnectionProfile { Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = "postgres" };
        await PgAdmin.CreateDatabaseAsync(admin, _db, CancellationToken.None);
        _p = admin.Clone("test");
        _p.Database = _db;
        await RunAsync("""
            CREATE SCHEMA app;
            CREATE TABLE app.item (id int PRIMARY KEY, name text, at date, amount numeric(10,2), stamp timestamptz, tags text[]);
            INSERT INTO app.item SELECT g, 'món ' || g, date '2026-11-01' + g, g * 1.5, timestamptz '2026-11-26 00:00:00+00', ARRAY['a', 'b c']
              FROM generate_series(1, 2500) g;
            UPDATE app.item SET name = NULL WHERE id = 2;
            """, write: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        var b = new NpgsqlConnectionStringBuilder(Server) { Database = "postgres", Pooling = false };
        await using var c = new NpgsqlConnection(b.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_db} WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }

    private Task<QueryOutcome> RunAsync(string sql, bool write = false, int maxRows = 1000, int timeout = 60, CancellationToken ct = default,
        Action<string>? notice = null) =>
        PgQuery.RunAsync(_p, QuickQuery.Plan(sql).Statements, write, maxRows, timeout, ct, notice);

    private async Task<long> CountAsync()
    {
        var o = await RunAsync("SELECT count(*) FROM app.item");
        return long.Parse(o.Table!.Rows[0][0]!);
    }

    [Fact]
    public async Task Read_only_reads_with_limit_and_formats_values()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var o = await RunAsync("SELECT 1 AS x; SELECT id, name, at, amount, tags FROM app.item ORDER BY id;", maxRows: 100);
        Assert.True(o.Success);
        Assert.False(o.Committed);
        Assert.Equal(2, o.Statements.Count);
        var t = o.Table!;
        Assert.True(t.Truncated);
        Assert.True(t.Cursor);
        Assert.Equal(100, t.Rows.Count);
        Assert.Equal(["id", "name", "at", "amount", "tags"], t.Columns);
        Assert.Equal(["integer", "text", "date", "numeric(10, 2)", "text[]"], t.Types);
        Assert.Equal(["1", "món 1", "2026-11-02", "1.50", "{a,\"b c\"}"], t.Rows[0]);
        Assert.Null(t.Rows[1][1]);

        var all = await RunAsync("SELECT id FROM app.item", maxRows: 0);
        Assert.Equal(2500, all.Table!.Rows.Count);
        Assert.False(all.Table.Truncated);

        var exact = await RunAsync("SELECT id FROM app.item WHERE id <= 100", maxRows: 100);
        Assert.False(exact.Table!.Truncated);
    }

    [Fact]
    public async Task Read_only_refuses_writes_and_changes_nothing()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var o = await RunAsync("SELECT 1;\nDELETE FROM app.item;");
        Assert.False(o.Success);
        Assert.Equal(2, o.ErrorLine);
        Assert.StartsWith("25006", o.Error);
        Assert.Equal(2500, await CountAsync());

        // search_path theo schema của hồ sơ
        _p.Schema = "app";
        var s = await RunAsync("SELECT count(*) FROM item");
        Assert.Equal("2500", s.Table!.Rows[0][0]);
    }

    [Fact]
    public async Task Write_commits_and_error_rolls_back_everything()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var bad = await RunAsync("DELETE FROM app.item WHERE id > 2000;\nSELECT * FROM khong_co;", write: true);
        Assert.False(bad.Success);
        Assert.Equal(2, bad.ErrorLine);
        Assert.StartsWith("42P01", bad.Error);
        Assert.Equal(500L, bad.Statements[0].Rows);
        Assert.Equal(2500, await CountAsync());

        var ok = await RunAsync("DELETE FROM app.item WHERE id > 2000 RETURNING id; UPDATE app.item SET name = 'x' WHERE id = 1;", write: true, maxRows: 10);
        Assert.True(ok.Success);
        Assert.True(ok.Committed);
        Assert.Equal("DELETE", ok.Statements[0].Kind);
        Assert.Equal(500L, ok.Statements[0].Rows);
        Assert.Equal(("UPDATE", 1L), (ok.Statements[1].Kind, ok.Statements[1].Rows));
        Assert.Equal(10, ok.Table!.Rows.Count);
        Assert.True(ok.Table.Truncated);
        Assert.False(ok.Table.Cursor);
        Assert.Equal(2000, await CountAsync());
    }

    [Fact]
    public async Task Notices_timeout_and_cancel()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var notices = new List<string>();
        var n = await RunAsync("DO $$ BEGIN RAISE NOTICE 'xin chào'; END $$; SHOW search_path;", notice: notices.Add);
        Assert.True(n.Success);
        Assert.Contains(notices, x => x.Contains("xin chào"));
        Assert.Equal("DO", n.Statements[0].Kind);
        Assert.Single(n.Table!.Rows);

        var slow = await RunAsync("SELECT pg_sleep(5)", timeout: 1);
        Assert.False(slow.Success);
        Assert.StartsWith("Quá 1 giây", slow.Error);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var started = DateTime.Now;
        var cancelled = await RunAsync("UPDATE app.item SET name = 'y'; SELECT pg_sleep(30)", write: true, ct: cts.Token);
        Assert.Equal("Đã huỷ", cancelled.Error);
        Assert.True(DateTime.Now - started < TimeSpan.FromSeconds(15));
        var check = await RunAsync("SELECT count(*) FROM app.item WHERE name = 'y'");
        Assert.Equal("0", check.Table!.Rows[0][0]);
    }

    [Fact]
    public async Task Export_reads_every_row_again_read_only()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        using var temp = new TempFolder();
        var o = await RunAsync("SELECT id, name FROM app.item ORDER BY id", maxRows: 100);
        var path = Path.Combine(temp.Path, "x", "a.csv");
        Assert.Equal(2500, await PgQuery.ExportCsvAsync(_p, o.Table!, path, 60, CancellationToken.None));
        var lines = File.ReadAllLines(path);
        Assert.Equal(2501, lines.Length);
        Assert.Equal("id,name", lines[0].TrimStart('﻿'));
        Assert.Equal("2,", lines[2]);
        Assert.False(File.Exists(path + ".tmp"));

        var small = await RunAsync("SELECT 'a,b' AS v");
        Assert.Equal(1, await PgQuery.ExportCsvAsync(_p, small.Table!, path, 60, CancellationToken.None));
        Assert.Equal("\"a,b\"", File.ReadAllLines(path)[1]);

        var returning = await RunAsync("DELETE FROM app.item WHERE id > 2490 RETURNING id", write: true, maxRows: 5);
        await Assert.ThrowsAsync<PostgresException>(() => PgQuery.ExportCsvAsync(_p, returning.Table!, path, 60, CancellationToken.None));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
