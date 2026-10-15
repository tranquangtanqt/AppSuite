using System.Text;
using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Core.Import;
using MeRon.Core.Models;

namespace MeRon.Tests;

/// <summary>Thư mục tạm cho 1 test - tự xoá khi xong.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MeRonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(string name)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public MeRonDatabase NewDatabase()
    {
        var db = new MeRonDatabase(System.IO.Path.Combine(Path, "db", "me_ron.db"));
        db.EnsureCreated();
        return db;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class Backup
{
    /// <summary>Bộ backup nhỏ, đúng định dạng điện thoại xuất (dữ liệu giả). <paramref name="stamp"/> = tên thư mục / hậu tố file.</summary>
    public static string Write(string parent, string stamp = "20261011_231039", Action<Dictionary<string, string>>? change = null)
    {
        var files = new Dictionary<string, string>
        {
            ["address"] = "code\tname\tcreatedAt\tupdatedAt\nW1\tTòa nhà W1\t2026-06-27 07:20:53\t2026-06-27 07:20:53\n00\tKhác\t2026-06-27 07:20:53\t2026-06-27 07:20:53",
            ["users"] = "id\tname\taddress\tphone\tnote\tcreatedAt\tupdatedAt\n1\tChị An\tW1\t0901\t\t2026-06-27 07:20:53\t2026-06-27 07:20:53\n2\tAnh Bình\t\t\tkhách quen\t2026-06-27 07:20:53\t2026-06-27 07:20:53",
            ["categories"] = "id\tname\tdescription\tcreatedAt\tupdatedAt\n1\tBánh bao\t\t2026-06-27 07:20:53\t2026-06-27 07:20:53\n2\tĐồ uống\t\t2026-06-27 07:20:53\t2026-06-27 07:20:53",
            ["products"] = "id\tcategoryId\tname\timageUrl\tprice\tdescription\tcreatedAt\tupdatedAt\n1\t1\tBánh bao không nhân\t\t5000\t\t2026-06-27 07:20:53\t2026-06-27 07:20:53\n2\t1\tBánh bao thịt trứng\t\t12000\t\t2026-06-29T06:05:56.013540\t2026-06-29T06:05:56.013560",
            ["orders"] = "id\tuserId\tstatus\tdeliveryDatetime\tpaymentDatetime\tdiscountValue\tsubTotal\ttotal\tnote\tcreatedAt\tupdatedAt\n1\t1\t2\t2026-06-27T14:20:53.242992\t2026-06-27T07:20:53.000\t1000\t25000\t24000\t\t2026-06-27T14:27:12.039502\t2026-06-27T14:27:12.039546\n3\t2\t1\t2026-06-28 07:00:00\t\t0\t12000\t12000\tgiao sớm\t2026-06-27 07:20:53\t2026-06-27 07:20:53",
            ["orderitems"] = "id\torderId\tproductId\tsnapshotName\tsnapshotPrice\tquantity\tlineTotal\tcreatedAt\tupdatedAt\n1\t1\t1\tBánh bao không nhân\t5000\t1\t5000\t2026-06-27T14:27:12.039614\t2026-06-27T14:27:12.039629\n2\t1\t2\tBánh bao thịt trứng\t10000\t2\t20000\t2026-06-27T14:27:12.039640\t2026-06-27T14:27:12.039650\n5\t3\t\tBánh flan\t12000\t1\t12000\t2026-06-27 07:20:53\t2026-06-27 07:20:53",
            ["purchases"] = "",
            ["purchaseitems"] = "",
        };
        change?.Invoke(files);
        var folder = System.IO.Path.Combine(parent, stamp);
        Directory.CreateDirectory(folder);
        foreach (var (table, content) in files)
        {
            File.WriteAllText(System.IO.Path.Combine(folder, $"{table}_{stamp}.tsv"), content, new UTF8Encoding(false));
        }
        return folder;
    }
}

public class TsvParseTests
{
    [Fact]
    public void Empty_file_is_an_empty_table()
    {
        var t = TsvTable.Parse(Schema.Purchases, "p.tsv", "");
        Assert.Empty(t.Rows);
        Assert.Empty(t.Errors);
    }

    [Fact]
    public void Reads_values_nulls_and_types()
    {
        var t = TsvTable.Parse(Schema.Users, "u.tsv", "﻿id\tname\taddress\tphone\tnote\tcreatedAt\tupdatedAt\r\n7\tChị An\t\t0901\t\t2026-06-27 07:20:53\t2026-06-27 07:20:53\r\n");
        Assert.Empty(t.Errors);
        var row = Assert.Single(t.Rows);
        Assert.Equal(7L, row[t.IndexOf("id")]);
        Assert.Equal("Chị An", row[t.IndexOf("name")]);
        Assert.Null(row[t.IndexOf("address")]);
        Assert.Equal("0901", row[t.IndexOf("phone")]); // số điện thoại là chữ - giữ số 0 đầu
    }

    [Fact]
    public void Reports_bad_number_wrong_cell_count_and_duplicate_key_with_line_numbers()
    {
        var t = TsvTable.Parse(Schema.Products, "p.tsv",
            "id\tcategoryId\tname\timageUrl\tprice\tdescription\tcreatedAt\tupdatedAt\n" +
            "1\t1\tA\t\tabc\t\t\t\n" +
            "2\t1\tB\t\t5000\n" +
            "3\t1\tC\t\t5000\t\t\t\n" +
            "3\t1\tD\t\t5000\t\t\t\n");
        Assert.Equal(3, t.Errors.Count);
        Assert.Contains("Dòng 2", t.Errors[0]);
        Assert.Contains("price", t.Errors[0]);
        Assert.Contains("Dòng 3", t.Errors[1]);
        Assert.Contains("Dòng 5", t.Errors[2]);
        Assert.Single(t.Rows);
    }

    [Fact]
    public void Missing_key_column_is_an_error_unknown_and_missing_columns_are_warnings()
    {
        var wrong = TsvTable.Parse(Schema.Orders, "o.tsv", "code\tname\nW1\tA");
        Assert.Contains(wrong.Errors, e => e.Contains("thiếu cột khoá"));

        var newer = TsvTable.Parse(Schema.Address, "a.tsv", "code\tname\tlat\nW1\tA\t10.5");
        Assert.Empty(newer.Errors);
        Assert.Contains(newer.Warnings, w => w.Contains("lat"));
        Assert.Contains(newer.Warnings, w => w.Contains("createdAt"));
        Assert.Single(newer.Rows);
    }
}

public class BackupSourceTests
{
    [Fact]
    public void Backup_folder_itself_finds_all_8_files_without_mixing_orders_and_orderitems()
    {
        using var temp = new TempFolder();
        var folder = Backup.Write(temp.Path);

        var set = new LocalFolderBackupSource(folder).Resolve();

        Assert.Equal("20261011_231039", set.Name);
        Assert.Empty(set.Missing);
        Assert.StartsWith("orders_", Path.GetFileName(set.Files[Schema.Orders]));
        Assert.StartsWith("orderitems_", Path.GetFileName(set.Files[Schema.OrderItems]));
    }

    [Fact]
    public void Parent_folder_picks_newest_timestamp_folder_and_skips_bk()
    {
        using var temp = new TempFolder();
        Backup.Write(temp.Path, "20261008_204533");
        Backup.Write(temp.Path, "20261010_202255");
        Backup.Write(temp.Path, "20261012_000000_bk");
        temp.Sub("20261013_000000"); // thư mục rỗng - bỏ qua

        var set = new LocalFolderBackupSource(temp.Path).Resolve();

        Assert.Equal("20261010_202255", set.Name);
    }

    [Fact]
    public void Folder_without_backups_explains()
    {
        using var temp = new TempFolder();
        var ex = Assert.Throws<BackupSourceException>(() => new LocalFolderBackupSource(temp.Path).Resolve());
        Assert.Contains("yyyyMMdd_HHmmss", ex.Message);
    }
}

public class ImporterTests
{
    private static ImportPlan Plan(string folder) => BackupImporter.Prepare(new LocalFolderBackupSource(folder).Resolve());

    [Fact]
    public void Imports_everything_with_original_ids()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        var plan = Plan(Backup.Write(temp.Path));
        Assert.True(plan.CanImport, string.Join("\n", plan.Errors));

        var result = BackupImporter.Apply(plan, db);

        Assert.Equal(2, result.RowCounts["Users"]);
        Assert.Equal(3, result.RowCounts["OrderItems"]);
        Assert.Equal(0, result.RowCounts["Purchases"]);
        Assert.Null(result.SafetyCopyPath); // database đang trống - không cần bản sao
        Assert.Equal(3, db.Count("OrderItems"));
        Assert.Equal("20261011_231039", db.GetMeta(MeRonDatabase.MetaKeys.LastImportBackupName));
        var customers = new MasterDataRepository(db).GetCustomers();
        Assert.Equal([1L, 2L], customers.Select(c => c.Id));
        Assert.Equal("0901", customers[0].Phone);
    }

    [Fact]
    public void Second_import_replaces_all_data_and_keeps_a_safety_copy()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        BackupImporter.Apply(Plan(Backup.Write(temp.Path)), db);
        new MasterDataRepository(db).SaveCustomer(new Customer(0, "Khách nhập trên máy tính", null, null, null));
        Assert.Equal(3, db.Count("Users"));

        var smaller = Backup.Write(temp.Sub("new"), "20261012_080000",
            f => f["users"] = "id\tname\taddress\tphone\tnote\tcreatedAt\tupdatedAt\n9\tChị Cúc\t\t\t\t\t");
        var result = BackupImporter.Apply(Plan(smaller), db);

        var customers = new MasterDataRepository(db).GetCustomers();
        Assert.Equal("Chị Cúc", Assert.Single(customers).Name);
        Assert.NotNull(result.SafetyCopyPath);
        Assert.True(File.Exists(result.SafetyCopyPath));
        Assert.Equal(3, new MeRonDatabase(result.SafetyCopyPath!).Count("Users"));
    }

    [Fact]
    public void Plan_with_errors_never_touches_the_database()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        BackupImporter.Apply(Plan(Backup.Write(temp.Path)), db);

        var broken = Backup.Write(temp.Sub("bad"), "20261012_080000", f =>
        {
            f.Remove("products");
            f["orders"] += "\nxx\t1\t1\t\t\t\t\t\t\t\t";
        });
        var plan = Plan(broken);

        Assert.False(plan.CanImport);
        Assert.Contains(plan.Errors, e => e.Contains("products_"));
        Assert.Contains(plan.Errors, e => e.Contains("không phải số nguyên"));
        Assert.Throws<InvalidOperationException>(() => BackupImporter.Apply(plan, db));
        Assert.Equal(2, db.Count("Users"));
        Assert.Equal(2, db.Count("Products"));
    }

    [Fact]
    public void Orphan_references_are_warnings_not_errors()
    {
        using var temp = new TempFolder();
        var plan = Plan(Backup.Write(temp.Path, change: f =>
            f["orders"] += "\n4\t99\t1\t2026-06-28 07:00:00\t\t0\t0\t0\t\t\t"));

        Assert.True(plan.CanImport);
        Assert.Contains(plan.Warnings, w => w.Contains("khách hàng") && w.Contains("99"));
    }

    [Fact]
    public void New_rows_after_import_continue_after_the_largest_imported_id()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        BackupImporter.Apply(Plan(Backup.Write(temp.Path)), db);

        var id = new MasterDataRepository(db).SaveCustomer(new Customer(0, "Mới", null, null, null));

        Assert.Equal(3, id);
    }
}

