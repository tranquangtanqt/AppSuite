using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>Trang Nhật ký server: tách dòng log theo log_line_prefix, ghép dòng, phân loại, lọc, xuất, giải thích cấu hình (2026-11-23).</summary>
public class ServerLogTests
{
    /// <summary>log_line_prefix của server Fujitsu Enterprise Postgres 15 người dùng gửi (192.168.145.107).</summary>
    private const string FepPrefix = "%e: %t [%p]: [%l-1] user = %u,db = %d,remote = %r app = %a ";

    private const string FepLog =
        "00000: 2026-11-23 10:23:44 +07 [7852]: [1-1] user = ,db = ,remote =  app =  LOG:  database system was shut down at 2026-11-13 20:32:02 +07\n"
        + "00000: 2026-11-23 10:36:44 +07 [4588]: [1-1] user = sugaki,db = postgres,remote = 192.168.145.110(63385) app = psql LOG:  statement: select version();\n"
        + "00000: 2026-11-23 10:36:44 +07 [4588]: [2-1] user = sugaki,db = postgres,remote = 192.168.145.110(63385) app = psql LOG:  duration: 98.290 ms\n"
        + "00000: 2026-11-23 10:37:01 +07 [5000]: [1-1] user = mcf,db = sugaki,remote = 192.168.145.20(50001) app = AppSuite DbTools LOG:  statement: SELECT name FROM pg_ls_logdir()\n"
        + "00000: 2026-11-23 10:37:02 +07 [4600]: [1-1] user = mcf,db = sugaki,remote = 192.168.145.21(50002) app = mcframe LOG:  statement: UPDATE item_master\n"
        + "\t   SET price = 10\n"
        + "\t WHERE item_cd = 'A01'\n"
        + "00000: 2026-11-23 10:37:02 +07 [4601]: [1-1] user = mcf,db = sugaki,remote = 192.168.145.21(50003) app = mcframe LOG:  execute <unnamed>: INSERT INTO t_log(msg) VALUES ($1)\n"
        + "00000: 2026-11-23 10:37:02 +07 [4601]: [2-1] user = mcf,db = sugaki,remote = 192.168.145.21(50003) app = mcframe DETAIL:  parameters: $1 = 'xin chào'\n"
        + "00000: 2026-11-23 10:37:02 +07 [4600]: [2-1] user = mcf,db = sugaki,remote = 192.168.145.21(50002) app = mcframe LOG:  duration: 3.5 ms\n"
        + "42P01: 2026-11-23 10:37:05 +07 [4602]: [1-1] user = mcf,db = sugaki,remote = 192.168.145.21(50004) app = mcframe ERROR:  relation \"khong_co\" does not exist at character 15\n"
        + "42P01: 2026-11-23 10:37:05 +07 [4602]: [2-1] user = mcf,db = sugaki,remote = 192.168.145.21(50004) app = mcframe STATEMENT:  select * from khong_co\n"
        + "\t where 1 = 1\n"
        + "00000: 2026-11-23 10:37:06 +07 [4603]: [1-1] user = postgres,db = sugaki,remote = 192.168.145.22(50005) app = psql LOG:  statement: ALTER ROLE mcf PASSWORD 'bi''mat'\n"
        + "00000: 2026-11-23 10:37:07 +07 [4604]: [1-1] user = mcf,db = sugaki,remote = 192.168.145.21(50006) app = mcframe LOG:  statement: /* batch */ WITH x AS (DELETE FROM t RETURNING *) SELECT count(*) FROM x\n"
        + "08006: 2026-11-23 10:37:08 +07 [4605]: [1-1] user = sugaki,db = postgres,remote = 192.168.145.107(54699) app = [unknown] LOG:  could not receive data from client: An existing connection was forcibly closed by the remote host.\n"
        + "\t\n"
        + "00000: 2026-11-23 10:37:09 +07 [4606]: [1-1] user = mcf,db = sugaki,remote = 192.168.145.21(50007) app = [unknown] LOG:  connection authorized: user=mcf database=sugaki\n";

    private static LogParseResult ParseFep(string text = FepLog, bool partial = false) =>
        ServerLogParser.Parse(text, new LogLinePrefix(FepPrefix), partial);

