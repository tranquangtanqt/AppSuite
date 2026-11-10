using MeRon.Core.Data;
using MeRon.Core.Models;

namespace MeRon.Tests;

/// <summary>Sao lưu hằng ngày khi mở app (2026-11-10).</summary>
public class DailyCopyTests
{
    private static string[] DailyFiles(MeRonDatabase db) =>
        Directory.GetFiles(db.BackupsFolder, $"me_ron_{MeRonDatabase.DailyCopyReason}_*.db").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void Copies_once_per_day_and_keeps_the_latest_14()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        new MasterDataRepository(db).SaveCustomer(new Customer(0, "Chị An", null, null, null));

        var first = db.MakeDailyCopy(new DateTime(2026, 11, 10, 8, 0, 0));
        Assert.NotNull(first);
        Assert.Null(db.MakeDailyCopy(new DateTime(2026, 11, 10, 20, 30, 0))); // mở lại cùng ngày: không chép thêm
        Assert.Equal(["me_ron_hang-ngay_20261110_080000.db"], DailyFiles(db));

        // Bản sao mở được và có dữ liệu.
        var copy = new MeRonDatabase(first!);
        Assert.Equal(1, copy.Count("Users"));

        for (var day = 11; day <= 30; day++)
        {
            Assert.NotNull(db.MakeDailyCopy(new DateTime(2026, 11, day, 9, 0, 0)));
        }
        var files = DailyFiles(db);
        Assert.Equal(MeRonDatabase.KeepDailyCopies, files.Length);
        Assert.Equal("me_ron_hang-ngay_20261117_090000.db", files[0]);
        Assert.Equal("me_ron_hang-ngay_20261130_090000.db", files[^1]);
    }

    [Fact]
    public void Daily_copies_do_not_touch_other_safety_copies_and_skip_an_empty_database()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        Assert.Null(db.MakeDailyCopy(new DateTime(2026, 11, 10)));
        Assert.False(Directory.Exists(db.BackupsFolder));

        new MasterDataRepository(db).SaveCustomer(new Customer(0, "Chị An", null, null, null));
        db.MakeSafetyCopy("truoc-import");
        for (var day = 1; day <= 20; day++)
        {
            db.MakeDailyCopy(new DateTime(2026, 11, day));
        }
        Assert.Single(Directory.GetFiles(db.BackupsFolder, "me_ron_truoc-import_*.db"));
        Assert.Equal(MeRonDatabase.KeepDailyCopies, DailyFiles(db).Length);
    }
}
