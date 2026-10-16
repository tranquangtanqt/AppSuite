using System.Globalization;
using System.Text.Json;
using MeRon.Core.Import;
using MeRon.Core.Models;
using Microsoft.Data.Sqlite;

namespace MeRon.Core.Data;

public sealed record TableRowCount(string Name, string Title, long Rows);

/// <summary>Đơn / phiếu nhập trong 1 năm (<paramref name="Month"/> = 0) hoặc 1 tháng. <paramref name="Sales"/> không tính đơn Huỷ.</summary>
public sealed record PeriodStats(int Year, int Month, long Orders, long CancelledOrders, long Sales, long Purchases, long PurchaseAmount);

public sealed record DataOverview(
    long FileBytes,
    IReadOnlyList<TableRowCount> Tables,
    DateOnly? FirstOrderDay,
    DateOnly? LastOrderDay,
    DateOnly? FirstPurchaseDay,
    DateOnly? LastPurchaseDay,
    IReadOnlyList<PeriodStats> Years,
    long CustomersWithoutOrders,
    long ProductsNeverSold,
    long EmptyCategories);

/// <summary>Dọn dữ liệu cũ: xoá đơn hàng (trừ đơn chưa thanh toán) + phiếu nhập có ngày trước <paramref name="Before"/>; tuỳ
/// chọn xoá thêm khách hàng không còn đơn nào và món không còn dòng đơn nào (chỉ những khách / món tạo trước mốc).</summary>
public sealed record CleanupOptions(DateOnly Before, bool DeleteCustomersWithoutOrders, bool DeleteProductsNeverSold);

/// <summary>Số dòng sẽ xoá (xem trước) hoặc đã xoá. <paramref name="KeptUnpaid"/>: đơn "Đã lên đơn" trước mốc - luôn giữ lại.</summary>
public sealed record CleanupPlan(long Orders, long OrderItems, long OrderAmount, long Purchases, long PurchaseItems, long PurchaseAmount,
    long Customers, long Products, long KeptUnpaid = 0, long KeptUnpaidAmount = 0)
{
    public bool IsEmpty => Orders + Purchases + Customers + Products == 0;
}

/// <summary>1 dòng đã xoá khi dọn: id + createdAt (để biết dòng cùng id sau này có phải chính nó không).</summary>
public sealed record DeletedRow(long Id, string? CreatedAt);

/// <summary>
/// Ghi chú 1 lần dọn - file <see cref="DataMaintenance.RecordFileName"/> nằm trong bộ backup TSV xuất ngay trước khi dọn.
/// Khôi phục chỉ thêm lại đúng các dòng có tên trong đây (không đụng dòng người dùng tự xoá / sửa sau đó).
/// </summary>
public sealed record CleanupRecord(DateTime Time, DateOnly Before, bool KeptUnpaid, Dictionary<string, List<DeletedRow>> Deleted)
{
    public int CountOf(string table) => Deleted.TryGetValue(table, out var rows) ? rows.Count : 0;
}

/// <summary>Kết quả khôi phục. <paramref name="Merged"/>: khách / món đã có lại trùng tên → đơn cũ nối vào cái đang có;
/// <paramref name="NewIds"/>: id cũ đã bị dòng khác chiếm → thêm với id mới.</summary>
public sealed record RestoreResult(IReadOnlyDictionary<string, int> Added, int AlreadyThere, int Merged, int NewIds, int NotInBackup, string? SafetyCopy)
{
    public int AddedOf(string table) => Added.TryGetValue(table, out var n) ? n : 0;
}

/// <summary>
/// Trang "Thông tin dữ liệu": tổng quan dữ liệu đang có (số dòng từng bảng, khoảng ngày, theo năm / tháng, dữ liệu thừa),
/// dọn dữ liệu cũ và khôi phục phần đã dọn. Ngày của đơn = ngày giao (không có thì ngày tạo); của phiếu nhập = ngày nhập
/// (không có thì ngày tạo) - so bằng substr(…, 1, 10) đúng với cả 2 dạng chuỗi ngày của điện thoại.
/// </summary>
public sealed class DataMaintenance(MeRonDatabase database)
{
    public const string RecordFileName = "don-dep.json";