    [Fact]
    public void Fep_log_lines_are_split_into_entries_with_prefix_fields()
    {
        var (entries, unknown) = ParseFep();
        Assert.Equal(0, unknown);
        Assert.Equal(10, entries.Count);

        var server = entries[0];
        Assert.Equal(LogKind.Server, server.Kind);
        Assert.Equal((7852, string.Empty, string.Empty), (server.Pid!.Value, server.User, server.Database));

        var version = entries[1];
        Assert.Equal("2026-11-23 10:36:44 +07", version.Time);
        Assert.Equal(("sugaki", "postgres", "192.168.145.110(63385)", "psql", "00000"), (version.User, version.Database, version.Client, version.App, version.SqlState));
        Assert.Equal(LogKind.Select, version.Kind);
        Assert.Equal("select version();", version.Sql);
        Assert.Equal(98.29, version.DurationMs); // dòng "duration" ngay sau gắn vào câu
        Assert.Equal(2, version.RawLines.Count);
    }

    [Fact]
    public void Multiline_sql_parameters_errors_and_durations_attach_to_the_right_statement()
    {
        var entries = ParseFep().Entries;

        var update = entries.Single(e => e.Pid == 4600);
        Assert.Equal(LogKind.Update, update.Kind);
        Assert.Equal("UPDATE item_master\n   SET price = 10\n WHERE item_cd = 'A01'", update.Sql);
        Assert.Equal(3.5, update.DurationMs); // duration tới sau dòng của pid khác vẫn tìm đúng câu cùng pid

        var insert = entries.Single(e => e.Pid == 4601);
        Assert.Equal(LogKind.Insert, insert.Kind);
        Assert.Equal("execute <unnamed>", insert.Message);
        Assert.Equal("DETAIL: parameters: $1 = 'xin chào'", insert.Detail);

        var error = entries.Single(e => e.Pid == 4602);
        Assert.Equal((LogKind.Error, "ERROR", "42P01"), (error.Kind, error.Level, error.SqlState));
        Assert.Equal("select * from khong_co\n where 1 = 1", error.Sql);
        Assert.StartsWith("relation \"khong_co\"", error.Message);

        Assert.Equal(LogKind.Delete, entries.Single(e => e.Pid == 4604).Kind); // WITH ... DELETE
        Assert.Equal(LogKind.Server, entries.Single(e => e.Pid == 4605).Kind);
        Assert.Equal(LogKind.Connection, entries.Single(e => e.Pid == 4606).Kind);
    }

    [Fact]
    public void Passwords_are_masked_everywhere()
    {
        var alter = ParseFep().Entries.Single(e => e.Pid == 4603);
        Assert.Equal(LogKind.Ddl, alter.Kind);
        Assert.Equal("ALTER ROLE mcf PASSWORD '***'", alter.Sql);
        Assert.DoesNotContain("mat", alter.RawLines[0]);
        Assert.DoesNotContain("mat", ServerLogExport.ToCsv([alter]));
        Assert.Equal("CREATE USER a WITH password '***' LOGIN", ServerLogParser.MaskPasswords("CREATE USER a WITH password 'x y' LOGIN"));
    }

    [Theory]
    [InlineData("select 1", LogKind.Select)]
    [InlineData("  (SELECT 1) UNION (SELECT 2)", LogKind.Select)]
    [InlineData("-- ghi chú\nInsert into t values (1)", LogKind.Insert)]
    [InlineData("MERGE INTO t USING s ON true WHEN MATCHED THEN DO NOTHING", LogKind.Update)]
    [InlineData("truncate t", LogKind.Delete)]
    [InlineData("WITH a AS (SELECT 1) INSERT INTO t SELECT * FROM a", LogKind.Insert)]
    [InlineData("with a as (select 1) select * from a", LogKind.Select)]
    [InlineData("CREATE OR REPLACE FUNCTION f() RETURNS int LANGUAGE sql RETURN 1", LogKind.Ddl)]
    [InlineData("comment on table t is 'x'", LogKind.Ddl)]
    [InlineData("BEGIN", LogKind.OtherSql)]
    [InlineData("SET search_path TO a", LogKind.OtherSql)]
    public void Statement_kind_comes_from_the_first_keyword(string sql, LogKind kind) => Assert.Equal(kind, ServerLogParser.KindOf(sql));