public class MasterDataTests
{
    [Fact]
    public void Customer_name_must_be_unique_ignoring_case_but_editing_itself_is_fine()
    {
        using var temp = new TempFolder();
        var repo = new MasterDataRepository(temp.NewDatabase());
        var id = repo.SaveCustomer(new Customer(0, "Chị An", null, null, null));

        var ex = Assert.Throws<MeRonValidationException>(() => repo.SaveCustomer(new Customer(0, "  chị an ", null, null, null)));
        Assert.Contains("đã tồn tại", ex.Message);
        repo.SaveCustomer(new Customer(id, "Chị An", "W1", "0901", null));
        repo.SaveCustomer(new Customer(0, "Chị Ân", null, null, null)); // khác dấu = khác tên, như điện thoại
        Assert.Equal(2, repo.GetCustomers().Count);
    }

    [Fact]
    public void Cannot_delete_customer_with_orders_or_category_with_products()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Path)).Resolve()), db);
        var repo = new MasterDataRepository(db);

        Assert.Contains("đơn hàng", Assert.Throws<MeRonValidationException>(() => repo.DeleteCustomer(1)).Message);
        Assert.Contains("món ăn", Assert.Throws<MeRonValidationException>(() => repo.DeleteCategory(1)).Message);
        repo.DeleteCategory(2);
        Assert.Single(repo.GetCategories());
    }

    [Fact]
    public void Address_code_is_unique_and_product_needs_name_and_price()
    {
        using var temp = new TempFolder();
        var repo = new MasterDataRepository(temp.NewDatabase());
        repo.AddAddress(new Address("W1", "Tòa nhà W1"));

        Assert.Throws<MeRonValidationException>(() => repo.AddAddress(new Address("w1", "khác")));
        Assert.Throws<MeRonValidationException>(() => repo.SaveProduct(new Product(0, null, " ", null, 5000, null)));
        Assert.Throws<MeRonValidationException>(() => repo.SaveProduct(new Product(0, null, "A", null, -1, null)));
        var id = repo.SaveProduct(new Product(0, null, "Bánh flan", "https://x/a.png", 12000, null));
        repo.SaveProduct(new Product(id, null, "Bánh flan", null, 15000, null));
        var product = Assert.Single(repo.GetProducts());
        Assert.Equal(15000, product.Price);
        Assert.Equal("https://x/a.png", product.ImageUrl); // ảnh của điện thoại giữ nguyên khi sửa trên máy tính
    }

    [Fact]
    public void Search_ignores_case_and_vietnamese_marks()
    {
        Assert.True(TextSearch.Matches("Bánh bao thịt trứng", "banh bao"));
        Assert.True(TextSearch.Matches("Đồ uống", "do uong"));
        Assert.True(TextSearch.Matches("Anything", "  "));
        Assert.False(TextSearch.Matches("Sữa chua", "kem"));
    }

    [Fact]
    public void Product_search_matches_every_word_in_any_order_and_ranks_prefix_first()
    {
        Assert.True(TextSearch.MatchesAllWords("Bánh bao xá xíu/trứng sữa", "bao xiu"));
        Assert.True(TextSearch.MatchesAllWords("Bánh bao xá xíu/trứng sữa", "  TRUNG   banh "));
        Assert.True(TextSearch.MatchesAllWords("Bất kỳ", ""));
        Assert.False(TextSearch.MatchesAllWords("Bánh bao xá xíu/trứng sữa", "bao thit"));

        string[] products = ["Rau câu/chè", "Bánh canh", "Chè bưởi", "Bánh bao chè?", "Panna cotta"];
        Assert.Equal(["Chè bưởi", "Rau câu/chè", "Bánh bao chè?"], TextSearch.Filter(products, p => p, "che", 50));
        Assert.Equal(["Rau câu/chè", "Bánh canh"], TextSearch.Filter(products, p => p, " ", 2));
        Assert.Empty(TextSearch.Filter(products, p => p, "che buoi xanh", 50));
    }
}