    private const string OrderDay = "substr(COALESCE(NULLIF(O.deliveryDatetime, ''), O.createdAt), 1, 10)";
    private const string PurchaseDay = "substr(COALESCE(NULLIF(P.date, ''), P.createdAt), 1, 10)";
    private static readonly long Cancelled = (long)OrderStatus.Cancelled;
    private static readonly long Unpaid = (long)OrderStatus.Shipping;

    /// <summary>Đơn bị xoá khi dọn: trước mốc và KHÔNG phải "Đã lên đơn" (chưa thanh toán = còn nợ → luôn giữ).</summary>
    private static readonly string OldOrder = $"({OrderDay} < $before AND O.status <> {Unpaid})";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public DataOverview Overview()
    {
        using var connection = database.Open();
        var tables = Schema.BackupTables.Select(t => new TableRowCount(t.Name, t.Title, Scalar(connection, $"SELECT COUNT(*) FROM {t.Name}"))).ToList();
        var (firstOrder, lastOrder) = Span(connection, $"SELECT MIN({OrderDay}), MAX({OrderDay}) FROM Orders AS O");
        var (firstPurchase, lastPurchase) = Span(connection, $"SELECT MIN({PurchaseDay}), MAX({PurchaseDay}) FROM Purchases AS P");
        return new DataOverview(
            File.Exists(database.Path) ? new FileInfo(database.Path).Length : 0,
            tables,
            firstOrder, lastOrder, firstPurchase, lastPurchase,
            Periods(connection, year: null),
            Scalar(connection, "SELECT COUNT(*) FROM Users AS U WHERE NOT EXISTS (SELECT 1 FROM Orders AS O WHERE O.userId = U.id)"),
            Scalar(connection, "SELECT COUNT(*) FROM Products AS P WHERE NOT EXISTS (SELECT 1 FROM OrderItems AS I WHERE I.productId = P.id)"),
            Scalar(connection, "SELECT COUNT(*) FROM Categories AS C WHERE NOT EXISTS (SELECT 1 FROM Products AS P WHERE P.categoryId = C.id)"));
    }

    /// <summary>12 tháng của 1 năm (tháng không có gì vẫn có dòng 0) - bấm 1 năm trong bảng để xem.</summary>
    public IReadOnlyList<PeriodStats> Months(int year)
    {
        using var connection = database.Open();
        var found = Periods(connection, year).ToDictionary(p => p.Month);
        return Enumerable.Range(1, 12).Select(m => found.GetValueOrDefault(m) ?? new PeriodStats(year, m, 0, 0, 0, 0, 0)).ToList();
    }

    public CleanupPlan Plan(CleanupOptions options)
    {
        using var connection = database.Open();
        return Plan(connection, options);
    }

    /// <summary>
    /// Xoá theo <paramref name="options"/> trong 1 transaction (lỗi giữa chừng = không xoá gì). Chép bản sao an toàn .db
    /// trước (Backups\me_ron_truoc-don_…). <paramref name="recordFolder"/>: bộ backup TSV vừa xuất - ghi
    /// <see cref="RecordFileName"/> (các dòng đã xoá) vào đó để khôi phục sau. Trigger đếm "thay đổi chưa xuất" đếm các dòng bị xoá.
    /// </summary>
    public CleanupPlan Cleanup(CleanupOptions options, string? recordFolder = null, DateTime? now = null)
    {
        database.MakeSafetyCopy("truoc-don");
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var plan = Plan(connection, options);
        var before = Day(options.Before);

        // Ghi lại id + createdAt trước khi xoá (khách / món tính sau khi bỏ đơn cũ → chọn trước theo điều kiện "không còn").
        var deleted = new Dictionary<string, List<DeletedRow>>
        {
            ["Orders"] = Rows(connection, $"SELECT O.id, O.createdAt FROM Orders AS O WHERE {OldOrder}", before),
            ["OrderItems"] = Rows(connection, $"SELECT id, createdAt FROM OrderItems WHERE orderId IN (SELECT O.id FROM Orders AS O WHERE {OldOrder})", before),
            ["Purchases"] = Rows(connection, $"SELECT P.id, P.createdAt FROM Purchases AS P WHERE {PurchaseDay} < $before", before),
            ["PurchaseItems"] = Rows(connection, $"SELECT id, createdAt FROM PurchaseItems WHERE purchaseId IN (SELECT P.id FROM Purchases AS P WHERE {PurchaseDay} < $before)", before),
            ["Users"] = options.DeleteCustomersWithoutOrders ? Rows(connection, $"SELECT U.id, U.createdAt FROM Users AS U WHERE {OrphanCustomer}", before) : [],
            ["Products"] = options.DeleteProductsNeverSold ? Rows(connection, $"SELECT P.id, P.createdAt FROM Products AS P WHERE {UnsoldProduct}", before) : [],
        };

        foreach (var (table, rows) in deleted)
        {
            foreach (var chunk in rows.Chunk(500))
            {
                MeRonDatabase.Execute(connection, $"DELETE FROM {table} WHERE id IN ({string.Join(",", chunk.Select(r => r.Id))})");
            }
        }

        if (recordFolder is not null)
        {
            var record = new CleanupRecord(now ?? DateTime.Now, options.Before, KeptUnpaid: true, deleted);
            File.WriteAllText(Path.Combine(recordFolder, RecordFileName), JsonSerializer.Serialize(record, Json));
        }
        transaction.Commit();
        return plan;
    }

