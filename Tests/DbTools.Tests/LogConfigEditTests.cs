using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>Nhật ký server - Đổi cấu hình log: câu ALTER SYSTEM, mô tả thay đổi, lưu ý, Về mặc định.</summary>
public class LogConfigEditTests
{
    private static Dictionary<string, LogSetting> Settings(params (string Name, string Value, string File)[] items) =>
        items.ToDictionary(i => i.Name, i => new LogSetting(i.Name, i.Value, i.File.Length > 0 ? "configuration file" : "default", i.File, ""));

    [Fact]
    public void Reads_current_values()
    {
        var v = LogConfigValues.From(Settings(("log_statement", "mod", ""), ("log_min_duration_statement", "250", ""),
            ("log_connections", "on", ""), ("log_disconnections", "off", "")));
        Assert.Equal(new LogConfigValues("mod", 250, true, false), v);
        Assert.Equal(new LogConfigValues("none", -1, false, false), LogConfigValues.From(new Dictionary<string, LogSetting>()));
    }

    [Fact]
    public void Only_changed_settings_are_altered()
    {
        var current = new LogConfigValues("none", -1, false, false);
        Assert.Empty(LogConfigEdit.AlterStatements(current, current));
        Assert.Equal(
        [
            "ALTER SYSTEM SET log_statement = 'all'",
            "ALTER SYSTEM SET log_min_duration_statement = 0",
            "ALTER SYSTEM SET log_disconnections = on",
        ], LogConfigEdit.AlterStatements(current, new LogConfigValues("all", 0, false, true)));
        Assert.Equal(["ALTER SYSTEM SET log_connections = on"], LogConfigEdit.AlterStatements(current, current with { Connections = true }));

        var lines = LogConfigEdit.DescribeChanges(current, new LogConfigValues("ddl", 1500, true, false));
        Assert.Equal("log_statement: none → ddl (chỉ DDL: CREATE / ALTER / DROP)", lines[0]);
        Assert.Equal("log_min_duration_statement: -1 → 1500 (câu chạy lâu hơn 1,500 ms)", lines[1]);
        Assert.Equal("log_connections: off → on", lines[2]);
    }

    [Fact]
    public void Validates_values()
    {
        Assert.Null(LogConfigEdit.Validate(new LogConfigValues("all", -1, false, false)));
        Assert.NotNull(LogConfigEdit.Validate(new LogConfigValues("ALL'; DROP", -1, false, false)));
        Assert.NotNull(LogConfigEdit.Validate(new LogConfigValues("all", -2, false, false)));
    }

    [Fact]
    public void Reset_only_touches_alter_system_lines()
    {
        var s = Settings(("log_statement", "all", "/data/postgresql.auto.conf"), ("log_connections", "on", "/data/postgresql.conf"),
            ("log_disconnections", "off", ""), ("log_destination", "stderr", "/data/postgresql.auto.conf"));
        Assert.Equal(["ALTER SYSTEM RESET log_statement"], LogConfigEdit.ResetStatements(s.Values));
        Assert.True(s["log_statement"].ByAlterSystem);
        Assert.False(s["log_connections"].ByAlterSystem);
    }

    [Fact]
    public void Warns_about_logging_everything_and_overrides()
    {
        var plain = Settings(("log_statement", "none", ""));
        Assert.Empty(LogConfigEdit.Warnings(new LogConfigValues("mod", 500, false, false), plain.Values));
        Assert.Single(LogConfigEdit.Warnings(new LogConfigValues("all", -1, false, false), plain.Values));
        Assert.Single(LogConfigEdit.Warnings(new LogConfigValues("none", 0, false, false), plain.Values));
        var perDb = new[] { new LogSetting("log_statement", "ddl", "database", "", "none") };
        Assert.Contains(LogConfigEdit.Warnings(new LogConfigValues("mod", -1, false, false), perDb), w => w.Contains("database"));
    }

    [Fact]
    public void Production_must_type_database_name()
    {
        Assert.Null(ProductionGuard.ForLogConfig(false, "db", "p"));
        Assert.Equal("db", ProductionGuard.ForLogConfig(true, "db", "p"));
        Assert.Equal("p", ProductionGuard.ForLogConfig(true, "", "p"));
    }
}

/// <summary>ALTER SYSTEM thật trên server thử (cần DBTOOLS_TEST_PG, user superuser) - cuối test RESET lại như cũ.</summary>
public sealed class LogConfigIntegrationTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");

    private static ConnectionProfile Profile()
    {
        var b = new NpgsqlConnectionStringBuilder(Server);
        return new ConnectionProfile { Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = "postgres" };
    }

    [Fact]
    public async Task Apply_reloads_and_reset_restores()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var p = Profile();
        var ct = TestContext.Current.CancellationToken;
        var before = await PgServerLog.EditableSettingsAsync(p, ct);
        var original = LogConfigValues.From(before);
        var wanted = new LogConfigValues(original.Statement == "ddl" ? "mod" : "ddl", original.MinDurationMs == 1234 ? 4321 : 1234,
            !original.Connections, !original.Disconnections);
        try
        {
            var after = await PgServerLog.ApplyAsync(p, LogConfigEdit.AlterStatements(original, wanted), ct);
            Assert.Equal(wanted, LogConfigValues.From(after));
            Assert.All(LogConfigEdit.Names, n => Assert.True(after[n].ByAlterSystem, n));

            var reset = await PgServerLog.ApplyAsync(p, LogConfigEdit.ResetStatements(after.Values), ct);
            Assert.All(LogConfigEdit.Names, n => Assert.False(reset[n].ByAlterSystem, n));
            Assert.Equal(original, LogConfigValues.From(await PgServerLog.EditableSettingsAsync(p, ct)));
        }
        finally
        {
            // Lỗi giữa chừng vẫn trả server thử về như cũ.
            await PgServerLog.ApplyAsync(p, LogConfigEdit.Names.Select(n => "ALTER SYSTEM RESET " + n).ToList(), CancellationToken.None);
        }
    }
}
