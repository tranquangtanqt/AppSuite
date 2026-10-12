using System.Globalization;
using MeRon.Core.Data;
using Microsoft.Data.Sqlite;

namespace MeRon.Core.Import;

/// <summary>Kết quả đọc + kiểm tra cả bộ backup, chưa ghi gì vào database.</summary>
public sealed class ImportPlan
{
    public required BackupFileSet Backup { get; init; }

    public required IReadOnlyList<TsvTable> Tables { get; init; }

    /// <summary>Lỗi chặn import (thiếu file, sai cột, số sai...). Có lỗi thì database giữ nguyên.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>Điều đáng chú ý nhưng vẫn nạp được (cột lạ, đơn trỏ tới khách không còn...).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public bool CanImport => Errors.Count == 0;
}

public sealed record ImportResult(IReadOnlyDictionary<string, int> RowCounts, string? SafetyCopyPath);

/// <summary>
/// Nạp bộ backup TSV vào SQLite theo kiểu THAY TOÀN BỘ: đọc + kiểm tra hết 8 file trước (<see cref="Prepare"/>), chỉ
/// khi không có lỗi mới xoá dữ liệu cũ và ghi dữ liệu mới trong 1 transaction (<see cref="Apply"/>) - lỗi giữa chừng
/// thì rollback, database như trước. Trước khi ghi luôn chép database hiện tại ra Backups\ để lấy lại được.
/// </summary>
public static class BackupImporter
{
    public static ImportPlan Prepare(BackupFileSet backup)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var tables = new List<TsvTable>();

        foreach (var missing in backup.Missing)
        {
            errors.Add($"Thiếu file {missing.FilePrefix}_*.tsv ({missing.Title}).");
        }

        foreach (var (table, file) in backup.Files.OrderBy(f => IndexOf(f.Key)))
        {
            string text;
            try
            {
                text = File.ReadAllText(file, System.Text.Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{table.Title} ({Path.GetFileName(file)}): không đọc được file - {ex.Message}");
                continue;
            }
            var parsed = TsvTable.Parse(table, file, text);
            tables.Add(parsed);
            errors.AddRange(parsed.Errors.Select(e => $"{table.Title} ({Path.GetFileName(file)}): {e}"));
            warnings.AddRange(parsed.Warnings.Select(w => $"{table.Title} ({Path.GetFileName(file)}): {w}"));
        }

        if (errors.Count == 0)
        {
            warnings.AddRange(CheckReferences(tables));
        }

        return new ImportPlan { Backup = backup, Tables = tables, Errors = errors, Warnings = warnings };
    }

    private static int IndexOf(TableDef table)
    {
        for (var i = 0; i < Schema.BackupTables.Count; i++)
        {
            if (Schema.BackupTables[i] == table)
            {
                return i;
            }
        }
        return int.MaxValue;
    }

    /// <summary>Liên kết giữa các bảng (điện thoại không bật khoá ngoại nên có thể lệch) - chỉ cảnh báo, vẫn nạp nguyên văn.</summary>
    private static IEnumerable<string> CheckReferences(IReadOnlyList<TsvTable> tables)
    {
        TsvTable? Get(TableDef def) => tables.FirstOrDefault(t => t.Table == def);

        IEnumerable<string> Check(TableDef child, string column, TableDef parent, string what)
        {
            if (Get(child) is not { } c || Get(parent) is not { } p)
            {
                yield break;
            }
            var index = c.IndexOf(column);
            if (index < 0)
            {
                yield break;
            }
            var keys = p.Keys.ToHashSet();
            var orphans = c.Rows.Select(r => r[index]).Where(v => v is not null && !keys.Contains(v)).ToList();
            if (orphans.Count > 0)
            {
                var sample = string.Join(", ", orphans.Distinct().Take(5).Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
                yield return $"{orphans.Count} dòng {child.Title} trỏ tới {what} không có trong file (vd {column} = {sample}).";
            }
        }

        return Check(Schema.Orders, "userId", Schema.Users, "khách hàng")
            .Concat(Check(Schema.OrderItems, "orderId", Schema.Orders, "đơn hàng"))
            .Concat(Check(Schema.OrderItems, "productId", Schema.Products, "món ăn"))
            .Concat(Check(Schema.Products, "categoryId", Schema.Categories, "danh mục"))
            .Concat(Check(Schema.PurchaseItems, "purchaseId", Schema.Purchases, "phiếu nhập"));
    }

    public static ImportResult Apply(ImportPlan plan, MeRonDatabase database)
    {
        if (!plan.CanImport)
        {
            throw new InvalidOperationException("Bộ backup còn lỗi - không nạp.");
        }

        database.EnsureCreated();
        var safetyCopy = database.MakeSafetyCopy("truoc-import");

        var counts = new Dictionary<string, int>();
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var table in Schema.BackupTables)
        {
            MeRonDatabase.Execute(connection, $"DELETE FROM {table.Name};");
        }
        // Đặt lại bộ đếm AUTOINCREMENT; chèn id tường minh thì SQLite tự nâng bộ đếm theo id lớn nhất.
        MeRonDatabase.Execute(connection,
            $"DELETE FROM sqlite_sequence WHERE name IN ({string.Join(", ", Schema.BackupTables.Select(t => $"'{t.Name}'"))});");

        foreach (var data in plan.Tables)
        {
            counts[data.Table.Name] = data.Rows.Count;
            if (data.Rows.Count == 0)
            {
                continue;
            }
            using var insert = connection.CreateCommand();
            insert.CommandText =
                $"INSERT INTO {data.Table.Name} ({string.Join(", ", data.Columns.Select(c => c.Name))}) " +
                $"VALUES ({string.Join(", ", data.Columns.Select((_, i) => "$p" + i))})";
            var parameters = data.Columns.Select((_, i) => insert.Parameters.Add("$p" + i, SqliteType.Text)).ToArray();
            for (var i = 0; i < data.Columns.Count; i++)
            {
                parameters[i].SqliteType = data.Columns[i].Kind == ColumnKind.Integer ? SqliteType.Integer : SqliteType.Text;
            }
            insert.Prepare();
            foreach (var row in data.Rows)
            {
                for (var i = 0; i < row.Length; i++)
                {
                    parameters[i].Value = row[i] ?? DBNull.Value;
                }
                insert.ExecuteNonQuery();
            }
        }

        MeRonDatabase.SetMeta(connection, MeRonDatabase.MetaKeys.LastImportSource, plan.Backup.Source);
        MeRonDatabase.SetMeta(connection, MeRonDatabase.MetaKeys.LastImportBackupName, plan.Backup.Name);
        MeRonDatabase.SetMeta(connection, MeRonDatabase.MetaKeys.LastImportAt, MeRonDatabase.Now());
        transaction.Commit();

        return new ImportResult(counts, safetyCopy);
    }
}
