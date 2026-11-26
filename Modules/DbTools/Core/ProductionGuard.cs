namespace DbTools.Core;

/// <summary>
/// Hồ sơ "Quan trọng (Production)": thao tác ghi lên server đó phải gõ lại đúng 1 tên trong hộp hỏi lại thì nút mới bật.
/// Mỗi hàm trả tên phải gõ; null = không cần gõ (hồ sơ thường). Tên trống (vd hồ sơ chưa điền Database) thì gõ tên hồ sơ.
/// Lớp này ở Core để unit test; hồ sơ / hộp thoại gọi vào (ConnectionProfile.IsProduction, Views/Dialogs.ConfirmAsync).
/// </summary>
public static class ProductionGuard
{
    /// <summary>Chạy thư mục script (không áp cho Chạy thử - luôn rollback).</summary>
    public static string? ForRun(bool production, string database, string profileName) =>
        production ? Name(database, profileName) : null;

    /// <summary>Chạy SQL ở chế độ cho phép ghi (chế độ chỉ đọc không hỏi).</summary>
    public static string? ForQuery(bool production, bool allowWrite, string database, string profileName) =>
        production && allowWrite ? Name(database, profileName) : null;

    /// <summary>Đổi cấu hình server (ALTER SYSTEM - áp cho cả server production).</summary>
    public static string? ForServerConfig(bool production, string database, string profileName) =>
        production ? Name(database, profileName) : null;

    /// <summary>Restore vào DB có sẵn hoặc tạo DB mới trên server production.</summary>
    public static string? ForRestore(bool production, string database, string profileName) =>
        production ? Name(database, profileName) : null;

    /// <summary>Copy DB: phải ngắt phiên của DB nguồn trên server production (cùng server, TEMPLATE) → gõ tên DB nguồn;
    /// ghi DB mới lên server production → gõ tên DB đích. Chỉ đọc từ nguồn production (pg_dump) thì không hỏi thêm.</summary>
    public static string? ForCopy(bool sourceProduction, bool destinationProduction, bool killsSourceSessions, string sourceDb, string targetDb) =>
        sourceProduction && killsSourceSessions ? sourceDb
        : destinationProduction ? targetDb
        : null;

    /// <summary>Ngắt phiên trên server production: các phiên cùng 1 DB → gõ tên DB đó; nhiều DB → gõ tên hồ sơ.</summary>
    public static string? ForSessions(bool production, IEnumerable<string?> databases, string profileName, string profileDatabase)
    {
        if (!production)
        {
            return null;
        }
        var distinct = databases.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : Name(profileName, profileDatabase);
    }

    /// <summary>Chữ đã gõ khớp tên (bỏ khoảng trắng 2 đầu, phân biệt hoa thường - tên DB trong PostgreSQL phân biệt).</summary>
    public static bool Matches(string? typed, string expected) => (typed ?? string.Empty).Trim() == expected;

    /// <summary>Cả 2 trống thì gõ "PRODUCTION" - không bao giờ để ô trống tự khớp.</summary>
    private static string Name(string first, string fallback) =>
        !string.IsNullOrWhiteSpace(first) ? first.Trim()
        : !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim()
        : "PRODUCTION";
}
