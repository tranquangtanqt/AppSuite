using DbTools.Core;
using DbTools.Models;
using DbTools.Services;

namespace DbTools.Tests;

public class ProfileTreeTests
{
    private static ConnectionProfile P(string name, string folder = "", string host = "localhost", string db = "db") =>
        new() { Name = name, Folder = folder, Host = host, Database = db };

    /// <summary>Cây viết thành chữ: mỗi dòng 1 thư mục / hồ sơ, lùi vào theo cấp.</summary>
    private static string Draw(ProfileFolder folder, string indent = "") =>
        string.Concat(folder.Folders.Select(f => $"{indent}[{f.Name} {f.Count}]\n" + Draw(f, indent + " ")))
        + string.Concat(folder.Profiles.Select(p => $"{indent}{p.TreeLabel}\n"));

    [Fact]
    public void Build_nests_folders_first_sorted_by_name()
    {
        var tree = ProfileTree.Build([P("z root"), P("v2", "MCF7/eci"), P("b", "MCF7"), P("v1", "MCF7/eci"), P("x", "Alpha"), P("a root")]);

        Assert.Equal("[Alpha 1]\n x\n[MCF7 3]\n [eci 2]\n  v1\n  v2\n b\na root\nz root\n", Draw(tree));
    }

    [Fact]
    public void Folders_differing_only_in_case_are_one_folder()
    {
        var tree = ProfileTree.Build([P("1", "mcf7/a"), P("2", "MCF7/B"), P("3", "Mcf7/b")]);

        Assert.Equal("[mcf7 3]\n [a 1]\n  1\n [B 2]\n  2\n  3\n", Draw(tree));
        Assert.Equal(["mcf7", "mcf7/a", "mcf7/B"], ProfileTree.FolderPaths([P("1", "mcf7/a"), P("2", "MCF7/B"), P("3", "Mcf7/b")]));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData("/ a \\ b //c/", "a/b/c")]
    [InlineData("MCF7", "MCF7")]
    public void Normalize_cleans_separators_and_spaces(string? input, string expected) => Assert.Equal(expected, ProfileTree.Normalize(input));

    [Theory]
    [InlineData(" 202604_mcf7 \\ ECI ", "202604_MCF7/eci")]
    [InlineData("202604_mcf7/Moi", "202604_MCF7/Moi")]
    [InlineData("202604_mcf70", "202604_mcf70")]
    [InlineData("", "")]
    public void Typed_folder_follows_the_case_of_existing_folders(string typed, string expected) =>
        Assert.Equal(expected, ProfileTree.MatchExisting([P("1", "202604_MCF7/eci"), P("2")], typed));

    [Theory]
    [InlineData("eci v2", "v2")]            // thư mục + tên
    [InlineData("10.0.0.9", "far")]         // host
    [InlineData("SUGAKI", "far")]           // database, không phân biệt hoa thường
    [InlineData("khongco", "")]
    [InlineData("", "v1,v2,far")]
    public void Filter_matches_every_word_in_name_target_or_folder(string filter, string expected)
    {
        ConnectionProfile[] all = [P("v1", "MCF7/eci"), P("v2", "MCF7/eci"), P("far", "", "10.0.0.9", "sugaki")];

        Assert.Equal(expected, string.Join(',', all.Where(p => ProfileTree.Matches(p, filter)).Select(p => p.Name)));
        if (filter == "eci v2")
        {
            Assert.Equal("[MCF7 1]\n [eci 1]\n  v2\n", Draw(ProfileTree.Build(all, filter))); // thư mục không còn hồ sơ khớp thì ẩn
        }
    }

    [Fact]
    public void Flatten_gives_pickers_the_tree_order_with_every_profile_once()
    {
        ConnectionProfile[] all = [P("root"), P("v2", "B/x"), P("v1", "B/x"), P("a", "A")];

        Assert.Equal(["a", "v1", "v2", "root"], ProfileTree.Flatten(all).Select(p => p.Name));
    }

