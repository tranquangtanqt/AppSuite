using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>
/// Sao lưu / Khôi phục chạy thật: cần DBTOOLS_TEST_PG (như <see cref="PgIntegrationTests"/>) và PostgreSQL client (pg_dump.exe)
/// trên máy. Mỗi test dùng database tạm dbtools_bk_* rồi xoá.
/// </summary>
public sealed class PgBackupIntegrationTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");
    private readonly string _prefix = "dbtools_bk_" + Guid.NewGuid().ToString("N")[..10];
    private readonly List<string> _databases = [];
    private ConnectionProfile _profile = new();
    private PgClient? _client;

    private string Db(string suffix)
    {
        var name = $"{_prefix}_{suffix}";
        _databases.Add(name);
        return name;
    }

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        var b = new NpgsqlConnectionStringBuilder(Server);
        _profile = new ConnectionProfile { Name = "test", Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = Db("src") };
        await AdminAsync($"CREATE DATABASE {_profile.Database}");
        _client = PgVersion.Choose(await PgTools.FindAsync());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var db in _databases)
        {
            await AdminAsync($"DROP DATABASE IF EXISTS {db} WITH (FORCE)");
        }
    }

    private void SkipIfNoServer()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        Assert.SkipWhen(_client is null, "Máy không có pg_dump.exe.");
    }

    private static async Task AdminAsync(string sql)
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        var b = new NpgsqlConnectionStringBuilder(Server) { Database = "postgres", Pooling = false };
        await using var c = new NpgsqlConnection(b.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ExecAsync(string database, string sql)
    {
        await using var c = await Pg.OpenAsync(PgAdmin.On(_profile, database), CancellationToken.None);
        foreach (var s in SqlSplitter.Split(sql).Statements)
        {
            await using var cmd = new NpgsqlCommand(s.Text, c);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<object?> ScalarAsync(string database, string sql)
    {
        await using var c = await Pg.OpenAsync(PgAdmin.On(_profile, database), CancellationToken.None);
        await using var cmd = new NpgsqlCommand(sql, c);
        return await cmd.ExecuteScalarAsync();
    }

    private const string Seed = """
        CREATE TABLE cust (id int PRIMARY KEY, name text);
        INSERT INTO cust SELECT g, 'khách ' || g FROM generate_series(1, 250) g;
        CREATE FUNCTION cust_count() RETURNS bigint AS $$ SELECT count(*) FROM cust $$ LANGUAGE sql;
        CREATE VIEW cust_v AS SELECT id FROM cust WHERE id < 10;
        """;

    private async Task<(int Code, string Output)> ToolAsync(string tool, List<string> args)
    {
        var output = new System.Text.StringBuilder();
        var code = await PgTools.RunAsync(_client!.Exe(tool), args, _profile.Password, l => output.AppendLine(l), CancellationToken.None);
        return (code, output.ToString());
    }

    [Fact]
    public async Task Custom_dump_restores_into_a_new_database()
    {
        SkipIfNoServer();
        await ExecAsync(_profile.Database, Seed);
        using var folder = new TempFolder();
        var file = Path.Combine(folder.Path, "src.dump");

        var (code, output) = await ToolAsync("pg_dump", PgArgs.Dump(PgAdmin.Target(_profile), new DumpOptions(DumpFormat.Custom), file));
        Assert.True(code == 0, output);
        Assert.DoesNotContain(_profile.Password.Length > 0 ? _profile.Password : "\u0000", output);

        var info = await PgTools.InspectAsync(_client, file, CancellationToken.None);
        Assert.Equal(_profile.Database, info.DbName);
        Assert.Equal(1, info.Count("TABLE"));
        Assert.Equal(1, info.Count("TABLE DATA"));
        Assert.Equal(1, info.Count("VIEW"));
        Assert.Equal(1, info.Count("FUNCTION"));

        var target = Db("new");
        Assert.False(await PgAdmin.DatabaseExistsAsync(_profile, target, CancellationToken.None));
        await PgAdmin.CreateDatabaseAsync(_profile, target, CancellationToken.None);
        Assert.True(await PgAdmin.DatabaseExistsAsync(_profile, target, CancellationToken.None));
        (code, output) = await ToolAsync("pg_restore", PgArgs.Restore(PgAdmin.Target(_profile, target), new RestoreOptions(NoOwner: true), file));
        Assert.True(code == 0, output);
        Assert.Equal(250L, await ScalarAsync(target, "SELECT cust_count()"));
        Assert.Equal(9L, await ScalarAsync(target, "SELECT count(*) FROM cust_v"));
        Assert.Contains(target, await PgAdmin.ListDatabasesAsync(_profile, CancellationToken.None));
    }

    [Fact]
    public async Task Schema_only_plain_dump_runs_with_psql()
    {
        SkipIfNoServer();
        await ExecAsync(_profile.Database, Seed);
        using var folder = new TempFolder();
        var file = Path.Combine(folder.Path, "src.sql");
        var (code, output) = await ToolAsync("pg_dump", PgArgs.Dump(PgAdmin.Target(_profile), new DumpOptions(DumpFormat.Plain, SchemaOnly: true), file));
        Assert.True(code == 0, output);
        var info = await PgTools.InspectAsync(_client, file, CancellationToken.None);
        Assert.True(info.IsPlainSql);
        Assert.NotEmpty(info.ServerVersion);

        var target = Db("plain");
        await PgAdmin.CreateDatabaseAsync(_profile, target, CancellationToken.None);
        (code, output) = await ToolAsync("psql", PgArgs.Psql(PgAdmin.Target(_profile, target), singleTransaction: true, file));
        Assert.True(code == 0, output);
        Assert.Equal(0L, await ScalarAsync(target, "SELECT count(*) FROM cust")); // chỉ cấu trúc
    }

    [Fact]
    public async Task Single_transaction_restore_into_existing_db_rolls_back_on_conflict()
    {
        SkipIfNoServer();
        await ExecAsync(_profile.Database, Seed);
        using var folder = new TempFolder();
        var file = Path.Combine(folder.Path, "src.dump");
        Assert.Equal(0, (await ToolAsync("pg_dump", PgArgs.Dump(PgAdmin.Target(_profile), new DumpOptions(DumpFormat.Custom), file))).Code);

        // DB đích có sẵn bảng cust (khác cấu trúc) → không --clean thì "already exists" → rollback hết, không có view / hàm mới
        var target = Db("exist");
        await PgAdmin.CreateDatabaseAsync(_profile, target, CancellationToken.None);
        await ExecAsync(target, "CREATE TABLE cust (other int);");
        var (code, _) = await ToolAsync("pg_restore", PgArgs.Restore(PgAdmin.Target(_profile, target), new RestoreOptions(), file));
        Assert.NotEqual(0, code);
        Assert.False((bool)(await ScalarAsync(target, "SELECT to_regclass('cust_v') IS NOT NULL"))!);

        // --clean --if-exists: xoá bảng trùng tên rồi tạo lại từ file dump
        (code, var output) = await ToolAsync("pg_restore", PgArgs.Restore(PgAdmin.Target(_profile, target), new RestoreOptions(Clean: true), file));
        Assert.True(code == 0, output);
        Assert.Equal(250L, await ScalarAsync(target, "SELECT count(*) FROM cust"));
    }

    [Fact]
    public async Task Template_copy_needs_sessions_terminated()
    {
        SkipIfNoServer();
        await ExecAsync(_profile.Database, Seed);
        var target = Db("copy");
        await using (var holder = await Pg.OpenAsync(_profile, CancellationToken.None))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => PgAdmin.CopyDatabaseAsync(_profile, _profile.Database, target, CancellationToken.None));
            Assert.Equal(PostgresErrorCodes.ObjectInUse, ex.SqlState);

            var sessions = await PgAdmin.ListSessionsAsync(_profile, _profile.Database, CancellationToken.None);
            Assert.Contains(sessions, s => s.Application == "AppSuite DbTools" && !s.IsSelf);
            Assert.True(await PgAdmin.TerminateDatabaseAsync(_profile, _profile.Database, CancellationToken.None) >= 1);
        }
        await Task.Delay(300);
        Assert.Empty(await PgAdmin.ListSessionsAsync(_profile, _profile.Database, CancellationToken.None));
        await PgAdmin.CopyDatabaseAsync(_profile, _profile.Database, target, CancellationToken.None);
        Assert.Equal(250L, await ScalarAsync(target, "SELECT count(*) FROM cust"));
        Assert.True(PgAdmin.SameServer(_profile, PgAdmin.On(_profile, target)));
        Assert.True(await PgAdmin.ServerMajorAsync(_profile, CancellationToken.None) >= 12);
    }

    [Fact]
    public async Task Backup_before_run_saves_what_the_folder_will_change()
    {
        SkipIfNoServer();
        await ExecAsync(_profile.Database, SampleScripts.BaseObjects);
        using var scripts = new TempFolder();
        SampleScripts.WriteTo(scripts);
        var files = RunOrder.AutoSort(SampleScripts.Load(scripts)).Files;
        using var dumps = new TempFolder();
        var log = new List<string>();

        // DB trống (chỉ có bảng nền, chưa có dòng): vẫn có bảng nền bị DELETE / INSERT → sao lưu bảng đó
        var first = await PreRunBackup.RunAsync(_profile, _client, BackupPlan.Build(files, dropFirst: false), dumps.Path, scripts.Path, log.Add, CancellationToken.None);
        Assert.False(first.Skipped);
        Assert.Equal(1, first.DataTables);
        Assert.Equal(0, first.Functions);

        var outcome = await PgRunner.RunAsync(_profile, files, null, dryRun: false, new Progress<RunEvent>(), CancellationToken.None);
        Assert.True(outcome.Success, outcome.Error);
        await ExecAsync(_profile.Database, "INSERT INTO app_order_all (order_no) VALUES ('A1'), ('A2');");

        // Chạy lại có Xoá rồi tạo: bảng của thư mục (có dữ liệu) + hàm + trigger đều được sao lưu
        var result = await PreRunBackup.RunAsync(_profile, _client, BackupPlan.Build(files, dropFirst: true), dumps.Path, scripts.Path, log.Add, CancellationToken.None);
        Assert.False(result.Skipped);
        Assert.StartsWith(Path.Combine(dumps.Path, PreRunBackup.SubFolder), result.Folder);
        Assert.True(result.DataTables >= 3, result.Describe()); // app_order_all, app_order_all_r, app_history_table
        Assert.True(result.Functions >= 2);
        Assert.True(result.Triggers >= 2);
        foreach (var name in new[] { "bang-du-lieu.dump", "cau-truc.dump", "ham-trigger.sql", "doc-ky.txt" })
        {
            Assert.True(File.Exists(Path.Combine(result.Folder, name)), name);
        }
        var defs = await File.ReadAllTextAsync(Path.Combine(result.Folder, "ham-trigger.sql"));
        Assert.Contains("CREATE OR REPLACE FUNCTION public.app_order_all_tr_k_body()", defs);
        Assert.Contains("CREATE TRIGGER app_order_all_tr_r", defs);

        var info = await PgTools.InspectAsync(_client, Path.Combine(result.Folder, "bang-du-lieu.dump"), CancellationToken.None);
        Assert.True(info.Count("TABLE DATA") >= 3);

        // Bản sao lưu dùng được: xoá bảng rồi restore lại có đủ 2 dòng
        await ExecAsync(_profile.Database, "DROP TABLE app_order_all CASCADE;");
        var (code, output) = await ToolAsync("pg_restore",
            [.. PgArgs.Connection(PgAdmin.Target(_profile)), "-t", "app_order_all", Path.Combine(result.Folder, "bang-du-lieu.dump")]);
        Assert.True(code == 0, output);
        Assert.Equal(2L, await ScalarAsync(_profile.Database, "SELECT count(*) FROM app_order_all"));

        // Chạy hàm-trigger.sql trả lại định nghĩa cũ
        await ExecAsync(_profile.Database, "CREATE OR REPLACE FUNCTION app_order_all_tr_k_body() RETURNS trigger AS $$ BEGIN RETURN NULL; END $$ LANGUAGE plpgsql;");
        await ExecAsync(_profile.Database, defs);
        Assert.Contains("C01", (string)(await ScalarAsync(_profile.Database, "SELECT prosrc FROM pg_proc WHERE proname = 'app_order_all_tr_k_body'"))!);
    }

    [Fact]
    public async Task Backup_is_skipped_when_folder_only_creates_new_objects()
    {
        SkipIfNoServer();
        var file = SqlScriptFile.Parse("CREATE TABLE brand_new (id int);\nCREATE OR REPLACE FUNCTION brand_new_f() RETURNS int AS $$ SELECT 1 $$ LANGUAGE sql;"u8.ToArray(),
            @"C:\x\n.sql", "n.sql");
        using var dumps = new TempFolder();
        var result = await PreRunBackup.RunAsync(_profile, _client, BackupPlan.Build([file], dropFirst: false), dumps.Path, "x", _ => { }, CancellationToken.None);
        Assert.True(result.Skipped);
        Assert.False(Directory.Exists(Path.Combine(dumps.Path, PreRunBackup.SubFolder)));
    }
}
