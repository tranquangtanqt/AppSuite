using MeRon.Core.Models;
using Microsoft.Data.Sqlite;

namespace MeRon.Core.Data;

/// <summary>Lỗi ở 1 khoản của phiếu nhập - form đưa con trỏ về đúng ô.</summary>
public sealed class PurchaseValidationException(string message, int? lineIndex = null, bool priceField = false) : Exception(message)
{
    public int? LineIndex { get; } = lineIndex;

    public bool PriceField { get; } = priceField;
}

/// <summary>
/// Phiếu nhập (purchase_local_datasource_impl.dart): lọc theo ngày (cả 2 đầu), mới nhất trước; lưu / sửa phiếu + các khoản
/// trong 1 transaction (sửa = xoá hết khoản cũ rồi chèn lại); xoá phiếu xoá cả khoản. Tổng = cộng giá các khoản.
/// </summary>
public sealed class PurchaseRepository(MeRonDatabase database)
{
    public IReadOnlyList<Purchase> Query(DateOnly? from, DateOnly? to)
    {
        var where = new List<string>();
        var parameters = new List<(string, object?)>();
        if (from is { } f)
        {
            where.Add("substr(date, 1, 10) >= $from");
            parameters.Add(("$from", DbDate.Day(f)));
        }
        if (to is { } t)
        {
            where.Add("substr(date, 1, 10) <= $to");
            parameters.Add(("$to", DbDate.Day(t)));
        }
        var sql = "SELECT id, date, total FROM Purchases" + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty) +
                  " ORDER BY substr(date, 1, 10) DESC, id DESC";
        using var connection = database.Open();
        return Read(connection, sql, parameters.ToArray());
    }

    public Purchase? Get(long id)
    {
        using var connection = database.Open();
        return Read(connection, "SELECT id, date, total FROM Purchases WHERE id = $id", ("$id", id)).FirstOrDefault();
    }

    /// <summary>Phải có ít nhất 1 khoản; mỗi khoản có tên và giá &gt; 0 (nút Lưu của điện thoại tắt khi thiếu).</summary>
    public static void Validate(PurchaseDraft draft)
    {
        if (draft.Lines.Count == 0)
        {
            throw new PurchaseValidationException("Phiếu chưa có khoản nào - bấm Thêm hàng.");
        }
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(draft.Lines[i].Name))
            {
                throw new PurchaseValidationException("Vui lòng nhập tên hàng.", i);
            }
            if ((draft.Lines[i].Price ?? 0) <= 0)
            {
                throw new PurchaseValidationException("Vui lòng nhập giá tiền hợp lệ (lớn hơn 0).", i, priceField: true);
            }
        }
    }

    public static long Total(IEnumerable<PurchaseLineDraft> lines) => lines.Sum(l => l.Price ?? 0);

    public long Save(PurchaseDraft draft)
    {
        Validate(draft);
        var now = MeRonDatabase.Now();
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        long id;
        if (draft.Id == 0)
        {
            Execute(connection, "INSERT INTO Purchases(date, total, createdAt, updatedAt) VALUES($date, $total, $now, $now)",
                ("$date", DbDate.Format(draft.Date)), ("$total", Total(draft.Lines)), ("$now", now));
            using var last = connection.CreateCommand();
            last.CommandText = "SELECT last_insert_rowid()";
            id = Convert.ToInt64(last.ExecuteScalar());
        }
        else
        {
            id = draft.Id;
            if (Execute(connection, "UPDATE Purchases SET date = $date, total = $total, updatedAt = $now WHERE id = $id",
                    ("$date", DbDate.Format(draft.Date)), ("$total", Total(draft.Lines)), ("$now", now), ("$id", id)) == 0)
            {
                throw new MeRonValidationException("Phiếu nhập không còn trong dữ liệu (có thể vừa nhập lại backup).");
            }
            Execute(connection, "DELETE FROM PurchaseItems WHERE purchaseId = $id", ("$id", id));
        }
        foreach (var line in draft.Lines)
        {
            Execute(connection, "INSERT INTO PurchaseItems(purchaseId, name, price, createdAt, updatedAt) VALUES($pid, $name, $price, $now, $now)",
                ("$pid", id), ("$name", line.Name!.Trim()), ("$price", line.Price), ("$now", now));
        }
        transaction.Commit();
        return id;
    }

    public void Delete(long id)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, "DELETE FROM PurchaseItems WHERE purchaseId = $id", ("$id", id));
        Execute(connection, "DELETE FROM Purchases WHERE id = $id", ("$id", id));
        transaction.Commit();
    }

    private static List<Purchase> Read(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        var purchases = new List<Purchase>();
        using (var command = Command(connection, sql, parameters))
        using (var r = command.ExecuteReader())
        {
            while (r.Read())
            {
                purchases.Add(new Purchase(r.GetInt64(0), r.IsDBNull(1) ? null : Convert.ToString(r.GetValue(1)), r.IsDBNull(2) ? 0 : r.GetInt64(2), []));
            }
        }
        if (purchases.Count == 0)
        {
            return purchases;
        }
        var ids = purchases.Select(p => p.Id).ToHashSet();
        var items = new Dictionary<long, List<PurchaseItem>>();
        using (var command = Command(connection, "SELECT id, purchaseId, name, price FROM PurchaseItems ORDER BY id", []))
        using (var r = command.ExecuteReader())
        {
            while (r.Read())
            {
                var pid = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                if (!ids.Contains(pid))
                {
                    continue;
                }
                if (!items.TryGetValue(pid, out var list))
                {
                    items[pid] = list = [];
                }
                list.Add(new PurchaseItem(r.GetInt64(0), pid, r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? 0 : r.GetInt64(3)));
            }
        }
        return purchases.Select(p => items.TryGetValue(p.Id, out var list) ? p with { Items = list } : p).ToList();
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    private static int Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return command.ExecuteNonQuery();
    }
}
