using DbTools.Core;
using DbTools.Models;
using DbTools.Services;

namespace DbTools.Tests;

public class StoreTests
{
    [Fact]
    public void Connection_password_is_encrypted_on_disk_and_restored()
    {
        using var folder = new TempFolder();
        var store = new ConnectionStore(Path.Combine(folder.Path, "connections.json"));
        var profile = new ConnectionProfile { Name = "dev", Host = "localhost", Port = 1209, Database = "db1", Username = "postgres", Password = "Bí-mật@123" };

        store.Save([profile]);
        var json = File.ReadAllText(store.FilePath);
        var loaded = Assert.Single(store.Load());

        Assert.DoesNotContain("Bí-mật@123", json);
        Assert.DoesNotContain("\"Password\"", json);
        Assert.Equal("Bí-mật@123", loaded.Password);
        Assert.Equal(1209, loaded.Port);
        Assert.Equal("localhost:1209/db1", loaded.Target);
    }

    [Fact]
    public void Schema_choice_is_saved_per_profile_and_builds_search_path()
    {
        using var folder = new TempFolder();
        var store = new ConnectionStore(Path.Combine(folder.Path, "connections.json"));
        var withSchema = new ConnectionProfile { Name = "a", Schema = " addon ", IncludePublic = false };
        var plain = new ConnectionProfile { Name = "b" };
        var quoted = new ConnectionProfile { Name = "c", Schema = "My\"Schema", IncludePublic = true };

        store.Save([withSchema, plain, quoted]);
        var loaded = store.Load();

        Assert.Equal(" addon ", loaded[0].Schema);
        Assert.False(loaded[0].IncludePublic);
        Assert.True(loaded[1].IncludePublic); // mặc định bật
        Assert.Equal("SET search_path TO \"addon\"", Pg.SearchPathSql(loaded[0]));
        Assert.Null(Pg.SearchPathSql(loaded[1]));
        Assert.Equal("SET search_path TO \"My\"\"Schema\", public", Pg.SearchPathSql(loaded[2]));
        Assert.Equal("SET search_path TO \"public\"", Pg.SearchPathSql(new ConnectionProfile { Schema = "public" }));
        Assert.Equal("schema addon", loaded[0].SchemaText);
        Assert.Equal("schema My\"Schema + public", loaded[2].SchemaText);
        Assert.StartsWith("schema mặc định", loaded[1].SchemaText);
    }

    [Fact]
    public void Production_flag_is_saved_cloned_and_marked_red_in_pickers()
    {
        using var folder = new TempFolder();
        var store = new ConnectionStore(Path.Combine(folder.Path, "connections.json"));
        var prod = new ConnectionProfile { Name = "prod", Host = "10.0.0.5", Database = "mcf" , IsProduction = true };
        var dev = new ConnectionProfile { Name = "dev", Database = "mcf" };

        store.Save([prod, dev]);
        var loaded = store.Load();

        Assert.True(loaded[0].IsProduction);
        Assert.False(loaded[1].IsProduction); // file cũ không có trường này → hồ sơ thường
        Assert.True(loaded[0].Clone("prod 2").IsProduction); // nhân bản giữ dấu Production
        Assert.StartsWith("🔴 PRODUCTION · prod", loaded[0].ToString());
        Assert.Equal(loaded[1].Display, loaded[1].ToString());
    }

    [Fact]
    public void Production_guard_asks_for_the_name_of_the_database_being_changed()
    {
        // hồ sơ thường: không hỏi gõ lại
        Assert.Null(ProductionGuard.ForRun(false, "mcf", "dev"));
        Assert.Null(ProductionGuard.ForRestore(false, "mcf", "dev"));
        Assert.Null(ProductionGuard.ForCopy(false, false, true, "a", "b"));
        Assert.Null(ProductionGuard.ForSessions(false, ["mcf"], "dev", "mcf"));

        Assert.Equal("mcf", ProductionGuard.ForRun(true, " mcf ", "prod"));
        Assert.Equal("prod", ProductionGuard.ForRun(true, "", "prod")); // hồ sơ chưa điền Database → gõ tên hồ sơ
        Assert.Equal("PRODUCTION", ProductionGuard.ForRun(true, "", " ")); // không bao giờ để ô trống tự khớp
        Assert.Equal("mcf_new", ProductionGuard.ForRestore(true, "mcf_new", "prod"));

        // Copy: ngắt phiên DB nguồn production → tên nguồn; ghi lên đích production → tên đích; chỉ đọc nguồn → không hỏi
        Assert.Equal("mcf", ProductionGuard.ForCopy(true, true, true, "mcf", "mcf_copy"));
        Assert.Equal("mcf_copy", ProductionGuard.ForCopy(true, true, false, "mcf", "mcf_copy"));
        Assert.Equal("mcf_copy", ProductionGuard.ForCopy(false, true, false, "mcf", "mcf_copy"));
        Assert.Null(ProductionGuard.ForCopy(true, false, false, "mcf", "mcf_copy"));

        // Ngắt phiên: cùng 1 DB → tên DB; nhiều DB → tên hồ sơ
        Assert.Equal("mcf", ProductionGuard.ForSessions(true, ["mcf", "mcf", null], "prod", "postgres"));
        Assert.Equal("prod", ProductionGuard.ForSessions(true, ["mcf", "other"], "prod", "postgres"));

        Assert.True(ProductionGuard.Matches(" mcf ", "mcf"));
        Assert.False(ProductionGuard.Matches("MCF", "mcf")); // tên DB phân biệt hoa thường
        Assert.False(ProductionGuard.Matches(null, "mcf"));
        Assert.False(ProductionGuard.Matches("", "mcf"));
    }

