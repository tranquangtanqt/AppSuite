using System.Text.Json;

namespace MeRon.Core;

/// <summary>
/// Cài đặt của trang Cài đặt: các trang người dùng ẩn khỏi menu trái, số ngày giữ ảnh chụp (<see cref="CaptureKeepDays"/>).
/// Lưu file settings.json cạnh database (MERON_DB_PATH đổi chỗ thì đổi theo) - không nằm trong database nên nhập backup /
/// xoá toàn bộ dữ liệu không mất. File hỏng / không có → hiện tất cả, giữ ảnh 30 ngày.
/// </summary>
public sealed class MenuSettings
{
    public const string FileName = "settings.json";

    public const int DefaultCaptureKeepDays = 30;

    public const int MaxCaptureKeepDays = 3650;

    private int _captureKeepDays = DefaultCaptureKeepDays;

    /// <summary>Ảnh "Chụp ảnh" (Pictures\MeRon) cũ hơn ngần này ngày thì tự đưa vào Thùng rác khi mở app (xem
    /// <see cref="CaptureFiles"/>). 0 = giữ hết.</summary>
    public int CaptureKeepDays
    {
        get => _captureKeepDays;
        set => _captureKeepDays = Math.Clamp(value, 0, MaxCaptureKeepDays);
    }

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
            if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { } stored)
            {
                foreach (var page in (stored.HiddenMenu ?? []).Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    settings.SetVisible(page, false);
                }
                if (stored.CaptureKeepDays is { } days)
                {
                    settings.CaptureKeepDays = days;
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
        var stored = new Stored { HiddenMenu = [.. _hidden.Order(StringComparer.Ordinal)], CaptureKeepDays = CaptureKeepDays };
        File.WriteAllText(path, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Stored
    {
        public List<string>? HiddenMenu { get; set; }

        /// <summary>null = file cũ (trước 2026-10-23) → mặc định 30.</summary>
        public int? CaptureKeepDays { get; set; }
    }
}
