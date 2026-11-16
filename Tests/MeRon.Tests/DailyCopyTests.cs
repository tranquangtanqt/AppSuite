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

    [Fact]
    public void Keep_count_from_settings_prunes_old_daily_copies()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        new MasterDataRepository(db).SaveCustomer(new Customer(0, "Chị An", null, null, null));
        for (var day = 1; day <= 10; day++)
        {
            db.MakeDailyCopy(new DateTime(2026, 11, day, 8, 0, 0), keep: 30);
        }
        Assert.Equal(10, DailyFiles(db).Length);
        // Giảm còn 3 (Cài đặt): mở lại cùng ngày cũng dọn ngay
        Assert.Null(db.MakeDailyCopy(new DateTime(2026, 11, 10, 20, 0, 0), keep: 3));
        Assert.Equal(["me_ron_hang-ngay_20261108_080000.db", "me_ron_hang-ngay_20261109_080000.db", "me_ron_hang-ngay_20261110_080000.db"], DailyFiles(db));

        // Cài đặt: mặc định 14, giới hạn 1 – 365, lưu / đọc lại
        var settings = new MeRon.Core.MenuSettings();
        Assert.Equal(14, settings.DailyBackupKeep);
        settings.DailyBackupKeep = 0;
        Assert.Equal(1, settings.DailyBackupKeep);
        settings.DailyBackupKeep = 30;
        var path = Path.Combine(temp.Path, "settings.json");
        settings.Save(path);
        Assert.Equal(30, MeRon.Core.MenuSettings.Load(path).DailyBackupKeep);
    }

    [Fact]
    public void Lists_copies_and_restores_one_after_saving_current_data()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        var master = new MasterDataRepository(db);
        master.SaveCustomer(new Customer(0, "Chị An", null, null, null));
        var morning = db.MakeDailyCopy(new DateTime(2026, 11, 16, 8, 0, 0))!;
        master.SaveCustomer(new Customer(0, "Anh Bình", null, null, null));
        Assert.Equal(2, db.Count("Users"));
        File.WriteAllText(Path.Combine(db.BackupsFolder, "ghi-chu.db"), "x"); // file lạ: bỏ qua

        var copies = db.ListCopies();
        var copy = Assert.Single(copies);
        Assert.Equal("Hằng ngày", copy.Label);
        Assert.Equal(new DateTime(2026, 11, 16, 8, 0, 0), copy.Time);
        Assert.Equal(morning, copy.Path);
        Assert.Equal((0L, 1L), MeRonDatabase.CountIn(copy.Path));

        // Khôi phục bản sáng: còn 1 khách; dữ liệu ngay trước đó chép ra bản "truoc-lay-lai" (2 khách)
        var before = db.RestoreCopy(copy.Path, new DateTime(2026, 11, 16, 10, 0, 0));
        Assert.Equal(1, db.Count("Users"));
        Assert.Equal("me_ron_truoc-lay-lai_20261116_100000.db", Path.GetFileName(before));
        Assert.Equal((0L, 2L), MeRonDatabase.CountIn(before!));
        Assert.Equal(["truoc-lay-lai", "hang-ngay"], db.ListCopies().Select(c => c.Reason).ToArray());
        new MasterDataRepository(db).SaveCustomer(new Customer(0, "Chị Cúc", null, null, null)); // vẫn ghi được sau khi khôi phục

        // File không phải database MeRon → báo lỗi, dữ liệu giữ nguyên
        var bad = Path.Combine(db.BackupsFolder, "me_ron_hang-ngay_20261101_080000.db");
        File.WriteAllText(bad, "không phải sqlite");
        Assert.Null(MeRonDatabase.CountIn(bad));
        Assert.Throws<InvalidDataException>(() => db.RestoreCopy(bad, DateTime.Now));
        Assert.Equal(2, db.Count("Users"));

        // Tắt "Tự sao lưu khi thao tác": Nhập / Xoá / Dọn không chép; Khôi phục bản sao vẫn chép bản "trước khi lấy lại"
        db.CopyBeforeActions = false;
        Assert.Null(db.MakeSafetyCopy("truoc-import")); // đang có dữ liệu nhưng tắt → không chép
        Assert.NotNull(db.RestoreCopy(copy.Path, new DateTime(2026, 11, 16, 11, 0, 0)));
        Assert.Null(db.WipeAll());
        Assert.Equal(0, db.Count("Users"));
        Assert.DoesNotContain(db.ListCopies(), c => c.Reason is "truoc-import" or "truoc-xoa");
        db.CopyBeforeActions = true;

        // Cài đặt 2 ô tích: mặc định bật, lưu / đọc lại
        var settings = new MeRon.Core.MenuSettings();
        Assert.True(settings.AutoDailyBackup);
        Assert.True(settings.BackupBeforeActions);
        settings.AutoDailyBackup = false;
        settings.BackupBeforeActions = false;
        var path = Path.Combine(temp.Path, "settings.json");
        settings.Save(path);
        var loaded = MeRon.Core.MenuSettings.Load(path);
        Assert.False(loaded.AutoDailyBackup);
        Assert.False(loaded.BackupBeforeActions);

        Assert.Null(BackupCopy.TryParse("me_ron_.db"));
        Assert.Equal("truoc-import", BackupCopy.TryParse("me_ron_truoc-import_20261102_093000.db")!.Reason);
    }
}