    /// <summary>Khách tạo trước mốc và không còn đơn nào sau khi dọn (đơn chưa thanh toán được giữ → khách đó cũng giữ).</summary>
    private static readonly string OrphanCustomer =
        $"substr(COALESCE(U.createdAt, ''), 1, 10) < $before AND NOT EXISTS (SELECT 1 FROM Orders AS O WHERE O.userId = U.id AND NOT {OldOrder})";

    /// <summary>Món tạo trước mốc và không còn dòng đơn nào của đơn còn lại sau khi dọn.</summary>
    private static readonly string UnsoldProduct =
        $"substr(COALESCE(P.createdAt, ''), 1, 10) < $before AND NOT EXISTS (SELECT 1 FROM OrderItems AS I JOIN Orders AS O ON O.id = I.orderId WHERE I.productId = P.id AND NOT {OldOrder})";

    private static CleanupPlan Plan(SqliteConnection connection, CleanupOptions options)
    {
        var before = Day(options.Before);
        long Count(string sql) => Scalar(connection, sql, before);
        return new CleanupPlan(
            Count($"SELECT COUNT(*) FROM Orders AS O WHERE {OldOrder}"),
            Count($"SELECT COUNT(*) FROM OrderItems WHERE orderId IN (SELECT O.id FROM Orders AS O WHERE {OldOrder})"),
            Count($"SELECT COALESCE(SUM(O.total), 0) FROM Orders AS O WHERE {OldOrder} AND O.status <> {Cancelled}"),
            Count($"SELECT COUNT(*) FROM Purchases AS P WHERE {PurchaseDay} < $before"),
            Count($"SELECT COUNT(*) FROM PurchaseItems WHERE purchaseId IN (SELECT P.id FROM Purchases AS P WHERE {PurchaseDay} < $before)"),
            Count($"SELECT COALESCE(SUM(P.total), 0) FROM Purchases AS P WHERE {PurchaseDay} < $before"),
            options.DeleteCustomersWithoutOrders ? Count($"SELECT COUNT(*) FROM Users AS U WHERE {OrphanCustomer}") : 0,
            options.DeleteProductsNeverSold ? Count($"SELECT COUNT(*) FROM Products AS P WHERE {UnsoldProduct}") : 0,
            Count($"SELECT COUNT(*) FROM Orders AS O WHERE {OrderDay} < $before AND O.status = {Unpaid}"),
            Count($"SELECT COALESCE(SUM(O.total), 0) FROM Orders AS O WHERE {OrderDay} < $before AND O.status = {Unpaid}"));
    }

    // ---------- Khôi phục phần đã dọn ----------

    /// <summary>Các lần dọn có thể khôi phục: thư mục con của <paramref name="parent"/> (thư mục "Truoc khi don") có
    /// <see cref="RecordFileName"/>, mới nhất trước.</summary>
    public static IReadOnlyList<(string Folder, CleanupRecord Record)> ListCleanups(string parent)
    {
        if (!Directory.Exists(parent))
        {
            return [];
        }
        var result = new List<(string, CleanupRecord)>();
        foreach (var folder in Directory.EnumerateDirectories(parent))
        {
            if (ReadRecord(folder) is { } record)
            {
                result.Add((folder, record));
            }
        }
        return result.OrderByDescending(r => r.Item2.Time).ToList();
    }

