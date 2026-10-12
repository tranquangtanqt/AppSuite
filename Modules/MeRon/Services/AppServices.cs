using MeRon.Core.Data;
using Microsoft.Extensions.Logging;

namespace MeRon.Services;

/// <summary>Database + repository dùng chung cho mọi trang (module không có DI container - như FileTools).</summary>
public static class AppServices
{
    private static readonly Lazy<MeRonDatabase> LazyDatabase = new(() =>
    {
        var db = new MeRonDatabase(MeRonDatabase.DefaultPath);
        db.EnsureCreated();
        AppLog.For("App").LogInformation("Database: {Path}", db.Path);
        return db;
    });

    public static MeRonDatabase Database => LazyDatabase.Value;

    public static MasterDataRepository MasterData { get; } = new(Database);

    public static OrderRepository Orders { get; } = new(Database);

    public static PurchaseRepository Purchases { get; } = new(Database);

    public static Core.Reports.ReportRepository Reports { get; } = new(Database, Orders);

    /// <summary>Chạy 1 thao tác ghi; trả về thông báo lỗi cho người dùng hoặc null nếu thành công.</summary>
    public static string? Try(string what, Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex) when (ex is MeRonValidationException or Core.Orders.OrderValidationException or PurchaseValidationException)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            AppLog.For("Data").LogError(ex, "{What} lỗi", what);
            return $"{what} lỗi: {ex.Message}";
        }
    }
}
