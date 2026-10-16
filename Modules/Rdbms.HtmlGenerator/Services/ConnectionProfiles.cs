using System;
using System.IO;
using System.Linq;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Quy tắc của hồ sơ kết nối (không phụ thuộc WinUI - có unit test): chuyển config.xml cũ sang hồ sơ, kiểm tra tên,
/// tên file .db / .html theo hồ sơ.
/// </summary>
public static class ConnectionProfiles
{
    /// <summary>Hồ sơ tạo từ thiết lập cũ (hoặc lần đầu chạy). File .db / .html của nó giữ tên cũ {database} - dữ liệu
    /// đã đọc trước khi có hồ sơ vẫn dùng tiếp được.</summary>
    public const string DefaultName = "Mặc định";

    public const int MaxNameLength = 40;

    /// <summary>Luôn có ít nhất 1 hồ sơ và <see cref="DatabaseConnectionsConfig.SelectedProfile"/> trỏ đúng 1 hồ sơ có
    /// thật. config.xml cũ (các nguồn ở gốc) → 1 hồ sơ "Mặc định" mang đúng các giá trị đó + nguồn đã chọn.</summary>
    public static void Normalize(DatabaseConnectionsConfig config)
    {
        config.Profiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Name));
        if (config.Profiles.Count == 0)
        {
            config.Profiles.Add(new ConnectionProfile
            {
                Name = DefaultName,
                Source = Enum.IsDefined(config.SelectedSource) ? config.SelectedSource : DatabaseSourceType.Postgres,
                Postgres = config.Postgres ?? new(),
                Oracle = config.Oracle ?? new(),
                MySql = config.MySql ?? new(),
                SqlServer = config.SqlServer ?? new(),
            });
        }
        config.Postgres = null;
        config.Oracle = null;
        config.MySql = null;
        config.SqlServer = null;

        // Sửa tay config.xml: tên trùng → giữ hồ sơ đầu; nguồn lạ → PostgreSQL.
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        config.Profiles.RemoveAll(p => !seen.Add(p.Name.Trim()));
        foreach (var profile in config.Profiles)
        {
            profile.Name = profile.Name.Trim();
            if (!Enum.IsDefined(profile.Source))
            {
                profile.Source = DatabaseSourceType.Postgres;
            }
        }
        if (Find(config, config.SelectedProfile) is null)
        {
            config.SelectedProfile = config.Profiles[0].Name;
        }
    }

    public static ConnectionProfile? Find(DatabaseConnectionsConfig config, string? name) =>
        config.Profiles.FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Lỗi của tên hồ sơ mới / đổi tên (null = hợp lệ). <paramref name="except"/>: hồ sơ đang đổi tên (đổi hoa /
    /// thường của chính nó thì không tính là trùng).</summary>
    public static string? ValidateName(DatabaseConnectionsConfig config, string? name, ConnectionProfile? except = null)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "Chưa nhập tên hồ sơ.";
        }
        if (trimmed.Length > MaxNameLength)
        {
            return $"Tên hồ sơ tối đa {MaxNameLength} ký tự.";
        }
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "Tên hồ sơ không được có các ký tự \\ / : * ? \" < > | (tên được dùng làm tên file).";
        }
        var existing = Find(config, trimmed);
        return existing is not null && !ReferenceEquals(existing, except) ? $"Đã có hồ sơ \"{existing.Name}\"." : null;
    }

    /// <summary>Tên gợi ý khi Thêm hồ sơ: "dev", "test", "prod" nếu chưa có, sau đó "Hồ sơ 2", "Hồ sơ 3"...</summary>
    public static string SuggestName(DatabaseConnectionsConfig config)
    {
        foreach (var name in new[] { "dev", "test", "prod" })
        {
            if (Find(config, name) is null)
            {
                return name;
            }
        }
        for (var i = 2; ; i++)
        {
            if (Find(config, $"Hồ sơ {i}") is null)
            {
                return $"Hồ sơ {i}";
            }
        }
    }

    /// <summary>Tên file .db / .html (chưa có đuôi): "{database} ({hồ sơ})" để dev / prod cùng tên database không ghi đè
    /// nhau; hồ sơ "Mặc định" giữ tên cũ "{database}". Chưa nhập database → "Rdbms.HtmlGenerator".</summary>
    public static string FileBaseName(ConnectionProfile profile)
    {
        var database = string.IsNullOrWhiteSpace(profile.DatabaseName) ? "Rdbms.HtmlGenerator" : profile.DatabaseName.Trim();
        return string.Equals(profile.Name, DefaultName, StringComparison.CurrentCultureIgnoreCase)
            ? database
            : $"{database} ({profile.Name})";
    }
}
