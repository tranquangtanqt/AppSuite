using System.Diagnostics;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>Xem lệnh (trang Sao lưu / Khôi phục): script .bat đúng các bước, không lộ mật khẩu, dán ra chạy được.</summary>
public class CommandPreviewTests
{
    private static readonly PgClient Client = new(@"C:\Program Files\PostgreSQL\17\bin", new Version(17, 2));
    private static readonly PgTarget Target = new("localhost", 5432, "postgres", "app_db");

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("C:\\Program Files\\x.dump", "\"C:\\Program Files\\x.dump\"")]
    [InlineData("\"Addon\"", "\"\\\"Addon\\\"\"")]
    [InlineData("a b\\", "\"a b\\\\\"")]
    [InlineData("", "\"\"")]
    public void Quotes_like_dotnet_argument_list(string arg, string expected)
    {
        Assert.Equal(expected, CommandPreview.Quote(arg));
    }

    [Fact]
    public void Dump_preview_lists_version_check_then_pg_dump()
    {
        var text = CommandPreview.ForDump(Client, "dev", Target, new DumpOptions(DumpFormat.Custom), @"D:\DMP\app_db_20261119_0900.dump");
        Assert.Contains("set PGPASSWORD=<mật khẩu của hồ sơ \"dev\">", text);
        Assert.Contains("\"C:\\Program Files\\PostgreSQL\\17\\bin\\psql.exe\" -h localhost -p 5432 -U postgres -d postgres -w -At -c \"SELECT current_setting('server_version_num')::int\"", text);
        Assert.Contains("\"C:\\Program Files\\PostgreSQL\\17\\bin\\pg_dump.exe\" -h localhost -p 5432 -U postgres -d app_db -w -v -F c -f D:\\DMP\\app_db_20261119_0900.dump", text);
        Assert.True(text.IndexOf("server_version_num") < text.IndexOf("pg_dump.exe"));
    }

    [Fact]
    public void Restore_preview_creates_database_only_for_new_target()
    {
        var toNew = CommandPreview.ForRestore(Client, "dev", Target with { Database = "My DB" }, new RestoreOptions(NoOwner: true), @"D:\DMP\a.dump", plainSql: false, toNew: true);
        Assert.Contains("pg_restore.exe\" -l D:\\DMP\\a.dump", toNew);
        Assert.Contains("-c \"CREATE DATABASE \\\"My DB\\\" TEMPLATE template0\"", toNew);
        Assert.Contains("--no-owner --no-privileges --single-transaction --exit-on-error D:\\DMP\\a.dump", toNew);

        var existing = CommandPreview.ForRestore(Client, "dev", Target, new RestoreOptions(Clean: true), @"D:\DMP\a.dump", plainSql: false, toNew: false);
        Assert.DoesNotContain("CREATE DATABASE", existing);
        Assert.Contains("--clean --if-exists", existing);

        var plain = CommandPreview.ForRestore(Client, "dev", Target, new RestoreOptions(), @"D:\DMP\a.sql", plainSql: true, toNew: false);
        Assert.Contains("psql.exe\" -h localhost -p 5432 -U postgres -d app_db -w -q -v ON_ERROR_STOP=1 --single-transaction -f D:\\DMP\\a.sql", plain);
        Assert.DoesNotContain("pg_restore", plain);
    }

    [Fact]
    public void Copy_previews_show_template_or_dump_restore()
    {
        var same = CommandPreview.ForCopySameServer(Client, "dev", Target, "app_db", "app_db_copy");
        Assert.Contains("pg_terminate_backend", same);
        Assert.Contains("CREATE DATABASE \\\"app_db_copy\\\" TEMPLATE \\\"app_db\\\"", same);
        Assert.Contains("Chỉ khi bấm", same);

        var other = CommandPreview.ForCopyOtherServer(Client, "dev", Target, "test", new PgTarget("10.0.0.5", 5433, "admin", "app_db_copy"),
            noOwner: true, @"D:\DMP\copy_app_db.dump");
        Assert.Contains("set PGPASSWORD=<mật khẩu của hồ sơ \"test\">", other);
        Assert.Contains("-h 10.0.0.5 -p 5433 -U admin -d app_db_copy -w -v --no-owner", other);
        Assert.Contains("del D:\\DMP\\copy_app_db.dump", other);
        Assert.True(other.IndexOf("pg_dump.exe") < other.IndexOf("CREATE DATABASE"));
    }

    [Fact]
    public void Missing_client_still_gives_tool_names()
    {
        var text = CommandPreview.ForDump(null, "dev", Target, new DumpOptions(DumpFormat.Plain), "x.sql");
        Assert.Contains("\"pg_dump.exe\" -h", text);
        Assert.Contains("Chưa tìm thấy PostgreSQL client", text);
    }

    [Fact]
    public void Admin_sql_quotes_names()
    {
        Assert.Equal("CREATE DATABASE \"a\"\"b\" TEMPLATE template0", PgSql.CreateDatabase("a\"b"));
        Assert.Equal("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'o''k')", PgSql.DatabaseExists("o'k"));
    }

    /// <summary>Dán script ra file .bat, điền mật khẩu, chạy bằng cmd: dump rồi restore vào DB mới được thật.</summary>
    [Fact]
    public async Task Copied_script_runs_in_cmd()
    {
        var server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");
        Assert.SkipWhen(string.IsNullOrEmpty(server), "Chưa đặt DBTOOLS_TEST_PG.");
        var client = PgVersion.Choose(await PgTools.FindAsync());
        Assert.SkipWhen(client is null, "Máy không có pg_dump.exe.");
        var b = new NpgsqlConnectionStringBuilder(server);
        var source = "dbtools_cmd_" + Guid.NewGuid().ToString("N")[..8];
        var copy = source + "_r";
        var admin = new ConnectionProfile { Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = "postgres" };
        try
        {
            await PgAdmin.CreateDatabaseAsync(admin, source, CancellationToken.None);
            await using (var c = await Pg.OpenAsync(PgAdmin.On(admin, source), CancellationToken.None))
            {
                foreach (var sql in new[] { "CREATE TABLE t (id int)", "INSERT INTO t SELECT generate_series(1, 42)" })
                {
                    await using var cmd = new NpgsqlCommand(sql, c);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            using var folder = new TempFolder();
            var file = Path.Combine(folder.Path, "my dump.dump");
            var dump = CommandPreview.ForDump(client, "test", PgAdmin.Target(admin, source), new DumpOptions(DumpFormat.Custom), file);
            var restore = CommandPreview.ForRestore(client, "test", PgAdmin.Target(admin, copy), new RestoreOptions(), file, plainSql: false, toNew: true);
            foreach (var (name, script) in new[] { ("dump.bat", dump), ("restore.bat", restore) })
            {
                var bat = Path.Combine(folder.Path, name);
                await File.WriteAllTextAsync(bat, script.Replace("<mật khẩu của hồ sơ \"test\">", admin.Password), new System.Text.UTF8Encoding(false));
                using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })!;
                var err = p.StandardError.ReadToEndAsync();
                var output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                Assert.True(p.ExitCode == 0, $"{name}: {output} {await err}");
            }
            Assert.True(File.Exists(file));
            await using var check = await Pg.OpenAsync(PgAdmin.On(admin, copy), CancellationToken.None);
            await using var count = new NpgsqlCommand("SELECT count(*) FROM t", check);
            Assert.Equal(42L, await count.ExecuteScalarAsync());
        }
        finally
        {
            await using var c = await Pg.OpenAsync(admin, CancellationToken.None);
            foreach (var db in new[] { source, copy })
            {
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {db} WITH (FORCE)", c);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