    [Fact]
    public void Password_from_another_account_becomes_empty()
    {
        Assert.Equal(string.Empty, ConnectionStore.Unprotect(Convert.ToBase64String([1, 2, 3])));
        Assert.Equal(string.Empty, ConnectionStore.Protect(string.Empty));
    }

    [Fact]
    public void Missing_or_broken_files_start_empty()
    {
        using var folder = new TempFolder();
        var path = folder.Write("settings.json", "{ hỏng");

        Assert.Empty(new ConnectionStore(Path.Combine(folder.Path, "none.json")).Load());
        Assert.True(new SettingsStore(path).Load().IncludeSubfolders);
    }

    [Fact]
    public void Settings_keep_saved_orders()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(Path.Combine(folder.Path, "settings.json"));
        var settings = new AppSettings { LastFolder = @"D:\x", LastProfile = "dev" };
        settings.SavedOrders[AppSettings.OrderKey(@"D:\X\")] = ["b.sql", "a.sql"];

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(["b.sql", "a.sql"], loaded.SavedOrders[AppSettings.OrderKey(@"d:\x")]);
        Assert.Equal("dev", loaded.LastProfile);
    }

    [Fact]
    public void History_is_newest_first_and_capped()
    {
        using var folder = new TempFolder();
        var store = new HistoryStore(folder.Path);
        var oldLog = store.NewLogPath(new DateTime(2026, 1, 1));
        File.WriteAllText(oldLog, "log cũ");
        store.Add(new RunRecord { Time = new DateTime(2026, 1, 1), Mode = "Chạy", LogPath = oldLog });
        for (var i = 0; i < HistoryStore.MaxRecords; i++)
        {
            store.Add(new RunRecord { Time = new DateTime(2026, 2, 1).AddMinutes(i), Mode = "Chạy thử" });
        }

        var list = store.Load();

        Assert.Equal(HistoryStore.MaxRecords, list.Count);
        Assert.Equal(new DateTime(2026, 2, 1).AddMinutes(HistoryStore.MaxRecords - 1), list[0].Time);
        Assert.False(File.Exists(oldLog)); // log của dòng bị đẩy ra cũng bị xoá
    }

    [Fact]
    public void Log_paths_do_not_collide()
    {
        using var folder = new TempFolder();
        var store = new HistoryStore(folder.Path);
        var time = new DateTime(2026, 11, 18, 10, 0, 0);
        var first = store.NewLogPath(time);
        File.WriteAllText(first, "x");

        Assert.NotEqual(first, store.NewLogPath(time));
    }
}

public class HelpContentTests
{
    private static string ViewsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Modules", "DbTools", "Views")))
        {
            dir = dir.Parent;
        }
        return dir is null ? string.Empty : Path.Combine(dir.FullName, "Modules", "DbTools", "Views");
    }

    [Fact]
    public void Every_page_has_exactly_one_help_section()
    {
        var folder = ViewsFolder();
        Assert.SkipWhen(folder.Length == 0, "Không tìm thấy mã nguồn Views (chạy từ bản publish).");
        var pages = Directory.GetFiles(folder, "*Page.xaml").Select(Path.GetFileNameWithoutExtension).ToList();

        Assert.Equal(8, pages.Count);
        foreach (var page in pages)
        {
            Assert.Single(HelpContent.Sections, s => s.PageTag == page);
        }
        Assert.All(HelpContent.Sections.Where(s => s.PageTag is not null), s => Assert.Contains(s.PageTag, pages));
    }

    [Fact]
    public void Sections_are_filled_in()
    {
        Assert.All(HelpContent.Sections, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Summary));
            Assert.NotEmpty(s.Items);
            Assert.All(s.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));
        });
        Assert.Same(HelpContent.Sections[0], HelpContent.ForPage("KhongCo"));
        Assert.Equal("RunPage", HelpContent.ForPage("RunPage").PageTag);
    }
}