    [Fact]
    public void Default_prefix_with_q_milliseconds_and_padding_is_understood()
    {
        // Mặc định PostgreSQL 15+: '%m [%p] '; kiểu hay dùng có %q (tiến trình nền không in phần sau %q) và canh lề %-10u.
        var p = new LogLinePrefix("%m [%p] %q%-8u@%d ");
        var log = "2026-11-23 10:00:00.123 JST [100] LOG:  checkpoint starting: time\n"
                  + "2026-11-23 10:00:01.456 JST [101] mcf     @sugaki LOG:  statement: delete from t\n";
        var entries = ServerLogParser.Parse(log, p).Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(("2026-11-23 10:00:00.123 JST", LogKind.Server, ""), (entries[0].Time, entries[0].Kind, entries[0].User));
        Assert.Equal(("mcf", "sugaki", LogKind.Delete), (entries[1].User, entries[1].Database, entries[1].Kind));
        Assert.False(p.Has('a'));
        Assert.True(p.Has('u'));
    }

    [Fact]
    public void Partial_chunk_drops_the_cut_line_and_orphan_continuations()
    {
        // Đọc đoạn cuối file: dòng đầu bị cắt giữa chừng, dòng nối tiếp của câu trước đó không có câu gốc.
        var tail = "SET price = 10\n" + FepLog[FepLog.IndexOf("\t WHERE", StringComparison.Ordinal)..];
        var (entries, unknown) = ParseFep(tail, partial: true);
        Assert.Equal(0, unknown);
        Assert.Equal(LogKind.Insert, entries[0].Kind);

        // Định dạng khác log_line_prefix: không mất dòng, đếm là dòng không nhận ra.
        var other = ServerLogParser.Parse("2026-11-23 LOG: abc\nxyz\n", new LogLinePrefix(FepPrefix));
        Assert.Equal(2, other.UnknownLines);
        Assert.Single(other.Entries);
    }

    [Fact]
    public void Filter_by_kind_text_user_database_client_and_own_app()
    {
        var entries = ParseFep().Entries;
        var all = Enum.GetValues<LogKind>().ToHashSet();
        Assert.DoesNotContain(entries.Where(new LogFilter(all).Matches), e => e.App == ServerLogParser.OwnApp);
        Assert.Contains(entries.Where(new LogFilter(all, HideOwn: false).Matches), e => e.App == ServerLogParser.OwnApp);

        Assert.Equal([4600], entries.Where(new LogFilter(new HashSet<LogKind> { LogKind.Update }).Matches).Select(e => e.Pid!.Value));
        Assert.Equal([4600], entries.Where(new LogFilter(all, Text: "ITEM_MASTER").Matches).Select(e => e.Pid!.Value));
        Assert.Equal([4601], entries.Where(new LogFilter(all, Text: "xin chào").Matches).Select(e => e.Pid!.Value)); // tìm cả tham số
        Assert.Equal([4603], entries.Where(new LogFilter(all, User: "postgres").Matches).Select(e => e.Pid!.Value));
        Assert.Equal(2, entries.Count(new LogFilter(all, Database: "postgres").Matches)); // trừ câu của DbTools
        Assert.Equal([4603], entries.Where(new LogFilter(all, Client: "145.22").Matches).Select(e => e.Pid!.Value));
        Assert.Equal(2, entries.Count(new LogFilter(all, Client: "psql").Matches)); // ô máy tìm cả tên ứng dụng
    }

