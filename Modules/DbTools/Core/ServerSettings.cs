using System.Globalization;
using System.Text.RegularExpressions;

namespace DbTools.Core;

/// <summary>1 thông số của server (1 dòng pg_settings). <see cref="Value"/> = current_setting() (dạng dễ đọc, vd "128MB");
/// <see cref="Setting"/> = giá trị thô theo <see cref="Unit"/> (vd "16384" × 8kB).</summary>
public sealed record PgSetting(
    string Name, string Value, string Setting, string Unit, string Category, string ShortDesc, string ExtraDesc,
    string Context, string VarType, string Source, string SourceFile, int? SourceLine, string BootValue,
    string MinValue, string MaxValue, IReadOnlyList<string> EnumValues, bool PendingRestart)
{
    /// <summary>Nhóm lớn (phần trước " / " của category), vd "Reporting and Logging".</summary>
    public string GroupKey => Category.Split(" / ")[0];

    /// <summary>Tên nhóm tiếng Việt.</summary>
    public string Group => ServerSettings.GroupName(GroupKey);

    /// <summary>Dòng cuối trong file cấu hình (pg_file_settings) nằm ở postgresql.auto.conf - bắt được cả giá trị đã ghi nhưng
    /// chưa áp (chờ khởi động lại), lúc đó pg_settings.sourcefile vẫn là postgresql.conf.</summary>
    public bool InAutoConf { get; init; }

    /// <summary>Đang đặt bằng ALTER SYSTEM (postgresql.auto.conf) - Về mặc định được.</summary>
    public bool ByAlterSystem => InAutoConf || SourceFile.EndsWith("postgresql.auto.conf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Khác mặc định khi biên dịch (đặt trong file cấu hình, dòng lệnh, ALTER SYSTEM...).</summary>
    public bool Changed => Source is not ("default" or "override" or "client" or "session");

    /// <summary>Đổi xong phải khởi động lại server mới có hiệu lực.</summary>
    public bool NeedsRestart => Context == "postmaster";

    /// <summary>internal: chỉ xem (cố định lúc biên dịch / initdb).</summary>
    public bool ReadOnly => Context == "internal";

    /// <summary>"Áp dụng khi" bằng lời.</summary>
    public string ContextText => Context switch
    {
        "internal" => "Chỉ xem - cố định lúc biên dịch / initdb",
        "postmaster" => "Cần KHỞI ĐỘNG LẠI server",
        "sighup" => "Nạp lại cấu hình là có hiệu lực (mọi phiên)",
        "backend" or "superuser-backend" => "Nạp lại cấu hình - chỉ áp cho kết nối MỚI",
        "superuser" or "user" => "Nạp lại cấu hình là có hiệu lực (phiên cũng tự SET được)",
        _ => Context,
    };

    /// <summary>Khoảng / giá trị cho phép, vd "0 - 2147483647 (ms)" hoặc "none, ddl, mod, all".</summary>
    public string RangeText => VarType switch
    {
        "bool" => "on / off",
        "enum" => string.Join(", ", EnumValues),
        "integer" or "real" when MinValue.Length > 0 => $"{MinValue} - {MaxValue}{(Unit.Length > 0 ? $" (đơn vị {Unit})" : string.Empty)}",
        _ => "chuỗi",
    };

    public string DefaultText => BootValue.Length == 0 ? "(trống)" : BootValue + (Unit.Length > 0 && VarType is "integer" or "real" ? " × " + Unit : string.Empty);

    public string SourceText => ByAlterSystem ? "ALTER SYSTEM (postgresql.auto.conf)"
        : SourceFile.Length > 0 ? $"{Path.GetFileName(SourceFile.Replace('\\', '/').Split('/')[^1])}{(SourceLine is { } l ? $" dòng {l}" : string.Empty)}"
        : Source switch
        {
            "default" => "mặc định",
            "configuration file" => "file cấu hình",
            "command line" => "dòng lệnh khởi động",
            "environment variable" => "biến môi trường",
            "override" => "server tự đặt",
            _ => Source,
        };
}

/// <summary>Giá trị đặt riêng cho 1 database / user (ALTER DATABASE / ROLE ... SET) - đè lên cấu hình server.</summary>
public sealed record SettingOverride(string Name, string Value, string Database, string Role)
{
    public string Where => (Database, Role) switch
    {
        ("", "") => "mọi phiên",
        (var d, "") => $"database {d}",
        ("", var r) => $"user {r}",
        var (d, r) => $"user {r} trong database {d}",
    };
}

/// <summary>Thay đổi chờ áp dụng: đặt giá trị mới, hoặc Về mặc định (RESET).</summary>
public sealed record SettingChange(PgSetting Setting, string? NewValue)
{
    public bool Reset => NewValue is null;

    public string Describe() => Reset
        ? $"{Setting.Name}: {Setting.Value} → về mặc định (bỏ dòng ALTER SYSTEM)"
        : $"{Setting.Name}: {Setting.Value} → {NewValue}";
}

/// <summary>
/// Trang "Cấu hình server": đọc pg_settings, kiểm giá trị trước khi gửi, dựng câu ALTER SYSTEM SET / RESET, lọc danh sách.
/// ALTER SYSTEM ghi vào postgresql.auto.conf trên máy server (đè lên postgresql.conf); pg_reload_conf() áp ngay các thông số
/// không phải postmaster. Không phụ thuộc DB / giao diện (unit test).
/// </summary>
public static partial class ServerSettings
{
    /// <summary>Thông số dạng danh sách mà PostgreSQL bọc từng phần tử (GUC_LIST_QUOTE): phải gửi 'a', 'b' chứ không 'a, b'.</summary>
    private static readonly HashSet<string> QuotedLists = new(StringComparer.Ordinal)
    {
        "search_path", "temp_tablespaces", "shared_preload_libraries", "session_preload_libraries", "local_preload_libraries",
    };

    private static readonly Dictionary<string, string> Groups = new(StringComparer.Ordinal)
    {
        ["Reporting and Logging"] = "Log",
        ["Connections and Authentication"] = "Kết nối và xác thực",
        ["Resource Usage"] = "Tài nguyên (bộ nhớ, đĩa)",
        ["Client Connection Defaults"] = "Mặc định cho phiên",
        ["Query Tuning"] = "Tối ưu câu lệnh",
        ["Write-Ahead Log"] = "WAL",
        ["Replication"] = "Replication",
        ["Autovacuum"] = "Autovacuum",
        ["Vacuuming"] = "Vacuum",
        ["Statistics"] = "Thống kê",
        ["Lock Management"] = "Khoá",
        ["Error Handling"] = "Xử lý lỗi",
        ["File Locations"] = "Vị trí file",
        ["Version and Platform Compatibility"] = "Tương thích",
        ["Preset Options"] = "Cố định (chỉ xem)",
        ["Developer Options"] = "Dành cho nhà phát triển",
        ["Customized Options"] = "Extension / tuỳ biến",
        ["Process Title"] = "Tên tiến trình",
    };

    /// <summary>Thứ tự nhóm: Log trước (hay dùng nhất), còn lại theo bảng trên, nhóm lạ (bản PostgreSQL / Fujitsu khác) cuối.</summary>
    public static int GroupOrder(string groupKey)
    {
        var i = Groups.Keys.ToList().IndexOf(groupKey);
        return i < 0 ? int.MaxValue : i;
    }

    public static string GroupName(string groupKey) => Groups.TryGetValue(groupKey, out var vn) ? vn : groupKey;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_$]*(\.[A-Za-z_][A-Za-z0-9_$]*)?$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^\s*(-?\d+(\.\d+)?)\s*(B|kB|MB|GB|TB|us|ms|s|min|h|d)?\s*$")]
    private static partial Regex NumberWithUnit();

    /// <summary>Kiểm giá trị người dùng gõ theo kiểu thông số. Trả (giá trị chuẩn hoá, null) hoặc (null, lỗi).</summary>
    public static (string? Value, string? Error) Normalize(PgSetting s, string input)
    {
        var text = input.Trim();
        if (s.ReadOnly)
        {
            return (null, $"{s.Name} chỉ xem được (internal).");
        }
        switch (s.VarType)
        {
            case "bool":
                return text.ToLowerInvariant() switch
                {
                    "on" or "true" or "yes" or "1" or "bật" => ("on", null),
                    "off" or "false" or "no" or "0" or "tắt" => ("off", null),
                    _ => (null, $"{s.Name}: chỉ nhận on / off."),
                };
            case "enum":
                var match = s.EnumValues.FirstOrDefault(v => v.Equals(text, StringComparison.OrdinalIgnoreCase));
                return match is null ? (null, $"{s.Name}: chỉ nhận {string.Join(", ", s.EnumValues)}.") : (match, null);
            case "integer" or "real":
                var m = NumberWithUnit().Match(text);
                if (!m.Success)
                {
                    return (null, $"{s.Name}: phải là số{(s.Unit.Length > 0 ? " (có thể kèm đơn vị: kB / MB / GB, ms / s / min / h / d)" : string.Empty)}.");
                }
                if (s.VarType == "integer" && m.Groups[2].Success && !m.Groups[3].Success)
                {
                    return (null, $"{s.Name}: phải là số nguyên.");
                }
                // Có đơn vị thì để server tự quy đổi và kiểm khoảng; không đơn vị thì kiểm min / max ngay.
                if (!m.Groups[3].Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                    && double.TryParse(s.MinValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                    && double.TryParse(s.MaxValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var max)
                    && (n < min || n > max))
                {
                    return (null, $"{s.Name}: phải trong khoảng {s.MinValue} - {s.MaxValue}{(s.Unit.Length > 0 ? " " + s.Unit : string.Empty)}.");
                }
                return (m.Groups[1].Value + (m.Groups[3].Success ? m.Groups[3].Value : string.Empty), null);
            default:
                return text.Contains('\n') || text.Contains('\r') ? (null, $"{s.Name}: không được xuống dòng.") : (text, null);
        }
    }

    /// <summary>Câu ALTER SYSTEM cho 1 thay đổi (giá trị đã qua <see cref="Normalize"/>). Tên lấy từ pg_settings nhưng vẫn kiểm.</summary>
    public static string Statement(SettingChange change)
    {
        var name = change.Setting.Name;
        if (!NamePattern().IsMatch(name))
        {
            throw new ArgumentException($"Tên thông số lạ: {name}");
        }
        if (change.Reset)
        {
            return $"ALTER SYSTEM RESET {name}";
        }
        var value = change.NewValue!;
        var literal = QuotedLists.Contains(name)
            ? string.Join(", ", value.Split(',', StringSplitOptions.TrimEntries).Select(Quote))
            : Quote(value);
        return $"ALTER SYSTEM SET {name} = {literal}";
    }

    private static string Quote(string v) => "'" + v.Replace("'", "''") + "'";

    /// <summary>Lọc danh sách: chữ (tên / mô tả / nhóm; nhiều từ phải có đủ), nhóm (null = tất cả), các ô lọc.</summary>
    public static IEnumerable<PgSetting> Filter(IEnumerable<PgSetting> all, string? text, string? groupKey, bool changedOnly, bool alterSystemOnly,
        bool restartOnly, ISet<string>? overridden = null, bool overriddenOnly = false)
    {
        var words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return all
            .Where(s => groupKey is null || s.GroupKey == groupKey)
            .Where(s => !changedOnly || s.Changed)
            .Where(s => !alterSystemOnly || s.ByAlterSystem)
            .Where(s => !restartOnly || s.NeedsRestart || s.PendingRestart)
            .Where(s => !overriddenOnly || overridden?.Contains(s.Name) == true)
            .Where(s => words.All(w => s.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                || s.ShortDesc.Contains(w, StringComparison.OrdinalIgnoreCase)
                || s.Category.Contains(w, StringComparison.OrdinalIgnoreCase)
                || s.Group.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => GroupOrder(s.GroupKey)).ThenBy(s => s.Category, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal);
    }

    /// <summary>Lưu ý cho hộp hỏi lại: cần khởi động lại, đặt riêng theo database / user đè lên, ghi mọi câu.</summary>
    public static List<string> Warnings(IEnumerable<SettingChange> changes, IEnumerable<SettingOverride> overrides)
    {
        var list = new List<string>();
        var all = changes.ToList();
        var restart = all.Where(c => c.Setting.NeedsRestart).Select(c => c.Setting.Name).ToList();
        if (restart.Count > 0)
        {
            list.Add($"⚠ {string.Join(", ", restart)}: chỉ có hiệu lực sau khi KHỞI ĐỘNG LẠI server - DbTools không tự khởi động lại. Giá trị sai có thể làm server không lên được.");
        }
        var byName = overrides.ToLookup(o => o.Name);
        foreach (var c in all.Where(c => byName.Contains(c.Setting.Name)))
        {
            list.Add($"⚠ {c.Setting.Name} đang được đặt riêng cho {string.Join("; ", byName[c.Setting.Name].Select(o => o.Where))} - giá trị đó đè lên cấu hình server với các phiên tương ứng.");
        }
        if (all.Any(c => (c.Setting.Name == "log_statement" && c.NewValue == "all") || (c.Setting.Name == "log_min_duration_statement" && c.NewValue == "0")))
        {
            list.Add("⚠ Ghi mọi câu (kể cả SELECT của ứng dụng): file log có thể lớn rất nhanh trên server bận - xem xong nhớ đặt lại.");
        }
        return list;
    }
}
