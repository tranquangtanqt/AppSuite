using System.Text;
using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Core.Models;

namespace MeRon.Tests;

public class ExportTests
{
    private static MeRonDatabase Imported(TempFolder temp)
    {
        var db = temp.NewDatabase();
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Path)).Resolve()), db);
        return db;
    }

    /// <summary>Toàn bộ nội dung 8 bảng (theo rowid) để so 2 database.</summary>
    internal static string Dump(MeRonDatabase db)
    {
        var builder = new StringBuilder();
        using var connection = db.Open();
        foreach (var table in Schema.BackupTables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table.Name} ORDER BY rowid";
            using var r = command.ExecuteReader();
            builder.AppendLine("## " + table.Name);
            while (r.Read())
            {
                for (var i = 0; i < r.FieldCount; i++)
                {
                    builder.Append(r.IsDBNull(i) ? "<null>" : $"{r.GetValue(i)}:{r.GetValue(i).GetType().Name}").Append('|');
                }
                builder.AppendLine();
            }
        }
        return builder.ToString();
    }

    [Fact]
    public void Export_writes_phone_format_folder_and_files()
    {
        using var temp = new TempFolder();
        var db = Imported(temp);

        var result = BackupExporter.Export(db, temp.Sub("out"), new DateTime(2026, 10, 12, 9, 5, 7));

        Assert.Equal("20261012_090507", result.Name);
        Assert.Equal(Path.Combine(temp.Path, "out", "20261012_090507"), result.Folder);
        Assert.False(Directory.Exists(result.Folder + ".partial"));
        var files = Directory.GetFiles(result.Folder).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(Schema.BackupTables.Select(t => $"{t.FilePrefix}_20261012_090507.tsv").Order(), files);

        var users = File.ReadAllBytes(Path.Combine(result.Folder, "users_20261012_090507.tsv"));
        Assert.NotEqual(0xEF, users[0]); // không BOM
        var text = Encoding.UTF8.GetString(users);
        Assert.StartsWith("id\tname\taddress\tphone\tnote\tcreatedAt\tupdatedAt\n1\tChị An\tW1\t0901\t\t", text);
        Assert.False(text.EndsWith('\n')); // như điện thoại: không xuống dòng cuối file
        // bảng trống: chỉ dòng tiêu đề (điện thoại không nhập được file rỗng)
        Assert.Equal("id\tdate\ttotal\tcreatedAt\tupdatedAt", File.ReadAllText(Path.Combine(result.Folder, "purchases_20261012_090507.tsv")));
        Assert.Equal(2, result.RowCounts["Users"]);
        Assert.Equal(0, result.RowCounts["Purchases"]);
    }

    [Fact]
    public void Export_then_import_gives_identical_data()
    {
        using var temp = new TempFolder();
        var db = Imported(temp);
        new MasterDataRepository(db).SaveCustomer(new Customer(0, "Khách\tcó tab", "W1", "0909", "dòng 1\r\ndòng 2\rdòng 3\nhết"));
        new PurchaseRepository(db).Save(new PurchaseDraft(0, new DateTime(2026, 7, 1), [new("Bột", 1000)]));
        var before = Dump(db);

        var export = BackupExporter.Export(db, temp.Sub("out"), new DateTime(2026, 10, 12, 9, 0, 0));
        var copy = new MeRonDatabase(Path.Combine(temp.Path, "copy", "me_ron.db"));
        copy.EnsureCreated();
        var plan = BackupImporter.Prepare(new LocalFolderBackupSource(export.Folder).Resolve());
        Assert.True(plan.CanImport, string.Join("\n", plan.Errors));
        Assert.Empty(plan.Warnings);
        BackupImporter.Apply(plan, copy);

        // tab / xuống dòng thành dấu cách là thay đổi duy nhất
        var expected = before.Replace("Khách\tcó tab", "Khách có tab").Replace("dòng 1\r\ndòng 2\rdòng 3\nhết", "dòng 1 dòng 2 dòng 3 hết");
        Assert.Equal(expected, Dump(copy));
    }

    [Fact]
    public void Cell_values()
    {
        Assert.Equal(string.Empty, BackupExporter.Cell(null));
        Assert.Equal("1234567", BackupExporter.Cell(1234567L)); // không nhóm nghìn
        Assert.Equal("a b c d", BackupExporter.Cell("a\tb\nc\rd"));
        Assert.Equal("2026-06-27T14:20:53.242992", BackupExporter.Cell("2026-06-27T14:20:53.242992"));
    }

    [Fact]
    public void Wipe_all_clears_8_tables_keeps_a_safety_copy_and_restarts_ids()
    {
        using var temp = new TempFolder();
        var db = Imported(temp);
        db.SetMeta(MeRonDatabase.MetaKeys.LastPickedFolder, @"C:\x");

        var copy = db.WipeAll();

        Assert.All(Schema.BackupTables, t => Assert.Equal(0, db.Count(t.Name)));
        Assert.NotNull(copy);
        Assert.StartsWith("me_ron_truoc-xoa_", Path.GetFileName(copy));
        Assert.Equal(2, new MeRonDatabase(copy!).Count("Users"));
        Assert.Null(db.GetMeta(MeRonDatabase.MetaKeys.LastImportBackupName));
        Assert.Equal(@"C:\x", db.GetMeta(MeRonDatabase.MetaKeys.LastPickedFolder)); // thư mục đã chọn vẫn nhớ
        Assert.Equal(1, new MasterDataRepository(db).SaveCustomer(new Customer(0, "Mới", null, null, null)));
    }

    [Fact]
    public void Wipe_on_empty_database_needs_no_copy()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        Assert.Null(db.WipeAll());
    }
}
