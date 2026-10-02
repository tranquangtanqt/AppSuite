using System.Text.Json;

namespace CsvEditor.Services;

/// <summary>Danh sách file mở / lưu gần đây (mới nhất trước, tối đa <see cref="MaxCount"/>), lưu ở
/// Data\Config\recent-files.json cạnh exe - cùng quy ước với settings.json của ScreenCapture. File hỏng / không
/// đọc được thì coi như danh sách rỗng; ghi lỗi (thư mục chỉ đọc...) thì bỏ qua - danh sách gần đây không đáng làm
/// hỏng thao tác mở / lưu.</summary>
public sealed class RecentFilesStore
{
    public const int MaxCount = 10;

    private readonly string _configPath;
    private List<string>? _paths;

    public RecentFilesStore(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "Data", "Config", "recent-files.json");
    }

    public IReadOnlyList<string> Paths => _paths ??= Load();

    /// <summary>Đưa <paramref name="path"/> lên đầu (trùng - không phân biệt hoa thường - thì chuyển lên, không lặp).</summary>
    public void Add(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var paths = Paths.Where(p => !string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase)).Prepend(fullPath).Take(MaxCount).ToList();
        Save(paths);
    }

    public void Remove(string path) =>
        Save(Paths.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList());

    public void Clear() => Save(new List<string>());

    private List<string> Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_configPath)) ?? new List<string>();
                return paths.Where(p => !string.IsNullOrWhiteSpace(p))
                    .DistinctBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxCount)
                    .ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new List<string>();
    }

    private void Save(List<string> paths)
    {
        _paths = paths;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
