using Rdbms.HtmlGenerator.Services;

namespace Rdbms.HtmlGenerator.Tests;

/// <summary>Lịch sử các lần đọc: chép .db cũ trước khi ghi đè, liệt kê mới nhất trước, dọn bớt. Mỗi test dùng thư mục tạm riêng.</summary>
public sealed class SchemaHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RdbmsHistoryTests_" + Guid.NewGuid().ToString("N"));

    public SchemaHistoryTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Db(string name, string content, DateTime readAt)
    {
        var path = Path.Combine(_directory, name + ".db");
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, readAt);
        return path;
    }

    [Fact]
    public void Chua_co_file_thi_khong_luu_gi()
    {
        var path = Path.Combine(_directory, "orders.db");

        Assert.Null(SchemaHistory.Save(path));
        Assert.Empty(SchemaHistory.List(path));
        Assert.Null(SchemaHistory.Latest(path));
    }

    [Fact]
    public void Luu_vao_thu_muc_Lich_su_theo_ten_db_va_luc_doc()
    {
        var readAt = new DateTime(2026, 10, 21, 9, 15, 30);
        var db = Db("orders (prod)", "v1", readAt);

        var copy = SchemaHistory.Save(db);

        Assert.Equal(Path.Combine(_directory, "Lịch sử", "orders (prod)", "2026-10-21 091530.db"), copy);
        Assert.Equal("v1", File.ReadAllText(copy!));
        Assert.Equal("v1", File.ReadAllText(db)); // bản gốc giữ nguyên
        var entry = Assert.Single(SchemaHistory.List(db));
        Assert.Equal(readAt, entry.ReadAt);
    }

    [Fact]
    public void Moi_db_co_lich_su_rieng()
    {
        var dev = Db("orders (dev)", "dev", new DateTime(2026, 10, 1, 8, 0, 0));
        var prod = Db("orders (prod)", "prod", new DateTime(2026, 10, 2, 8, 0, 0));

        SchemaHistory.Save(dev);
        SchemaHistory.Save(prod);

        Assert.Equal("dev", File.ReadAllText(Assert.Single(SchemaHistory.List(dev)).Path));
        Assert.Equal("prod", File.ReadAllText(Assert.Single(SchemaHistory.List(prod)).Path));
    }

    [Fact]
    public void Liet_ke_moi_nhat_truoc_va_don_bot()
    {
        var db = Path.Combine(_directory, "orders.db");
        for (var day = 1; day <= 5; day++)
        {
            File.WriteAllText(db, $"v{day}");
            File.SetLastWriteTime(db, new DateTime(2026, 10, day, 10, 0, 0));
            SchemaHistory.Save(db);
        }

        Assert.Equal([5, 4, 3, 2, 1], SchemaHistory.List(db).Select(e => e.ReadAt.Day));
        Assert.Equal("v5", File.ReadAllText(SchemaHistory.Latest(db)!.Path));

        Assert.Equal(2, SchemaHistory.Prune(db, keep: 3));
        Assert.Equal([5, 4, 3], SchemaHistory.List(db).Select(e => e.ReadAt.Day));

        Assert.Equal(3, SchemaHistory.Prune(db, keep: 0));
        Assert.Empty(SchemaHistory.List(db));
    }

    [Fact]
    public void Cung_mot_lan_doc_luu_2_lan_thi_khong_nhan_doi()
    {
        var db = Db("orders", "v1", new DateTime(2026, 10, 21, 9, 0, 0));

        SchemaHistory.Save(db);
        SchemaHistory.Save(db);

        Assert.Single(SchemaHistory.List(db));
    }

    [Fact]
    public void File_ten_la_thi_lay_gio_sua_file()
    {
        var db = Db("orders", "v1", new DateTime(2026, 10, 21, 9, 0, 0));
        var folder = SchemaHistory.FolderFor(db);
        Directory.CreateDirectory(folder);
        var manual = Path.Combine(folder, "ban chep tay.db");
        File.WriteAllText(manual, "old");
        File.SetLastWriteTime(manual, new DateTime(2026, 9, 1, 12, 0, 0));

        var entry = Assert.Single(SchemaHistory.List(db));

        Assert.Equal(new DateTime(2026, 9, 1, 12, 0, 0), entry.ReadAt);
    }
}