    public static CleanupRecord? ReadRecord(string folder)
    {
        var path = Path.Combine(folder, RecordFileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<CleanupRecord>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Số dòng của lần dọn hiện chưa có lại trong database (cùng id + createdAt) - 0 = đã khôi phục hết.</summary>
    public long Missing(CleanupRecord record)
    {
        using var connection = database.Open();
        return record.Deleted.Sum(t => t.Value.Count(r => !IsSameRow(connection, t.Key, r)));
    }

    /// <summary>
    /// Thêm lại các dòng của 1 lần dọn từ bộ backup TSV <paramref name="folder"/> mà hiện không có trong database - dòng
    /// đang có (kể cả sửa sau khi dọn) giữ nguyên. 1 transaction, bản sao an toàn trước. id cũ bị dòng khác chiếm (vd đã
    /// nhập lại backup điện thoại) → thêm với id mới và nối dòng con theo id mới. Khách / món trùng tên với cái đang có
    /// → không thêm, nối đơn / dòng món vào cái đang có.
    /// </summary>
    public RestoreResult Restore(string folder, CleanupRecord record)
    {
        var files = LocalFolderBackupSource.FindFiles(folder);
        var tables = new Dictionary<string, TsvTable>();
        foreach (var name in new[] { "Users", "Products", "Orders", "OrderItems", "Purchases", "PurchaseItems" })
        {
            if (record.CountOf(name) == 0)
            {
                continue;
            }
            var def = Schema.BackupTables.First(t => t.Name == name);
            if (!files.TryGetValue(def, out var file))
            {
                throw new IOException($"Bộ backup {folder} thiếu file {def.FilePrefix}_*.tsv ({def.Title}).");
            }
            var parsed = TsvTable.Parse(def, file, File.ReadAllText(file, System.Text.Encoding.UTF8));
            if (parsed.Errors.Count > 0)
            {
                throw new IOException($"{def.Title} ({Path.GetFileName(file)}): {parsed.Errors[0]}");
            }
            tables[name] = parsed;
        }

        var copy = database.MakeSafetyCopy("truoc-khoi-phuc");
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var added = new Dictionary<string, int>();
        var maps = new Dictionary<string, Dictionary<long, long>>();
        int already = 0, merged = 0, newIds = 0, notInBackup = 0;

        foreach (var name in new[] { "Users", "Products", "Orders", "OrderItems", "Purchases", "PurchaseItems" })
        {
            if (!tables.TryGetValue(name, out var data))
            {
                continue;
            }
            var map = maps[name] = [];
            var idIndex = data.IndexOf("id");
            var rows = data.Rows.Where(r => r[idIndex] is not null).ToDictionary(r => Convert.ToInt64(r[idIndex], CultureInfo.InvariantCulture));
            foreach (var deleted in record.Deleted[name])
            {
                if (!rows.TryGetValue(deleted.Id, out var row))
                {
                    notInBackup++;
                    continue;
                }
                if (IsSameRow(connection, name, deleted))
                {
                    already++;
                    continue;
                }
                // Khách / món: đã có cái trùng tên (tạo lại sau khi dọn) → dùng cái đó.
                if (name is "Users" or "Products" && SameName(connection, name, Value(data, row, "name")) is { } existing)
                {
                    map[deleted.Id] = existing;
                    merged++;
                    continue;
                }
                var values = data.Columns.Select((c, i) => (c.Name, Value: row[i])).ToList();
                Remap(values, "userId", maps.GetValueOrDefault("Users"));
                Remap(values, "productId", maps.GetValueOrDefault("Products"));
                Remap(values, "orderId", maps.GetValueOrDefault("Orders"));
                Remap(values, "purchaseId", maps.GetValueOrDefault("Purchases"));
                var idTaken = Scalar(connection, $"SELECT COUNT(*) FROM {name} WHERE id = {deleted.Id}") > 0;
                if (idTaken)
                {
                    values.RemoveAll(v => v.Name == "id");
                    newIds++;
                }
                Insert(connection, name, values);
                map[deleted.Id] = idTaken ? Scalar(connection, "SELECT last_insert_rowid()") : deleted.Id;
                added[name] = added.GetValueOrDefault(name) + 1;
            }
        }
        transaction.Commit();
        return new RestoreResult(added, already, merged, newIds, notInBackup, copy);
    }

    private static object? Value(TsvTable data, object?[] row, string column) => data.IndexOf(column) is >= 0 and var i ? row[i] : null;

    private static void Remap(List<(string Name, object? Value)> values, string column, Dictionary<long, long>? map)
    {
        var index = values.FindIndex(v => v.Name == column);
        if (map is null || index < 0 || values[index].Value is null)
        {
            return;
        }
        var old = Convert.ToInt64(values[index].Value, CultureInfo.InvariantCulture);
        if (map.TryGetValue(old, out var now))
        {
            values[index] = (column, now);
        }
    }

    private static bool IsSameRow(SqliteConnection connection, string table, DeletedRow row)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT createdAt FROM {table} WHERE id = $id";
        command.Parameters.AddWithValue("$id", row.Id);
        using var reader = command.ExecuteReader();
        return reader.Read() && (reader.IsDBNull(0) ? null : reader.GetString(0)) == row.CreatedAt;
    }

    private static long? SameName(SqliteConnection connection, string table, object? name)
    {
        if (name is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id FROM {table} WHERE lower(trim(name)) = lower(trim($name)) LIMIT 1";
        command.Parameters.AddWithValue("$name", text);
        return command.ExecuteScalar() is long id ? id : null;
    }

    private static void Insert(SqliteConnection connection, string table, List<(string Name, object? Value)> values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} ({string.Join(", ", values.Select(v => v.Name))}) VALUES ({string.Join(", ", values.Select((_, i) => $"$p{i}"))})";
        for (var i = 0; i < values.Count; i++)
        {
            command.Parameters.AddWithValue($"$p{i}", values[i].Value ?? DBNull.Value);
        }
        command.ExecuteNonQuery();
    }

    // ---------- chung ----------

    /// <summary>Theo năm (<paramref name="year"/> null) hoặc theo tháng của 1 năm; mới nhất trước.</summary>
    private static List<PeriodStats> Periods(SqliteConnection connection, int? year)
    {
        // Gộp đơn và phiếu nhập theo cùng khoá kỳ: "yyyy" hoặc "yyyy-MM".
        var length = year is null ? 4 : 7;
        var filter = year is null ? string.Empty : "WHERE substr(k, 1, 4) = $year";
        var sql = $"""
            SELECT k, SUM(orders), SUM(cancelled), SUM(sales), SUM(purchases), SUM(amount) FROM (
                SELECT substr({OrderDay}, 1, {length}) AS k, COUNT(*) AS orders,
                       SUM(CASE WHEN O.status = {Cancelled} THEN 1 ELSE 0 END) AS cancelled,
                       SUM(CASE WHEN O.status = {Cancelled} THEN 0 ELSE O.total END) AS sales, 0 AS purchases, 0 AS amount
                FROM Orders AS O GROUP BY k
                UNION ALL
                SELECT substr({PurchaseDay}, 1, {length}), 0, 0, 0, COUNT(*), SUM(P.total) FROM Purchases AS P GROUP BY 1
            ) {filter} GROUP BY k ORDER BY k DESC
            """;
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$year", (year ?? 0).ToString("D4", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        var result = new List<PeriodStats>();
        while (reader.Read())
        {
            var key = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (!int.TryParse(key.AsSpan(0, Math.Min(4, key.Length)), NumberStyles.None, CultureInfo.InvariantCulture, out var y))
            {
                continue; // đơn không có ngày nào (dữ liệu hỏng) - không xếp vào năm nào
            }
            var month = key.Length >= 7 && int.TryParse(key.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : 0;
            result.Add(new PeriodStats(y, month, Long(reader, 1), Long(reader, 2), Long(reader, 3), Long(reader, 4), Long(reader, 5)));
        }
        return result;
    }

    private static long Long(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? 0 : reader.GetInt64(i);

    private static string Day(DateOnly value) => DbDate.Day(value);

    private static (DateOnly?, DateOnly?) Span(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        reader.Read();
        DateOnly? Read(int i) => !reader.IsDBNull(i) && DateOnly.TryParseExact(reader.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day) ? day : null;
        return (Read(0), Read(1));
    }

    private static List<DeletedRow> Rows(SqliteConnection connection, string sql, string before)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$before", before);
        using var reader = command.ExecuteReader();
        var rows = new List<DeletedRow>();
        while (reader.Read())
        {
            rows.Add(new DeletedRow(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }
        return rows;
    }

    private static long Scalar(SqliteConnection connection, string sql, string? before = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (before is not null)
        {
            command.Parameters.AddWithValue("$before", before);
        }
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }
}
