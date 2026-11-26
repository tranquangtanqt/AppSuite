using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>Trang Cấu hình server: kiểm giá trị, câu ALTER SYSTEM, lọc, nhóm, lưu ý.</summary>
public class ServerSettingsTests
{
    internal static PgSetting S(string name, string type, string value = "", string unit = "", string min = "", string max = "", string[]? enums = null,
        string context = "sighup", string source = "default", string file = "", string category = "Reporting and Logging / What to Log", bool pending = false) =>
        new(name, value, value, unit, category, "mô tả " + name, "", context, type, source, file, null, "", min, max, enums ?? [], pending);

    [Theory]
    [InlineData("on", "on")]
    [InlineData(" TRUE ", "on")]
    [InlineData("0", "off")]
    [InlineData("tắt", "off")]
    public void Bool_values(string input, string expected) => Assert.Equal((expected, (string?)null), ServerSettings.Normalize(S("log_connections", "bool"), input));

    [Fact]
    public void Validates_by_type()
    {
        Assert.NotNull(ServerSettings.Normalize(S("log_connections", "bool"), "maybe").Error);

        var e = S("log_statement", "enum", enums: ["none", "ddl", "mod", "all"]);
        Assert.Equal("all", ServerSettings.Normalize(e, "ALL").Value);
        Assert.NotNull(ServerSettings.Normalize(e, "all'; DROP").Error);

        var i = S("log_min_duration_statement", "integer", unit: "ms", min: "-1", max: "2147483647");
        Assert.Equal("500", ServerSettings.Normalize(i, " 500 ").Value);
        Assert.Equal("2s", ServerSettings.Normalize(i, "2 s").Value);
        Assert.NotNull(ServerSettings.Normalize(i, "-2").Error);
        Assert.NotNull(ServerSettings.Normalize(i, "1.5").Error);
        Assert.NotNull(ServerSettings.Normalize(i, "10 giờ").Error);

        var m = S("work_mem", "integer", unit: "kB", min: "64", max: "2147483647");
        Assert.Equal("64MB", ServerSettings.Normalize(m, "64MB").Value);
        Assert.NotNull(ServerSettings.Normalize(m, "10").Error);

        var r = S("random_page_cost", "real", min: "0", max: "1.79769e+308");
        Assert.Equal("1.1", ServerSettings.Normalize(r, "1.1").Value);

        Assert.Equal("%m [%p]", ServerSettings.Normalize(S("log_line_prefix", "string"), "%m [%p]").Value);
        Assert.NotNull(ServerSettings.Normalize(S("log_line_prefix", "string"), "a\nb").Error);
        Assert.NotNull(ServerSettings.Normalize(S("block_size", "integer", context: "internal"), "8192").Error);
    }

    [Fact]
    public void Statements_quote_values_and_lists()
    {
        Assert.Equal("ALTER SYSTEM SET log_statement = 'all'", ServerSettings.Statement(new(S("log_statement", "enum"), "all")));
        Assert.Equal("ALTER SYSTEM SET log_line_prefix = '%m ''x'' '", ServerSettings.Statement(new(S("log_line_prefix", "string"), "%m 'x' ")));
        Assert.Equal("ALTER SYSTEM SET search_path = '\"$user\"', 'public'", ServerSettings.Statement(new(S("search_path", "string"), "\"$user\", public")));
        Assert.Equal("ALTER SYSTEM SET log_destination = 'stderr,csvlog'", ServerSettings.Statement(new(S("log_destination", "string"), "stderr,csvlog")));
        Assert.Equal("ALTER SYSTEM RESET pg_stat_statements.max", ServerSettings.Statement(new(S("pg_stat_statements.max", "integer"), null)));
        Assert.Throws<ArgumentException>(() => ServerSettings.Statement(new(S("x; DROP TABLE t", "string"), "1")));
    }

