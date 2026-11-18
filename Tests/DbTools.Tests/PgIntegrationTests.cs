using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>
/// Chạy thật trên PostgreSQL. Cần biến môi trường DBTOOLS_TEST_PG = connection string tới 1 server thử
/// (vd "Host=localhost;Port=55432;Username=postgres;Password=..."). Mỗi test tạo 1 database tạm tên dbtools_test_* rồi xoá -
/// không đụng database khác. Không có biến → bỏ qua.
/// </summary>
public sealed class PgIntegrationTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");
    private readonly string _database = "dbtools_test_" + Guid.NewGuid().ToString("N")[..12];
    private ConnectionProfile _profile = new();

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        var b = new NpgsqlConnectionStringBuilder(Server);
        _profile = new ConnectionProfile { Name = "test", Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = _database };
        await AdminAsync($"CREATE DATABASE {_database}");
    }

    public async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrEmpty(Server))
        {
            await AdminAsync($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)");
        }
    }

    private static async Task AdminAsync(string sql)
    {
        var b = new NpgsqlConnectionStringBuilder(Server) { Database = "postgres", Pooling = false };
        await using var c = new NpgsqlConnection(b.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var c = await Pg.OpenAsync(_profile, CancellationToken.None);
        await using var cmd = new NpgsqlCommand(sql, c);
        return await cmd.ExecuteScalarAsync();
    }

    private async Task ExecAsync(string sql)
    {
        await using var c = await Pg.OpenAsync(_profile, CancellationToken.None);
        foreach (var s in SqlSplitter.Split(sql).Statements)
        {
            await using var cmd = new NpgsqlCommand(s.Text, c);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private Task<bool> ExistsAsync(string relation) =>
        ScalarAsync($"SELECT to_regclass('{relation}') IS NOT NULL").ContinueWith(t => t.Result is true);

    private sealed class Collect : IProgress<RunEvent>
    {
        public List<RunEvent> Events { get; } = [];

        public void Report(RunEvent value)
        {
            lock (Events)
            {
                Events.Add(value);
            }
        }
    }

    private async Task<(RunOutcome Outcome, Collect Events)> RunAsync(IReadOnlyList<SqlScriptFile> files, bool dryRun, DropScript? drop = null)
    {
        var events = new Collect();
        var outcome = await PgRunner.RunAsync(_profile, files, drop, dryRun, events, TestContext.Current.CancellationToken);
        return (outcome, events);
    }

    private static List<SqlScriptFile> SampleInOrder(TempFolder folder)
    {
        SampleScripts.WriteTo(folder);
        return RunOrder.AutoSort(SampleScripts.Load(folder)).Files.ToList();
    }

    [Fact]
    public async Task Dry_run_executes_everything_then_leaves_db_unchanged()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        await ExecAsync(SampleScripts.BaseObjects);
        using var folder = new TempFolder();
        var files = SampleInOrder(folder);

        var (outcome, events) = await RunAsync(files, dryRun: true);

        Assert.True(outcome.Success, outcome.Error);
        Assert.False(outcome.Committed);
        Assert.Equal(files.Count, outcome.FilesDone);
        Assert.Equal(files.Count, events.Events.OfType<FileFinished>().Count());
        Assert.False(await ExistsAsync("app_order_all"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM app_history_table"));
    }

    [Fact]
    public async Task Alphabetical_order_fails_and_rolls_back()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        await ExecAsync(SampleScripts.BaseObjects);
        using var folder = new TempFolder();
        SampleScripts.WriteTo(folder);
        var alphabetical = SampleScripts.Load(folder).OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();

        var (outcome, events) = await RunAsync(alphabetical, dryRun: false);

        Assert.False(outcome.Success);
        Assert.False(outcome.Committed);
        var failed = Assert.Single(events.Events.OfType<FileFailed>());
        Assert.Equal(0, failed.Index); // CHECK\... chạy đầu tiên, bảng chưa có
        Assert.Equal(3, failed.Line);  // dòng ALTER TABLE (sau \encoding và dòng trống)
        Assert.Contains("42P01", failed.Message); // undefined_table
        Assert.False(await ExistsAsync("app_order_no_seq"));
    }

    [Fact]
    public async Task Run_commits_and_rerun_needs_drop_first()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        await ExecAsync(SampleScripts.BaseObjects);
        using var folder = new TempFolder();
        var files = SampleInOrder(folder);

        var (first, _) = await RunAsync(files, dryRun: false);
        Assert.True(first.Success, first.Error);
        Assert.True(first.Committed);
        Assert.True(await ExistsAsync("app_order_all"));
        Assert.True(await ExistsAsync("app_order_$s"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM app_history_table"));
        Assert.Equal("受注データ // đơn hàng", await ScalarAsync("SELECT obj_description('app_order_all'::regclass)"));
        // Trigger CO_VT điền co_cd; trigger lịch sử đang bị DISABLE.
        await ExecAsync("INSERT INTO app_order_all (order_no) VALUES ('A1')");
        Assert.Equal("C01", await ScalarAsync("SELECT co_cd FROM app_order_all"));

        // Kiểm tra: đối tượng đã có → CREATE TABLE / TRIGGER / SEQUENCE sẽ lỗi.
        var existing = await PgCatalog.FindExistingAsync(_profile, Precheck.CreatedNames(files), CancellationToken.None);
        var warnings = files.SelectMany(f => Precheck.Compare(f, existing, dropFirst: false)).Where(i => i.Level == IssueLevel.Warning).ToList();
        Assert.Contains(warnings, w => w.Message.Contains("CREATE TABLE app_order_all"));
        Assert.Contains(warnings, w => w.Message.Contains("CREATE TRIGGER app_order_all_tr_k"));
        var rows = await PgCatalog.CountRowsAsync(_profile, DropScript.Build(files).Tables, 1000, CancellationToken.None);
        Assert.Equal(1L, rows["app_order_all"]);

        var (rerun, _) = await RunAsync(files, dryRun: false);
        Assert.False(rerun.Success);
        Assert.Contains("42P07", rerun.Error); // duplicate_table

        var (dropped, _) = await RunAsync(files, dryRun: false, DropScript.Build(files));
        Assert.True(dropped.Success, dropped.Error);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM app_order_all")); // bảng tạo lại, mất dữ liệu cũ
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM app_history_table")); // DELETE trước INSERT
    }

    [Fact]
    public async Task Drop_first_on_a_fresh_database_works()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        await ExecAsync(SampleScripts.BaseObjects + "\nCREATE TABLE base_table (x int);");
        using var folder = new TempFolder();
        var files = SampleInOrder(folder);
        files.Add(SqlScriptFile.Parse(System.Text.Encoding.UTF8.GetBytes("""
            CREATE OR REPLACE FUNCTION base_tr_body() RETURNS trigger AS $$ BEGIN RETURN NEW; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER base_tr BEFORE INSERT ON base_table FOR EACH ROW EXECUTE FUNCTION base_tr_body();
            ALTER TABLE base_table ADD CONSTRAINT base_ck CHECK (x > 0);
            """), "extra.sql", "extra.sql"));

        // Lần đầu: chưa có đối tượng nào - DROP ... IF EXISTS phải không lỗi.
        var (first, _) = await RunAsync(files, dryRun: false, DropScript.Build(files));
        Assert.True(first.Success, first.Error);
        // Lần 2: trigger / ràng buộc trên bảng có sẵn được xoá riêng rồi tạo lại.
        var (second, _) = await RunAsync(files, dryRun: false, DropScript.Build(files));
        Assert.True(second.Success, second.Error);
    }

    [Fact]
    public async Task Chosen_schema_creates_there_and_finds_base_objects_in_public()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        // Bảng nền ở public; public còn có 1 bảng trùng tên với bảng script tạo (có dữ liệu) - không được đụng tới.
        await ExecAsync(SampleScripts.BaseObjects + "\nCREATE SCHEMA addon;\nCREATE TABLE app_order_all (x int);\nINSERT INTO app_order_all VALUES (42);");
        using var folder = new TempFolder();
        var files = SampleInOrder(folder);
        var addon = _profile.Clone("addon");
        addon.Schema = "addon";
        addon.IncludePublic = true;

        Assert.True(await PgCatalog.SchemaExistsAsync(addon, CancellationToken.None));
        Assert.Contains("addon", await PgCatalog.ListSchemasAsync(addon, CancellationToken.None));
        Assert.Contains("tạo đối tượng ở schema addon", await Pg.TestAsync(addon, CancellationToken.None));
        // Bảng cùng tên ở public không làm CREATE TABLE trong addon lỗi → không báo "đã có".
        var existing = await PgCatalog.FindExistingAsync(addon, Precheck.CreatedNames(files), CancellationToken.None);
        Assert.DoesNotContain(existing, e => e.Name == "app_order_all");

        var first = await PgRunner.RunAsync(addon, files, null, false, new Collect(), CancellationToken.None);
        Assert.True(first.Success, first.Error);
        Assert.True(await ExistsAsync("addon.app_order_all"));
        Assert.True(await ExistsAsync("addon.app_order_$s"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM public.app_history_table")); // INSERT vào bảng nền ở public

        // Chạy lại xoá-rồi-tạo trong addon: bảng public.app_order_all (có dữ liệu) vẫn nguyên.
        var drop = DropScript.Build(files, addon.SchemaOrNull);
        var rows = await PgCatalog.CountRowsAsync(addon, drop.Tables, 1000, CancellationToken.None);
        Assert.Equal(0L, rows["app_order_all"]); // đếm bảng của addon, không phải public
        var again = await PgRunner.RunAsync(addon, files, drop, false, new Collect(), CancellationToken.None);
        Assert.True(again.Success, again.Error);
        Assert.Equal(42, await ScalarAsync("SELECT x FROM public.app_order_all"));
    }

    [Fact]
    public async Task Objects_already_in_public_do_not_warn_when_running_into_another_schema()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        await ExecAsync(SampleScripts.BaseObjects + "\nCREATE SCHEMA addon;");
        using var folder = new TempFolder();
        var files = SampleInOrder(folder);
        var (inPublic, _) = await RunAsync(files, dryRun: false);
        Assert.True(inPublic.Success, inPublic.Error);
        var addon = _profile.Clone("addon");
        addon.Schema = "addon";

        var existing = await PgCatalog.FindExistingAsync(addon, Precheck.CreatedNames(files), CancellationToken.None);
        var created = Precheck.CreatedRelations(files);
        var warnings = files.SelectMany(f => Precheck.Compare(f, existing, false, created)).Where(i => i.Level == IssueLevel.Warning).ToList();
        var outcome = await PgRunner.RunAsync(addon, files, null, true, new Collect(), CancellationToken.None);

        Assert.Empty(warnings);
        Assert.True(outcome.Success, outcome.Error); // và chạy thật được (khớp với kết quả Kiểm tra)
    }

    [Fact]
    public async Task Schema_without_public_cannot_see_base_objects_and_missing_schema_is_detected()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        await ExecAsync(SampleScripts.BaseObjects + "\nCREATE SCHEMA addon;");
        using var folder = new TempFolder();
        var files = SampleInOrder(folder);
        var only = _profile.Clone("only");
        only.Schema = "addon";
        only.IncludePublic = false;
        var missing = _profile.Clone("missing");
        missing.Schema = "khong_co";

        var outcome = await PgRunner.RunAsync(only, files, null, true, new Collect(), CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Contains("app_context", outcome.Error); // view dùng bảng nền ở public
        Assert.False(await PgCatalog.SchemaExistsAsync(missing, CancellationToken.None));
        Assert.True(await PgCatalog.SchemaExistsAsync(_profile, CancellationToken.None)); // không chọn schema = luôn được
    }

    [Fact]
    public async Task Error_line_points_inside_multi_line_statement()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var file = SqlScriptFile.Parse(System.Text.Encoding.UTF8.GetBytes("\\encoding UTF-8\n\nCREATE TABLE ok1 (a int);\n\nSELECT a,\n       b_khong_co\nFROM ok1;\n"), "a.sql", "a.sql");

        var (outcome, events) = await RunAsync([file], dryRun: false);

        Assert.False(outcome.Success);
        Assert.Equal(6, Assert.Single(events.Events.OfType<FileFailed>()).Line);
        Assert.False(await ExistsAsync("ok1"));
    }

    [Fact]
    public async Task Cancel_rolls_back()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var file = SqlScriptFile.Parse(System.Text.Encoding.UTF8.GetBytes("CREATE TABLE c1 (a int);\nSELECT pg_sleep(30);"), "a.sql", "a.sql");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var outcome = await PgRunner.RunAsync(_profile, [file], null, false, new Collect(), cts.Token);

        Assert.False(outcome.Success);
        Assert.Equal("Đã huỷ", outcome.Error);
        Assert.False(await ExistsAsync("c1"));
    }

    [Fact]
    public async Task Test_connection_reports_version_and_bad_password()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");

        var ok = await Pg.TestAsync(_profile, CancellationToken.None);
        var bad = _profile.Clone("bad");
        bad.Password = "sai-mat-khau";
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Pg.TestAsync(bad, CancellationToken.None));

        Assert.StartsWith("PostgreSQL ", ok);
        Assert.Contains("28P01", Pg.Describe(ex)); // invalid_password
    }
}