/// <summary>Đếm thay đổi trên máy tính chưa xuất (cảnh báo trước khi nhập đè, 2026-10-15).</summary>
public class UnsyncedEditTests
{
    private static void Sql(MeRonDatabase db, string sql)
    {
        using var connection = db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Edits_are_counted_until_export_import_or_wipe()
    {
        using var temp = new TempFolder();
        var db = temp.NewDatabase();
        Assert.False(db.GetUnsyncedEdits().Any);

        // Nhập backup: hàng nghìn dòng nạp vào không tính là "sửa trên máy tính"
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Sub("in"))).Resolve()), db);
        Assert.Equal(0, db.GetUnsyncedEdits().Count);

        // Sửa 1 khách, thêm 1 địa chỉ, xoá địa chỉ đó: mọi đường ghi đều đếm (không cần sửa từng repository)
        Sql(db, "UPDATE Users SET note = 'đã sửa' WHERE id = 1");
        Sql(db, "INSERT INTO Address (code, name) VALUES ('W9', 'Toà W9')");
        Sql(db, "DELETE FROM Address WHERE code = 'W9'");
        var edits = db.GetUnsyncedEdits();
        Assert.Equal(3, edits.Count);
        Assert.NotNull(edits.Since);
        Assert.True(edits.Last >= edits.Since);

        // Xuất → mọi thay đổi đã nằm trong bản xuất
        BackupExporter.Export(db, temp.Sub("out"), DateTime.Now);
        Assert.False(db.GetUnsyncedEdits().Any);

        Sql(db, "UPDATE Users SET note = 'sửa lần 2' WHERE id = 1");
        Assert.Equal(1, db.GetUnsyncedEdits().Count);
        BackupImporter.Apply(BackupImporter.Prepare(new LocalFolderBackupSource(Backup.Write(temp.Sub("in2"))).Resolve()), db);
        Assert.False(db.GetUnsyncedEdits().Any);
        Assert.Null(db.GetMeta(MeRonDatabase.MetaKeys.EditTrackingOff)); // trigger bật lại sau khi nhập

        Sql(db, "UPDATE Users SET note = 'sửa lần 3' WHERE id = 1");
        Assert.True(db.GetUnsyncedEdits().Any);
        db.WipeAll();
        Assert.False(db.GetUnsyncedEdits().Any);
    }
}