    [Fact]
    public void Flags_groups_and_source_text()
    {
        var auto = S("log_statement", "enum", source: "configuration file", file: "/data/postgresql.auto.conf");
        Assert.True(auto.ByAlterSystem);
        Assert.True(auto.Changed);
        Assert.Equal("ALTER SYSTEM (postgresql.auto.conf)", auto.SourceText);
        var conf = S("port", "integer", source: "configuration file", file: @"C:\pg\data\postgresql.conf", context: "postmaster", category: "Connections and Authentication / Connection Settings")
            with { SourceLine = 64 };
        Assert.Equal("postgresql.conf dòng 64", conf.SourceText);
        Assert.True(conf.NeedsRestart);
        Assert.Equal("Kết nối và xác thực", conf.Group);
        Assert.False(S("x", "bool").Changed);
        Assert.Equal("Lạ", ServerSettings.GroupName("Lạ"));
        Assert.True(ServerSettings.GroupOrder("Reporting and Logging") < ServerSettings.GroupOrder("Autovacuum"));
        Assert.Equal(int.MaxValue, ServerSettings.GroupOrder("Lạ"));
        Assert.Equal("-1 - 2147483647 (đơn vị ms)", S("a", "integer", unit: "ms", min: "-1", max: "2147483647").RangeText);
        Assert.Equal("none, all", S("a", "enum", enums: ["none", "all"]).RangeText);
    }

    [Fact]
    public void Filter_combines_text_group_and_flags()
    {
        var all = new[]
        {
            S("log_statement", "enum", source: "configuration file", file: "/d/postgresql.auto.conf"),
            S("log_connections", "bool"),
            S("shared_buffers", "integer", context: "postmaster", category: "Resource Usage / Memory", pending: true),
            S("work_mem", "integer", category: "Resource Usage / Memory"),
            S("autovacuum", "bool", category: "Autovacuum"),
        };
        Assert.Equal(["log_connections", "log_statement", "shared_buffers", "work_mem", "autovacuum"],
            ServerSettings.Filter(all, null, null, false, false, false).Select(s => s.Name));
        Assert.Equal(["shared_buffers", "work_mem"], ServerSettings.Filter(all, "mem", null, false, false, false).Select(s => s.Name));
        Assert.Equal(["work_mem"], ServerSettings.Filter(all, "work mô", null, false, false, false).Select(s => s.Name));
        Assert.Equal(["autovacuum"], ServerSettings.Filter(all, null, "Autovacuum", false, false, false).Select(s => s.Name));
        Assert.Equal(["log_statement"], ServerSettings.Filter(all, null, null, true, false, false).Select(s => s.Name));
        Assert.Equal(["log_statement"], ServerSettings.Filter(all, null, null, false, true, false).Select(s => s.Name));
        Assert.Equal(["shared_buffers"], ServerSettings.Filter(all, null, null, false, false, true).Select(s => s.Name));
        Assert.Equal(["work_mem"], ServerSettings.Filter(all, null, null, false, false, false, new HashSet<string> { "work_mem" }, true).Select(s => s.Name));
        Assert.Equal(["shared_buffers", "work_mem"], ServerSettings.Filter(all, "tài nguyên", null, false, false, false).Select(s => s.Name)); // tên nhóm tiếng Việt
    }

    [Fact]
    public void Warnings_and_overrides()
    {
        var changes = new[]
        {
            new SettingChange(S("shared_buffers", "integer", context: "postmaster"), "256MB"),
            new SettingChange(S("log_statement", "enum"), "all"),
        };
        var overrides = new[] { new SettingOverride("log_statement", "ddl", "app_db", ""), new SettingOverride("log_statement", "none", "", "batch") };
        var w = ServerSettings.Warnings(changes, overrides);
        Assert.Equal(3, w.Count);
        Assert.Contains("KHỞI ĐỘNG LẠI", w[0]);
        Assert.Contains("database app_db; user batch", w[1]);
        Assert.Contains("Ghi mọi câu", w[2]);
        Assert.Equal("user batch trong database app_db", new SettingOverride("x", "1", "app_db", "batch").Where);
        Assert.Equal("shared_buffers:  → về mặc định (bỏ dòng ALTER SYSTEM)", new SettingChange(S("shared_buffers", "integer"), null).Describe());
    }

    [Fact]
    public void Production_must_type_database_name()
    {
        Assert.Null(ProductionGuard.ForServerConfig(false, "db", "p"));
        Assert.Equal("db", ProductionGuard.ForServerConfig(true, "db", "p"));
    }
}

