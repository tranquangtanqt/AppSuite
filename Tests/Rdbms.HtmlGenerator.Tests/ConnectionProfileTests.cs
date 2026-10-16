using Rdbms.HtmlGenerator.Models;
using Rdbms.HtmlGenerator.Services;

namespace Rdbms.HtmlGenerator.Tests;

public class ConnectionProfileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RdbmsProfiles_" + Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_folder, "config.xml");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>config.xml trước khi có hồ sơ (1 bộ thông tin mỗi nguồn ở gốc) - đúng định dạng XmlSerializer đã ghi.</summary>
    private const string OldConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <DatabaseConnectionsConfig xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <SelectedSource>SqlServer</SelectedSource>
          <Postgres><Host>pg.local</Host><Port>5433</Port><Database>orders</Database><Username>u</Username><Password>p</Password><Schema /></Postgres>
          <Oracle><Host>ora</Host><Port>1521</Port><ConnectBySid>true</ConnectBySid><ServiceName /><Sid>ORCL</Sid><Username /><Password /><Schema /></Oracle>
          <SqlServer><Host>sql01</Host><Port>1433</Port><Database>Z_ADDON</Database><UseWindowsAuthentication>false</UseWindowsAuthentication><Username>sa</Username><Password>x</Password><Schema>dbo</Schema></SqlServer>
          <Options><ConnectTimeoutSeconds>25</ConnectTimeoutSeconds><CommandTimeoutSeconds>120</CommandTimeoutSeconds><OpenHtmlAfterExport>true</OpenHtmlAfterExport><OpenFolderAfterExport>false</OpenFolderAfterExport></Options>
        </DatabaseConnectionsConfig>
        """;

    [Fact]
    public void Old_config_becomes_profile_mac_dinh_with_same_values()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(ConfigPath, OldConfig);

        var config = new ConnectionSettingsStore(ConfigPath).Load();

        var profile = Assert.Single(config.Profiles);
        Assert.Equal(ConnectionProfiles.DefaultName, profile.Name);
        Assert.Equal(profile.Name, config.SelectedProfile);
        Assert.Equal(DatabaseSourceType.SqlServer, profile.Source);
        Assert.Equal(("sql01", "Z_ADDON", "dbo"), (profile.SqlServer.Host, profile.SqlServer.Database, profile.SqlServer.Schema));
        Assert.Equal(("pg.local", 5433), (profile.Postgres.Host, profile.Postgres.Port));
        Assert.Equal("ORCL", profile.Oracle.Sid);
        Assert.Equal(25, config.Options.ConnectTimeoutSeconds);
        Assert.True(config.Options.OpenHtmlAfterExport);
        Assert.Equal("Z_ADDON", ConnectionProfiles.FileBaseName(profile)); // giữ tên file cũ → dữ liệu đã đọc vẫn dùng được
    }

    [Fact]
    public void Profiles_survive_save_and_load_and_old_root_elements_are_dropped()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(ConfigPath, OldConfig);
        var store = new ConnectionSettingsStore(ConfigPath);
        var config = store.Load();
        var prod = config.Profiles[0].Clone("prod");
        prod.SqlServer.Host = "sql-prod";
        config.Profiles.Add(prod);
        config.SelectedProfile = "prod";
        store.Save(config);

        var root = System.Xml.Linq.XDocument.Load(ConfigPath).Root!;
        Assert.DoesNotContain(root.Elements(), e => e.Name.LocalName is "Postgres" or "Oracle" or "MySql" or "SqlServer"); // bộ cũ ở gốc đã bỏ
        Assert.Equal(2, root.Element("Profiles")!.Elements().Count());
        var loaded = new ConnectionSettingsStore(ConfigPath).Load();
        Assert.Equal([ConnectionProfiles.DefaultName, "prod"], loaded.Profiles.Select(p => p.Name));
        Assert.Equal("prod", loaded.SelectedProfile);
        Assert.Equal("sql-prod", loaded.Profiles[1].SqlServer.Host);
        Assert.Equal("sql01", loaded.Profiles[0].SqlServer.Host); // bản sao độc lập, sửa prod không đụng Mặc định
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Missing_or_broken_config_gives_one_empty_profile()
    {
        var missing = new ConnectionSettingsStore(ConfigPath).Load();
        Assert.Equal(ConnectionProfiles.DefaultName, Assert.Single(missing.Profiles).Name);

        File.WriteAllText(ConfigPath, "<DatabaseConnectionsConfig><Profiles>hỏng");
        var broken = new ConnectionSettingsStore(ConfigPath).Load();
        Assert.Single(broken.Profiles);
        Assert.Equal(DatabaseSourceType.Postgres, broken.Profiles[0].Source);
    }

    [Fact]
    public void Normalize_fixes_hand_edited_config()
    {
        var config = new DatabaseConnectionsConfig
        {
            Profiles =
            [
                new ConnectionProfile { Name = " dev " },
                new ConnectionProfile { Name = "DEV" },                                // trùng (không phân biệt hoa thường)
                new ConnectionProfile { Name = "" },                                   // không tên
                new ConnectionProfile { Name = "prod", Source = (DatabaseSourceType)99 }, // nguồn lạ
            ],
            SelectedProfile = "không có",
        };
        ConnectionProfiles.Normalize(config);
        Assert.Equal(["dev", "prod"], config.Profiles.Select(p => p.Name));
        Assert.Equal(DatabaseSourceType.Postgres, config.Profiles[1].Source);
        Assert.Equal("dev", config.SelectedProfile);
    }

    [Theory]
    [InlineData("", "Chưa nhập tên hồ sơ.")]
    [InlineData("   ", "Chưa nhập tên hồ sơ.")]
    [InlineData("PROD", "Đã có hồ sơ \"prod\".")]
    [InlineData("a/b", "ký tự")]
    [InlineData("staging", null)]
    public void Validate_name(string name, string? error)
    {
        var config = new DatabaseConnectionsConfig { Profiles = [new() { Name = "dev" }, new() { Name = "prod" }] };
        var result = ConnectionProfiles.ValidateName(config, name);
        if (error is null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.Contains(error, result);
        }
        Assert.Contains("tối đa", ConnectionProfiles.ValidateName(config, new string('x', ConnectionProfiles.MaxNameLength + 1)));
    }

    [Fact]
    public void Rename_to_same_name_with_other_case_is_allowed()
    {
        var dev = new ConnectionProfile { Name = "dev" };
        var config = new DatabaseConnectionsConfig { Profiles = [dev] };
        Assert.Null(ConnectionProfiles.ValidateName(config, "DEV", except: dev));
        Assert.NotNull(ConnectionProfiles.ValidateName(config, "DEV"));
    }

    [Fact]
    public void Suggested_names_dev_test_prod_then_numbered()
    {
        var config = new DatabaseConnectionsConfig { Profiles = [new() { Name = ConnectionProfiles.DefaultName }] };
        var suggested = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var name = ConnectionProfiles.SuggestName(config);
            suggested.Add(name);
            config.Profiles.Add(new ConnectionProfile { Name = name });
        }
        Assert.Equal(["dev", "test", "prod", "Hồ sơ 2", "Hồ sơ 3"], suggested);
    }

    [Fact]
    public void File_name_includes_profile_so_dev_and_prod_do_not_overwrite()
    {
        var dev = new ConnectionProfile { Name = "dev", Source = DatabaseSourceType.Postgres, Postgres = { Database = "orders" } };
        var prod = dev.Clone("prod");
        Assert.Equal("orders (dev)", ConnectionProfiles.FileBaseName(dev));
        Assert.Equal("orders (prod)", ConnectionProfiles.FileBaseName(prod));

        var oracle = new ConnectionProfile { Name = "test", Source = DatabaseSourceType.Oracle, Oracle = { ConnectBySid = false, ServiceName = "ORCLPDB1" } };
        Assert.Equal("ORCLPDB1 (test)", ConnectionProfiles.FileBaseName(oracle));
        Assert.Equal("Rdbms.HtmlGenerator (x)", ConnectionProfiles.FileBaseName(new ConnectionProfile { Name = "x" }));
    }
}
