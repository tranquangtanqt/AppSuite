using System.Globalization;

namespace DbTools.Core;

/// <summary>1 thông số log đọc từ pg_settings: giá trị đang áp, nguồn (configuration file / default / database / user...), file
/// đặt nó, giá trị mặc định khi biên dịch.</summary>
public sealed record LogSetting(string Name, string Setting, string Source, string SourceFile, string BootValue)
{
    /// <summary>Đặt bằng ALTER SYSTEM (nằm trong postgresql.auto.conf) - Về mặc định sẽ xoá dòng này.</summary>
    public bool ByAlterSystem => SourceFile.EndsWith("postgresql.auto.conf", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Giá trị muốn đặt cho 4 thông số log đổi được ngay (không cần khởi động lại server).</summary>
public sealed record LogConfigValues(string Statement, int MinDurationMs, bool Connections, bool Disconnections)
{
    public static LogConfigValues From(IReadOnlyDictionary<string, LogSetting> s) => new(
        s.TryGetValue("log_statement", out var st) ? st.Setting : "none",
        s.TryGetValue("log_min_duration_statement", out var md) && int.TryParse(md.Setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ? ms : -1,
        s.TryGetValue("log_connections", out var c) && c.Setting == "on",
        s.TryGetValue("log_disconnections", out var d) && d.Setting == "on");
}

/// <summary>
/// Đổi cấu hình log của server ở trang Nhật ký server: ALTER SYSTEM SET (ghi vào postgresql.auto.conf trên máy server) rồi
/// pg_reload_conf() - áp cho cả server (mọi database), không cần khởi động lại; phiên đang mở nhận giá trị mới, riêng
/// log_connections / log_disconnections chỉ áp cho kết nối mới. Về mặc định = ALTER SYSTEM RESET (xoá dòng trong
/// postgresql.auto.conf, quay về postgresql.conf). Cần superuser (PG15+: hoặc được GRANT ALTER SYSTEM ON PARAMETER).
/// Lớp này không chạm DB (unit test); chạy câu ở Services\PgServerLog.
/// </summary>
public static class LogConfigEdit
{
    public static readonly string[] Names = ["log_statement", "log_min_duration_statement", "log_connections", "log_disconnections"];

    public static readonly string[] StatementValues = ["none", "ddl", "mod", "all"];

    /// <summary>Lỗi giá trị (null = hợp lệ).</summary>
    public static string? Validate(LogConfigValues v) =>
        !StatementValues.Contains(v.Statement) ? $"log_statement phải là {string.Join(" / ", StatementValues)}."
        : v.MinDurationMs < -1 ? "Thời gian tối thiểu phải từ -1 (tắt) trở lên."
        : null;

    /// <summary>Câu ALTER SYSTEM SET cho các thông số khác giá trị đang áp (không đổi gì = rỗng).</summary>
    public static List<string> AlterStatements(LogConfigValues current, LogConfigValues wanted)
    {
        var list = new List<string>();
        if (current.Statement != wanted.Statement)
        {
            list.Add($"ALTER SYSTEM SET log_statement = '{wanted.Statement}'");
        }
        if (current.MinDurationMs != wanted.MinDurationMs)
        {
            list.Add($"ALTER SYSTEM SET log_min_duration_statement = {wanted.MinDurationMs.ToString(CultureInfo.InvariantCulture)}");
        }
        if (current.Connections != wanted.Connections)
        {
            list.Add($"ALTER SYSTEM SET log_connections = {(wanted.Connections ? "on" : "off")}");
        }
        if (current.Disconnections != wanted.Disconnections)
        {
            list.Add($"ALTER SYSTEM SET log_disconnections = {(wanted.Disconnections ? "on" : "off")}");
        }
        return list;
    }

    /// <summary>Về mặc định: RESET các thông số đang đặt bằng ALTER SYSTEM (thông số đặt ở postgresql.conf thì để nguyên).</summary>
    public static List<string> ResetStatements(IEnumerable<LogSetting> settings) =>
        settings.Where(s => Names.Contains(s.Name) && s.ByAlterSystem).Select(s => $"ALTER SYSTEM RESET {s.Name}").ToList();

    /// <summary>Dòng "log_statement: none → all (mọi câu, cả SELECT)" cho hộp hỏi lại.</summary>
    public static List<string> DescribeChanges(LogConfigValues current, LogConfigValues wanted)
    {
        var lines = new List<string>();
        if (current.Statement != wanted.Statement)
        {
            lines.Add($"log_statement: {current.Statement} → {wanted.Statement} ({StatementText(wanted.Statement)})");
        }
        if (current.MinDurationMs != wanted.MinDurationMs)
        {
            lines.Add($"log_min_duration_statement: {current.MinDurationMs} → {wanted.MinDurationMs} ({DurationText(wanted.MinDurationMs)})");
        }
        if (current.Connections != wanted.Connections)
        {
            lines.Add($"log_connections: {OnOff(current.Connections)} → {OnOff(wanted.Connections)}");
        }
        if (current.Disconnections != wanted.Disconnections)
        {
            lines.Add($"log_disconnections: {OnOff(current.Disconnections)} → {OnOff(wanted.Disconnections)}");
        }
        return lines;
    }

    /// <summary>Lưu ý khi đặt: ghi mọi câu làm file log lớn nhanh, server có đặt riêng cho database / user thì đè lên.</summary>
    public static List<string> Warnings(LogConfigValues wanted, IEnumerable<LogSetting> settings)
    {
        var list = new List<string>();
        if (wanted.Statement == "all" || wanted.MinDurationMs == 0)
        {
            list.Add("⚠ Ghi mọi câu (kể cả SELECT của ứng dụng): file log có thể lớn rất nhanh trên server bận - xem Dung lượng thư mục log, nhớ đặt lại khi xong.");
        }
        foreach (var s in settings.Where(s => Names.Contains(s.Name) && s.Source is "database" or "user" or "database user"))
        {
            list.Add($"⚠ {s.Name} đang được đặt riêng cho {s.Source} (ALTER DATABASE / ROLE ... SET) - giá trị đó đè lên cấu hình server với các phiên tương ứng.");
        }
        return list;
    }

    public static string StatementText(string value) => value switch
    {
        "none" => "không ghi câu nào",
        "ddl" => "chỉ DDL: CREATE / ALTER / DROP",
        "mod" => "DDL + INSERT / UPDATE / DELETE / TRUNCATE",
        "all" => "mọi câu, cả SELECT",
        _ => value,
    };

    public static string DurationText(int ms) => ms switch
    {
        < 0 => "tắt",
        0 => "mọi câu kèm thời gian chạy",
        _ => $"câu chạy lâu hơn {ms.ToString("N0", CultureInfo.InvariantCulture)} ms",
    };

    private static string OnOff(bool v) => v ? "on" : "off";
}