/// <summary>Đọc / ALTER SYSTEM thật trên server thử (cần DBTOOLS_TEST_PG, superuser). Cuối test RESET lại như cũ.</summary>
public sealed class PgSettingsIntegrationTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");

    private static ConnectionProfile Profile()
    {
        var b = new NpgsqlConnectionStringBuilder(Server);
        return new ConnectionProfile { Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = "postgres" };
    }

    [Fact]
    public async Task Reads_applies_reloads_and_resets()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var p = Profile();
        var ct = TestContext.Current.CancellationToken;
        var before = await PgSettings.LoadAsync(p, ct);
        Assert.True(before.Superuser);
        Assert.True(before.Settings.Count > 200);
        var byName = before.Settings.ToDictionary(s => s.Name);
        Assert.Equal("enum", byName["log_statement"].VarType);
        Assert.Contains("all", byName["log_statement"].EnumValues);
        Assert.True(byName["shared_buffers"].NeedsRestart);
        Assert.EndsWith("MB", byName["shared_buffers"].Value); // current_setting: dạng dễ đọc
        Assert.True(byName["server_version"].ReadOnly);

        var changes = new List<SettingChange>
        {
            new(byName["log_min_duration_statement"], ServerSettings.Normalize(byName["log_min_duration_statement"], "2s").Value),
            // Thông số vô hại (test khác chạy song song đọc log của cùng server - không đụng log_line_prefix / log_statement).
            new(byName["default_text_search_config"], "pg_catalog.simple"),
            new(byName["work_mem"], "8MB"),
            new(byName["max_connections"], byName["max_connections"].Value == "120" ? "121" : "120"),
            new(byName["log_statement"], "khong_co"), // server từ chối → dừng ở đây
        };
        try
        {
            var (after, done, error) = await PgSettings.ApplyAsync(p, changes.Select(ServerSettings.Statement).ToList(), ct);
            Assert.Equal(4, done);
            Assert.IsType<PostgresException>(error);
            var a = after.Settings.ToDictionary(s => s.Name);
            Assert.Equal("2s", a["log_min_duration_statement"].Value);
            Assert.Equal("pg_catalog.simple", a["default_text_search_config"].Value);
            Assert.Equal("8MB", a["work_mem"].Value);
            Assert.True(a["work_mem"].ByAlterSystem);
            Assert.True(a["max_connections"].PendingRestart); // đã ghi, chờ khởi động lại
            Assert.Equal(byName["max_connections"].Value, a["max_connections"].Value);

            var resets = after.Settings.Where(s => s.ByAlterSystem).Select(s => ServerSettings.Statement(new SettingChange(s, null))).ToList();
            var (clean, _, resetError) = await PgSettings.ApplyAsync(p, resets, ct);
            Assert.Null(resetError);
            Assert.DoesNotContain(clean.Settings, s => s.ByAlterSystem);
            Assert.Equal(byName["work_mem"].Value, clean.Settings.Single(s => s.Name == "work_mem").Value);
            Assert.False(clean.Settings.Single(s => s.Name == "max_connections").PendingRestart);
        }
        finally
        {
            await PgSettings.ApplyAsync(p, ["ALTER SYSTEM RESET ALL"], CancellationToken.None);
        }
    }

    [Fact]
    public async Task Reads_database_and_role_overrides()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var p = Profile();
        var ct = TestContext.Current.CancellationToken;
        var role = "dbtools_cfg_" + Guid.NewGuid().ToString("N")[..8];
        await using var c = await Pg.OpenAsync(p, ct);
        async Task Exec(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await Exec($"CREATE ROLE {role}");
        try
        {
            await Exec($"ALTER ROLE {role} IN DATABASE postgres SET work_mem = '16MB'");
            var config = await PgSettings.LoadAsync(p, ct);
            var o = Assert.Single(config.Overrides, x => x.Role == role);
            Assert.Equal(("work_mem", "16MB", "postgres"), (o.Name, o.Value, o.Database));
        }
        finally
        {
            await Exec($"ALTER ROLE {role} IN DATABASE postgres RESET ALL");
            await Exec($"DROP ROLE {role}");
        }
    }
}
