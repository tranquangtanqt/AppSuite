using System.Globalization;
using System.Text.Json;
using MeRon.Core.Models;
using Microsoft.Data.Sqlite;

namespace MeRon.Core.History;

/// <summary>Loại thay đổi (lọc ở trang Lịch sử chỉnh sửa): thêm đơn (trước = không có), xoá (sau = không có), còn lại là sửa.</summary>
public enum HistoryKind
{
    Create,
    Edit,
    Delete,
}

/// <summary>1 lần đơn bị thay đổi: trạng thái đơn trước / sau (null = chưa có / đã xoá). <paramref name="Batch"/>: các đơn
/// đổi cùng 1 thao tác (Thanh toán toàn bộ, sửa hàng loạt...) chung 1 mã - khôi phục cả lô được.</summary>
public sealed record HistoryEntry(long Id, DateTime At, string Action, string? Batch, long OrderId, Order? Before, Order? After)
{
    public HistoryKind Kind => Before is null ? HistoryKind.Create : After is null ? HistoryKind.Delete : HistoryKind.Edit;

    /// <summary>Tên khách lúc đó (đơn sau khi đổi, đơn đã xoá thì lúc trước khi xoá).</summary>
    public string? CustomerName => (After ?? Before)?.UserName;

    public DateTime? Delivery => (After ?? Before)?.Delivery;
}

/// <summary>
/// Lịch sử chỉnh sửa đơn hàng trên máy tính (để khôi phục đơn xoá / sửa nhầm). Lưu ở file SQLite riêng
/// me_ron_history.db cạnh database - không nằm trong 8 bảng của backup nên Nhập dữ liệu (thay toàn bộ), Xoá toàn bộ không
/// làm mất; điện thoại không biết file này. Mỗi dòng giữ nguyên đơn (kèm dòng món) trước và sau dạng JSON. Giữ
/// <see cref="Cleanup"/> theo số ngày ở Cài đặt (mặc định 90).
/// </summary>
public sealed class OrderHistory(string path)
{
    public const string FileName = "me_ron_history.db";

    public string Path { get; } = System.IO.Path.GetFullPath(path);

    private bool _created;

    private SqliteConnection Open()
    {
        if (!_created)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        }
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        if (!_created)
        {
            Execute(connection, """
                CREATE TABLE IF NOT EXISTS OrderHistory (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    at TEXT NOT NULL,
                    action TEXT NOT NULL,
                    batch TEXT,
                    orderId INTEGER NOT NULL,
                    before TEXT,
                    after TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_OrderHistory_at ON OrderHistory(at);
                """);
            _created = true;
        }
        return connection;
    }

    /// <summary>Ghi các đơn vừa đổi trong 1 thao tác <paramref name="action"/> (vd "Sửa đơn"). Cặp không đổi gì thì bỏ qua;
    /// từ 2 đơn trở lên thì chung 1 mã lô. Trả số dòng đã ghi.</summary>
    public int Record(string action, IEnumerable<(Order? Before, Order? After)> changes, DateTime now)
    {
        var list = changes.Where(c => !SameContent(c.Before, c.After)).ToList();
        if (list.Count == 0)
        {
            return 0;
        }
        var batch = list.Count > 1 ? Guid.NewGuid().ToString("N") : null;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var (before, after) in list)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO OrderHistory(at, action, batch, orderId, before, after) VALUES($at, $action, $batch, $orderId, $before, $after)";
            command.Parameters.AddWithValue("$at", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue("$batch", (object?)batch ?? DBNull.Value);
            command.Parameters.AddWithValue("$orderId", (before ?? after)!.Id);
            command.Parameters.AddWithValue("$before", before is null ? DBNull.Value : JsonSerializer.Serialize(before));
            command.Parameters.AddWithValue("$after", after is null ? DBNull.Value : JsonSerializer.Serialize(after));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return list.Count;
    }

    /// <summary>Các lần thay đổi trong khoảng ngày (theo lúc sửa, tính cả 2 ngày; null = không giới hạn), mới nhất trước.</summary>
    public IReadOnlyList<HistoryEntry> Query(DateOnly? from = null, DateOnly? to = null)
    {
        var where = new List<string>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        if (from is { } f)
        {
            where.Add("at >= $from");
            command.Parameters.AddWithValue("$from", f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        if (to is { } t)
        {
            where.Add("at < $to");
            command.Parameters.AddWithValue("$to", t.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        command.CommandText = "SELECT id, at, action, batch, orderId, before, after FROM OrderHistory"
                              + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty) + " ORDER BY id DESC";
        return Read(command);
    }

    /// <summary>Mọi dòng cùng lô (thứ tự ghi).</summary>
    public IReadOnlyList<HistoryEntry> Batch(string batch)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, at, action, batch, orderId, before, after FROM OrderHistory WHERE batch = $batch ORDER BY id";
        command.Parameters.AddWithValue("$batch", batch);
        return Read(command);
    }

    public long Count()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM OrderHistory";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>Xoá dòng cũ hơn <paramref name="keepDays"/> ngày (0 = giữ hết). Trả số dòng đã xoá.</summary>
    public int Cleanup(int keepDays, DateTime now)
    {
        if (keepDays <= 0 || !File.Exists(Path))
        {
            return 0;
        }
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM OrderHistory WHERE at < $limit";
        command.Parameters.AddWithValue("$limit", now.Date.AddDays(-keepDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// 2 trạng thái đơn giống nhau về nội dung: bỏ qua tên khách (khách đổi tên không phải sửa đơn) và mã dòng món (Lưu đơn
    /// ghi lại dòng món với mã mới).
    /// </summary>
    public static bool SameContent(Order? a, Order? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        return a.Id == b.Id && a.UserId == b.UserId && a.Status == b.Status && a.DeliveryDatetime == b.DeliveryDatetime
               && a.PaymentDatetime == b.PaymentDatetime && a.DiscountValue == b.DiscountValue && a.SubTotal == b.SubTotal
               && a.Total == b.Total && (a.Note ?? string.Empty) == (b.Note ?? string.Empty)
               && a.Items.Select(ItemKey).SequenceEqual(b.Items.Select(ItemKey));

        static (long?, string?, long, long, long) ItemKey(OrderItem i) => (i.ProductId, i.SnapshotName, i.SnapshotPrice, i.Quantity, i.LineTotal);
    }

    private static List<HistoryEntry> Read(SqliteCommand command)
    {
        var list = new List<HistoryEntry>();
        using var r = command.ExecuteReader();
        while (r.Read())
        {
            list.Add(new HistoryEntry(
                r.GetInt64(0),
                DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetInt64(4),
                r.IsDBNull(5) ? null : JsonSerializer.Deserialize<Order>(r.GetString(5)),
                r.IsDBNull(6) ? null : JsonSerializer.Deserialize<Order>(r.GetString(6))));
        }
        return list;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