    [Fact]
    public void MoveFolder_renames_subfolders_and_leaves_similar_names_alone()
    {
        ConnectionProfile[] all = [P("1", "MCF7"), P("2", "mcf7/eci"), P("3", "MCF70/eci"), P("4")];

        ProfileTree.MoveFolder(all, "MCF7", "202604_MCF7");
        Assert.Equal(["202604_MCF7", "202604_MCF7/eci", "MCF70/eci", ""], all.Select(p => p.Folder));

        ProfileTree.MoveFolder(all, "202604_MCF7/eci", "/eci"); // kéo ra gốc
        Assert.Equal(["202604_MCF7", "eci", "MCF70/eci", ""], all.Select(p => p.Folder));

        ProfileTree.MoveFolder(all, "eci", "202604_MCF7"); // trùng thư mục đã có → gộp
        Assert.Equal(["202604_MCF7", "202604_MCF7", "MCF70/eci", ""], all.Select(p => p.Folder));
    }

    [Fact]
    public void Picker_text_shows_folder_and_production()
    {
        var p = P("dev", "MCF7/eci");
        p.IsProduction = true;

        Assert.Equal("🔴 PRODUCTION · dev  ‹MCF7 / eci›  (localhost:5432/db)", p.PickerText);
        Assert.Equal("MCF7 / eci / dev", p.FullName);
        Assert.Equal("localhost:5432/db  ‹A›", P("", "A").PickerText); // chưa đặt tên
        Assert.Equal("dev", P("dev").FullName);
        Assert.Equal("localhost:5432/db", P("").TreeLabel); // chưa đặt tên
        Assert.Equal("MCF7/eci", p.Clone("dev 2").Folder);
        Assert.Equal("", p.Clone("dev 2").Id); // nhân bản là hồ sơ khác
    }

    [Fact]
    public void Changing_folder_notifies_picker_text()
    {
        var p = P("dev");
        var changed = new List<string?>();
        p.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        p.Folder = "MCF7";

        Assert.Contains(nameof(ConnectionProfile.PickerText), changed);
    }

    [Fact]
    public void Old_file_without_folder_or_id_keeps_passwords_and_gets_stable_ids()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "connections.json");
        // "!!" = mật khẩu mã hoá của tài khoản / máy khác (không giải được) - cấp mã không được làm mất
        File.WriteAllText(path, $$"""
            [ { "Name": "dev", "Host": "h", "Port": 1, "Database": "d", "Username": "u", "ProtectedPassword": "{{ConnectionStore.Protect("mk")}}" },
              { "Name": "dev", "Host": "h2", "Port": 2, "Database": "d2", "Username": "u", "ProtectedPassword": "!!" } ]
            """);
        var store = new ConnectionStore(path);

        var first = store.Load();
        var second = store.Load();

        Assert.Equal(["", ""], first.Select(p => p.Folder));
        Assert.Equal(["mk", ""], second.Select(p => p.Password));
        Assert.Equal("!!", second[1].ProtectedPassword);
        Assert.All(first, p => Assert.NotEmpty(p.Id));
        Assert.NotEqual(first[0].Id, first[1].Id);
        Assert.Equal(first.Select(p => p.Id), second.Select(p => p.Id));
    }

    [Fact]
    public void Folder_and_id_are_saved_and_duplicate_ids_are_replaced()
    {
        using var folder = new TempFolder();
        var store = new ConnectionStore(Path.Combine(folder.Path, "connections.json"));
        var a = P("a", "MCF7/eci");
        var b = P("b");
        b.Id = a.Id = "same"; // file sửa tay / chép dòng

        store.Save([a, b]);
        var loaded = store.Load();

        Assert.Equal("MCF7/eci", loaded[0].Folder);
        Assert.Equal("same", loaded[0].Id);
        Assert.NotEqual("same", loaded[1].Id);
        Assert.Equal(b.Id, loaded[1].Id);
    }

    [Fact]
    public void Loading_a_missing_file_does_not_create_one()
    {
        using var folder = new TempFolder();
        var store = new ConnectionStore(Path.Combine(folder.Path, "connections.json"));

        Assert.Empty(store.Load());
        Assert.False(File.Exists(store.FilePath));
    }
}
