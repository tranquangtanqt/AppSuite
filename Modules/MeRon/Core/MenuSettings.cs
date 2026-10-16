using System.Text.Json;

namespace MeRon.Core;

/// <summary>
/// Cài đặt menu trái (trang Cài đặt): các trang người dùng ẩn khỏi menu. Lưu file settings.json cạnh database (MERON_DB_PATH
/// đổi chỗ thì đổi theo) - không nằm trong database nên nhập backup / xoá toàn bộ dữ liệu không mất. File hỏng / không có
/// → hiện tất cả.
/// </summary>
public sealed class MenuSettings
{
    public const string FileName = "settings.json";

    /// <summary>Không ẩn được: Đặt hàng là trang mở đầu (Cài đặt, Hướng dẫn nằm ở chân menu, không có trong danh sách).</summary>
    public static IReadOnlySet<string> AlwaysVisible { get; } = new HashSet<string> { "OrdersPage" };

    private readonly HashSet<string> _hidden = [];

    public IReadOnlyCollection<string> Hidden => _hidden;

    public bool IsVisible(string page) => AlwaysVisible.Contains(page) || !_hidden.Contains(page);

    public void SetVisible(string page, bool visible)
    {
        if (visible || AlwaysVisible.Contains(page))
        {
            _hidden.Remove(page);
        }
        else
        {
            _hidden.Add(page);
        }
    }

    public void ShowAll() => _hidden.Clear();

    public static MenuSettings Load(string path)
    {
        var settings = new MenuSettings();
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { HiddenMenu: { } hidden })
            {
                foreach (var page in hidden.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    settings.SetVisible(page, false);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // file hỏng → hiện tất cả
        }
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var stored = new Stored { HiddenMenu = [.. _hidden.Order(StringComparer.Ordinal)] };
        File.WriteAllText(path, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Stored
    {
        public List<string>? HiddenMenu { get; set; }
    }
}