    [Fact]
    public void Export_csv_has_header_and_quoted_multiline_sql_text_keeps_raw_lines()
    {
        var update = ParseFep().Entries.Single(e => e.Pid == 4600);
        var csv = ServerLogExport.ToCsv([update]);
        Assert.StartsWith("Giờ,PID,User,Database,Máy,Ứng dụng,Loại,Mức,Mã lỗi,Thời gian (ms),Câu SQL / thông báo,Chi tiết\r\n", csv);
        Assert.Contains("2026-11-23 10:37:02 +07,4600,mcf,sugaki,192.168.145.21(50002),mcframe,UPDATE,LOG,00000,3.5,\"UPDATE item_master\n", csv);

        var text = ServerLogExport.ToText([update]);
        Assert.Equal(4, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length); // câu 3 dòng + dòng duration
        Assert.StartsWith("00000: 2026-11-23 10:37:02 +07 [4600]", text);
    }

    private static LogServerInfo Info(params (string Name, string Value)[] settings) =>
        new(settings.ToDictionary(s => s.Name, s => s.Value), Superuser: true, Monitor: true, ReadFiles: true);

    [Fact]
    public void Config_of_the_fep_server_is_explained()
    {
        var lines = LogConfig.Describe(Info(("logging_collector", "on"), ("log_directory", "C:/log"), ("log_filename", "logfile-%a.log"),
            ("log_statement", "all"), ("log_min_duration_statement", "1000"), ("log_line_prefix", FepPrefix),
            ("log_truncate_on_rotation", "on"), ("lc_messages", "C"), ("log_destination", "stderr,eventlog")), Week(9826), 1L << 30, Now);
        Assert.Equal(
        [
            "Ghi file vào C:/log (trên máy server), tên logfile-%a.log.",
            "Ghi mọi câu lệnh (cả SELECT); thêm câu chạy lâu hơn 1,000 ms.",
            $"Đầu dòng log (log_line_prefix = '{FepPrefix}') có đủ giờ, user, database, máy, ứng dụng.",
            "Chỉ giữ 7 ngày: mỗi thứ trong tuần 1 file, tuần sau ghi đè (hiện 7 file, 10 KB). Cần xem lâu hơn thì lưu file về máy (Xuất).",
            "Dung lượng thư mục log: 10 KB (7 file, lớn nhất logfile-Sun.log 1 KB); tăng khoảng 1 KB / ngày (theo 7 ngày qua). Ngưỡng cảnh báo 1.0 GB (đổi ở Cài đặt).",
        ], lines.Select(l => l.Text.Replace("1.000", "1,000")));
        Assert.Equal([false, false, false, true, false], lines.Select(l => l.Warning));
    }

    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);

    /// <summary>7 file logfile-%a.log, mỗi ngày 1 file (mới nhất hôm nay), tổng <paramref name="total"/> byte.</summary>
    private static List<LogFileStat> Week(long total)
    {
        string[] days = ["Mon", "Sun", "Sat", "Fri", "Thu", "Wed", "Tue"];
        return days.Select((d, i) => new LogFileStat($"logfile-{d}.log", total / 7 + (i == 1 ? total % 7 : 0), Now.AddDays(-i))).ToList();
    }

    [Fact]
    public void Volume_warns_over_threshold_or_when_unlimited_logs_will_reach_it_soon()
    {
        // 3 GB, ngưỡng 1 GB → vượt.
        var over = LogConfig.Volume([new("a.log", 3L << 30, Now)], 1L << 30, unlimited: false, Now);
        Assert.True(over.Warning);
        Assert.StartsWith("Vượt ngưỡng cảnh báo! Dung lượng thư mục log: 3.0 GB", over.Text);

        // Không tự xoá, 700 MB, tăng 100 MB / ngày (7 file trong 7 ngày) → ~3 ngày nữa vượt 1 GB.
        var files = Enumerable.Range(0, 7).Select(i => new LogFileStat($"p{i}.log", 100L << 20, Now.AddDays(-i - 0.5))).ToList();
        var soon = LogConfig.Volume(files, 1L << 30, unlimited: true, Now);
        Assert.True(soon.Warning);
        Assert.Contains("tăng khoảng 100.0 MB / ngày", soon.Text);
        Assert.EndsWith("Theo tốc độ này khoảng 3 ngày nữa sẽ vượt ngưỡng.", soon.Text);

        // Cùng tốc độ nhưng server tự xoay vòng ghi đè → không cảnh báo; ngưỡng 100 GB cũng không.
        Assert.False(LogConfig.Volume(files, 1L << 30, unlimited: false, Now).Warning);
        Assert.False(LogConfig.Volume(files, 100L << 30, unlimited: true, Now).Warning);

        // File cũ hơn 7 ngày không tính vào tốc độ.
        Assert.Contains("tăng khoảng 0 byte / ngày", LogConfig.Volume([new("old.log", 5L << 20, Now.AddDays(-30))], 1L << 30, true, Now).Text);
    }

    [Fact]
    public void Reader_gives_the_same_entries_however_the_log_is_cut_into_chunks()
    {
        // Theo dõi đọc từng đoạn mới: đoạn có thể cắt giữa dòng, giữa câu nhiều dòng, giữa câu và dòng duration / DETAIL.
        var expected = Describe(ParseFep().Entries);
        var prefix = new LogLinePrefix(FepPrefix);
        for (var cut = 1; cut < FepLog.Length; cut++)
        {
            var reader = new ServerLogReader(prefix);
            reader.Append(FepLog[..cut]);
            reader.Append(FepLog[cut..]);
            Assert.Equal(expected, Describe(reader.Entries));
            Assert.Equal(0, reader.UnknownLines);
        }

        // 3 đoạn, cắt ngay trong dòng tiếp nối của câu UPDATE: Append trả vị trí mục cần hiện lại (mục UPDATE).
        var r3 = new ServerLogReader(prefix);
        var a = FepLog.IndexOf("\t   SET", StringComparison.Ordinal);
        r3.Append(FepLog[..a]);
        var update = r3.Entries.Count - 1;
        Assert.Equal("UPDATE item_master", r3.Entries[update].Sql);
        Assert.Equal(update, r3.Append(FepLog[a..(a + 5)])); // dòng dở: chưa thêm gì, mục cuối vẫn có thể đổi
        Assert.Equal(update, r3.Append(FepLog[(a + 5)..]));
        Assert.Equal(expected, Describe(r3.Entries));

        static List<string> Describe(IEnumerable<LogEntry> entries) => entries
            .Select(e => $"{e.Pid}|{e.Kind}|{e.Sql}|{e.Message}|{e.DurationMs}|{e.Detail}|{e.RawLines.Count}").ToList();
    }

    [Fact]
    public void Reader_without_trailing_newline_keeps_the_last_line_until_flush()
    {
        var reader = new ServerLogReader(new LogLinePrefix(FepPrefix));
        var lastLine = FepLog.TrimEnd('\n');
        reader.Append(lastLine);
        var countBefore = reader.Entries.Count;
        reader.Flush();
        Assert.Equal(countBefore + 1, reader.Entries.Count);
        Assert.Equal(LogKind.Connection, reader.Entries[^1].Kind);
    }

    [Fact]
    public void Config_warns_about_missing_logging_prefix_fields_retention_and_permissions()
    {
        var lines = LogConfig.Describe(new LogServerInfo(new Dictionary<string, string>
        {
            ["logging_collector"] = "off", ["log_statement"] = "none", ["log_min_duration_statement"] = "-1",
            ["log_line_prefix"] = "%m [%p] ", ["log_filename"] = "postgresql-%Y-%m-%d.log", ["lc_messages"] = "ja_JP.UTF-8",
            ["log_destination"] = "csvlog",
        }, Superuser: false, Monitor: false, ReadFiles: false), [new("a.log", 1L << 30, Now.AddDays(-2)), new("b.log", 1L << 29, Now.AddDays(-1)), new("c.log", 1L << 29, Now)], 1L << 30, Now);
        var text = string.Join("\n", lines.Select(l => l.Text));
        Assert.Contains("logging_collector = off", text);
        Assert.Contains("Không ghi câu lệnh", text);
        Assert.Contains("thiếu user (%u), database (%d), máy / IP (%r / %h)", text);
        Assert.Contains("Không tự xoá file cũ (hiện 3 file, 2.0 GB)", text);
        Assert.Contains("lc_messages = ja_JP.UTF-8", text);
        Assert.Contains("chỉ đọc file .log", text);
        Assert.Contains("pg_ls_logdir", text);
        Assert.Contains("pg_read_server_files", text);
        Assert.All(lines.Where(l => !l.Text.Contains(".csv") && !l.Text.StartsWith("Không tự xoá")), l => Assert.True(l.Warning, l.Text));
        Assert.Contains(lines, l => l.Warning && l.Text.StartsWith("Vượt ngưỡng cảnh báo! Dung lượng thư mục log: 2.0 GB"));
    }

    [Fact]
    public void Log_warn_threshold_defaults_to_1024_mb_and_is_clamped()
    {
        Assert.Equal(1024, new AppSettings().LogWarnMb);
        Assert.Equal(10, new AppSettings { LogWarnMb = 0 }.LogWarnMb);
        Assert.Equal(1_000_000, new AppSettings { LogWarnMb = int.MaxValue }.LogWarnMb);
    }

    /// <summary>PostgreSQL thật (DBTOOLS_TEST_PG, superuser, logging_collector = on): chạy 1 câu có dấu riêng với
    /// log_statement = all trong phiên, đọc file log hiện tại qua SQL, tìm thấy đúng câu đó.</summary>
    [Fact]
    public async Task Real_server_log_is_listed_read_and_parsed()
    {
        var server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");
        Assert.SkipWhen(string.IsNullOrEmpty(server), "Chưa đặt DBTOOLS_TEST_PG.");
        var b = new NpgsqlConnectionStringBuilder(server);
        var profile = new ConnectionProfile { Name = "test", Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = "postgres" };
        var info = await PgServerLog.InfoAsync(profile, CancellationToken.None);
        Assert.SkipWhen(info.Get("logging_collector") != "on", "Server thử không bật logging_collector.");

        var marker = "dbtools_log_" + Guid.NewGuid().ToString("N")[..8];
        await using (var c = await Pg.OpenAsync(profile, CancellationToken.None))
        {
            foreach (var sql in new[] { "SET log_statement = 'all'", $"SELECT '{marker}'" })
            {
                await using var cmd = new NpgsqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        await Task.Delay(500);

        var files = await PgServerLog.ListFilesAsync(profile, CancellationToken.None);
        var current = Assert.Single(files, f => f.IsCurrent);
        var chunk = await PgServerLog.ReadAsync(profile, current.Name, 256 * 1024, CancellationToken.None);
        Assert.True(chunk.FileSize >= current.Size);
        var entries = ServerLogParser.Parse(chunk.Text, new LogLinePrefix(info.Get("log_line_prefix")), chunk.Offset > 0).Entries;
        var hit = Assert.Single(entries, e => e.Sql.Contains(marker));
        Assert.Equal(LogKind.Select, hit.Kind);
        Assert.Equal(ServerLogParser.OwnApp, hit.App is { Length: > 0 } app ? app : ServerLogParser.OwnApp); // prefix không có %a thì trống

        // Theo dõi: 1 kết nối giữ mở, đọc đúng phần mới ghi thêm sau câu thứ 2.
        await using var follower = await ServerLogFollower.OpenAsync(profile, CancellationToken.None);
        var (currentName, size0) = await follower.StatAsync(current.Name, CancellationToken.None);
        Assert.Equal(current.Name, currentName);
        var reader = new ServerLogReader(new LogLinePrefix(info.Get("log_line_prefix")));
        var marker2 = marker + "_theo_doi";
        await using (var c = await Pg.OpenAsync(profile, CancellationToken.None))
        {
            foreach (var sql in new[] { "SET log_statement = 'all'", $"SELECT '{marker2}'" })
            {
                await using var cmd = new NpgsqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        await Task.Delay(500);
        var (_, size1) = await follower.StatAsync(current.Name, CancellationToken.None);
        Assert.True(size1 > size0);
        reader.Append(await follower.ReadAsync(current.Name, size0!.Value, size1!.Value - size0.Value, CancellationToken.None));
        Assert.Single(reader.Entries, e => e.Sql.Contains(marker2));
        Assert.DoesNotContain(reader.Entries, e => e.Sql.Contains($"'{marker}'")); // câu cũ nằm trước phần mới
        Assert.Null((await follower.StatAsync("khong_co_file_nay.log", CancellationToken.None)).Size);
    }
}
