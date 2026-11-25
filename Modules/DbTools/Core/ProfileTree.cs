using DbTools.Models;

namespace DbTools.Core;

/// <summary>1 thư mục trong cây hồ sơ kết nối (gốc cây: <see cref="Path"/> rỗng).</summary>
public sealed class ProfileFolder(string path)
{
    /// <summary>Đường dẫn từ gốc, vd "202604_MCF7/eci".</summary>
    public string Path { get; } = path;

    public string Name => Path[(Path.LastIndexOf('/') + 1)..];

    public List<ProfileFolder> Folders { get; } = [];
    public List<ConnectionProfile> Profiles { get; } = [];

    /// <summary>Số hồ sơ trong thư mục, gồm cả thư mục con.</summary>
    public int Count => Profiles.Count + Folders.Sum(f => f.Count);

    public override string ToString() => Name;
}

/// <summary>
/// Cây thư mục hồ sơ kết nối. Thư mục chỉ là chữ <see cref="ConnectionProfile.Folder"/> của từng hồ sơ ("a/b"): có hồ sơ thì có
/// thư mục, hết hồ sơ thì mất. Không phân biệt hoa thường. Trong 1 cấp: thư mục trước, hồ sơ sau, theo tên.
/// </summary>
public static class ProfileTree
{
    private static readonly StringComparer ByName = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>"/ a \ b //" → "a/b".</summary>
    public static string Normalize(string? path) =>
        string.Join('/', (path ?? string.Empty).Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string Parent(string path) => path.LastIndexOf('/') is var i and >= 0 ? path[..i] : string.Empty;

    /// <summary><paramref name="path"/> là <paramref name="folder"/> hoặc nằm trong nó (gốc chứa mọi thứ).</summary>
    public static bool IsUnder(string path, string folder) =>
        folder.Length == 0
        || path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mọi từ trong <paramref name="filter"/> đều có trong tên / host:port/database / user / thư mục.</summary>
    public static bool Matches(ConnectionProfile p, string filter)
    {
        var text = $"{p.Name}\n{p.Target}\n{p.Username}\n{p.Folder}";
        return filter.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    public static ProfileFolder Build(IEnumerable<ConnectionProfile> profiles, string filter = "")
    {
        var root = new ProfileFolder(string.Empty);
        var folders = new Dictionary<string, ProfileFolder>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };

        ProfileFolder Get(string path)
        {
            if (!folders.TryGetValue(path, out var folder))
            {
                // Chữ hoa thường của cấp cha theo thư mục đã có ("mcf7/a" và "MCF7/b" cùng nằm trong 1 thư mục).
                var parent = Get(Parent(path));
                var name = path[(path.LastIndexOf('/') + 1)..];
                folder = new ProfileFolder(parent.Path.Length == 0 ? name : parent.Path + "/" + name);
                parent.Folders.Add(folder);
                folders[path] = folder;
            }
            return folder;
        }

        foreach (var p in profiles.Where(p => Matches(p, filter)))
        {
            Get(Normalize(p.Folder)).Profiles.Add(p);
        }
        foreach (var f in folders.Values)
        {
            f.Folders.Sort((a, b) => ByName.Compare(a.Name, b.Name));
            var sorted = f.Profiles.OrderBy(p => p.TreeLabel, ByName).ToList(); // OrderBy giữ thứ tự khi trùng tên
            f.Profiles.Clear();
            f.Profiles.AddRange(sorted);
        }
        return root;
    }

    /// <summary>Hồ sơ theo thứ tự hiện trong cây - cho ô chọn server ở các trang.</summary>
    public static List<ConnectionProfile> Flatten(IEnumerable<ConnectionProfile> profiles)
    {
        var result = new List<ConnectionProfile>();
        void Walk(ProfileFolder folder)
        {
            folder.Folders.ForEach(Walk);
            result.AddRange(folder.Profiles);
        }
        Walk(Build(profiles));
        return result;
    }

    /// <summary>Đường dẫn mọi thư mục (gồm thư mục cha), theo thứ tự trong cây.</summary>
    public static List<string> FolderPaths(IEnumerable<ConnectionProfile> profiles)
    {
        var result = new List<string>();
        void Walk(ProfileFolder folder)
        {
            foreach (var f in folder.Folders)
            {
                result.Add(f.Path);
                Walk(f);
            }
        }
        Walk(Build(profiles));
        return result;
    }

    /// <summary>Chuẩn hoá đường dẫn gõ vào, cấp nào đã có thì theo kiểu chữ hoa thường đang có ("mcf7/moi" → "MCF7/moi").</summary>
    public static string MatchExisting(IEnumerable<ConnectionProfile> profiles, string path)
    {
        path = Normalize(path);
        foreach (var f in FolderPaths(profiles).Where(f => IsUnder(path, f))) // cha trước con
        {
            path = f + path[f.Length..];
        }
        return path;
    }

    /// <summary>Đổi tên / chuyển chỗ thư mục: mọi hồ sơ trong đó (cả thư mục con) đổi theo. Trùng thư mục đã có thì gộp vào;
    /// tên mới rỗng thì đưa ra gốc.</summary>
    public static void MoveFolder(IEnumerable<ConnectionProfile> profiles, string oldPath, string newPath)
    {
        foreach (var p in profiles.Where(p => IsUnder(Normalize(p.Folder), oldPath)).ToList())
        {
            p.Folder = Normalize(newPath + Normalize(p.Folder)[oldPath.Length..]);
        }
    }
}
