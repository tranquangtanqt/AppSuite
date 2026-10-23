using MeRon.Core.Models;
using MeRon.Core.Orders;
using Microsoft.Data.Sqlite;

namespace MeRon.Core.Data;

/// <summary>
/// Đọc / ghi đơn hàng. Giống điện thoại (order_local_datasource_impl.dart) trừ vài chỗ sửa lỗi có chủ đích:
/// - lọc theo NGÀY giao (substr 10 ký tự) thay vì so chuỗi ngày giờ - điện thoại bỏ sót đơn lưu dạng "yyyy-MM-dd HH:mm:ss"
///   đúng ngày "Từ ngày";
/// - xoá đơn xoá luôn các dòng món;
/// - Thanh toán toàn bộ chỉ chuyển đơn "Đã lên đơn" (điện thoại chuyển cả đơn đã huỷ).
/// Không phân trang: bản máy tính tải hết đơn khớp bộ lọc (vài nghìn đơn vẫn nhanh).
/// </summary>
public sealed class OrderRepository(MeRonDatabase database)
{
    private const string OrderColumns =
        "O.id, O.userId, U.name, O.status, O.deliveryDatetime, O.paymentDatetime, O.discountValue, O.subTotal, O.total, O.note";

    public IReadOnlyList<Order> Query(OrderFilter filter)
    {
        var where = new List<string>();
        var parameters = new List<(string, object?)>();
        if (filter.Status is { } status)
        {
            where.Add("O.status = $status");
            parameters.Add(("$status", (long)status));
        }
        if (filter.From is { } from)
        {
            where.Add("substr(O.deliveryDatetime, 1, 10) >= $from");
            parameters.Add(("$from", DbDate.Day(from)));
        }
        if (filter.To is { } to)
        {
            where.Add("substr(O.deliveryDatetime, 1, 10) <= $to");
            parameters.Add(("$to", DbDate.Day(to)));
        }
        if (filter.UserId is { } userId)
        {
            where.Add("O.userId = $userId");
            parameters.Add(("$userId", userId));
        }

        // Ngày giao so theo thời điểm thật (2 dạng chuỗi lẫn nhau không so trực tiếp được) - id làm khoá phụ cho ổn định.
        var orderBy = filter.Sort switch
        {
            OrderSort.TotalAscending => "O.total ASC, O.id ASC",
            OrderSort.TotalDescending => "O.total DESC, O.id DESC",
            OrderSort.DeliveryAscending => "julianday(replace(O.deliveryDatetime, 'T', ' ')) ASC, O.id ASC",
            _ => "julianday(replace(O.deliveryDatetime, 'T', ' ')) DESC, O.id DESC",
        };

        var sql = $"SELECT {OrderColumns} FROM Orders AS O LEFT JOIN Users AS U ON O.userId = U.id" +
                  (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty) +
                  $" ORDER BY {orderBy}";

        using var connection = database.Open();
        var orders = Read(connection, sql, parameters.ToArray());
        return AttachItems(connection, orders);
    }

    public Order? Get(long id)
    {
        using var connection = database.Open();
        var orders = Read(connection, $"SELECT {OrderColumns} FROM Orders AS O LEFT JOIN Users AS U ON O.userId = U.id WHERE O.id = $id",
            ("$id", id));
        return AttachItems(connection, orders).FirstOrDefault();
    }

    /// <summary>Đơn gần nhất của khách theo ngày giao (không tính đơn Huỷ, không tính <paramref name="excludeOrderId"/>) - gợi
    /// ý "Lần trước" ở form đơn. Null = khách chưa có đơn nào.</summary>
    public Order? GetLastOrder(long userId, long excludeOrderId = 0)
    {
        using var connection = database.Open();
        var orders = Read(connection,
            $"SELECT {OrderColumns} FROM Orders AS O LEFT JOIN Users AS U ON O.userId = U.id " +
            "WHERE O.userId = $userId AND O.status <> $cancelled AND O.id <> $id " +
            "ORDER BY julianday(replace(O.deliveryDatetime, 'T', ' ')) DESC, O.id DESC LIMIT 1",
            ("$userId", userId), ("$cancelled", (long)OrderStatus.Cancelled), ("$id", excludeOrderId));
        return AttachItems(connection, orders).FirstOrDefault();
    }

    /// <summary>Khách đã có đơn khác (không tính đơn Huỷ, không tính chính đơn đang sửa) trong cùng ngày giao? → hỏi "Cảnh
    /// báo trùng đơn".</summary>
    public bool HasOtherOrderSameDay(long userId, DateOnly day, long excludeOrderId)
    {
        using var connection = database.Open();
        using var command = Command(connection,
            "SELECT COUNT(*) FROM Orders WHERE userId = $userId AND substr(deliveryDatetime, 1, 10) = $day AND status <> $cancelled AND id <> $id",
            [("$userId", userId), ("$day", DbDate.Day(day)), ("$cancelled", (long)OrderStatus.Cancelled), ("$id", excludeOrderId)]);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>Tạo / cập nhật đơn + dòng món trong 1 transaction (cập nhật = xoá hết dòng cũ rồi chèn lại, như điện thoại).
    /// Trả về id đơn.</summary>
    public long Save(OrderDraft draft, IReadOnlyDictionary<long, Product> products)
    {
        OrderCalculator.Validate(draft);
        var subTotal = OrderCalculator.SubTotal(draft.Lines, products);
        var total = OrderCalculator.Total(subTotal, draft.Discount);
        var status = OrderCalculator.StatusOnSave(draft);
        var payment = status == OrderStatus.Completed ? draft.Payment ?? draft.Delivery : (DateTime?)null;
        var now = MeRonDatabase.Now();

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var values = new (string, object?)[]
        {
            ("$userId", draft.UserId), ("$status", (long)status), ("$delivery", DbDate.Format(draft.Delivery)),
            ("$payment", payment is null ? null : DbDate.Format(payment.Value)), ("$discount", draft.Discount),
            ("$subTotal", subTotal), ("$total", total), ("$note", string.IsNullOrWhiteSpace(draft.Note) ? string.Empty : draft.Note.Trim()),
            ("$now", now), ("$id", draft.Id),
        };

        long orderId;
        if (draft.Id == 0)
        {
            Execute(connection,
                "INSERT INTO Orders(userId, status, deliveryDatetime, paymentDatetime, discountValue, subTotal, total, note, createdAt, updatedAt) " +
                "VALUES($userId, $status, $delivery, $payment, $discount, $subTotal, $total, $note, $now, $now)", values);
            orderId = LastId(connection);
        }
        else
        {
            orderId = draft.Id;
            var changed = Execute(connection,
                "UPDATE Orders SET userId = $userId, status = $status, deliveryDatetime = $delivery, paymentDatetime = $payment, " +
                "discountValue = $discount, subTotal = $subTotal, total = $total, note = $note, updatedAt = $now WHERE id = $id", values);
            if (changed == 0)
            {
                throw new MeRonValidationException("Đơn hàng không còn trong dữ liệu (có thể vừa nhập lại backup).");
            }
            Execute(connection, "DELETE FROM OrderItems WHERE orderId = $id", ("$id", orderId));
        }

        foreach (var line in draft.Lines)
        {
            var name = line.IsFree
                ? line.FreeName!.Trim()
                : line.ProductId is { } pid && products.TryGetValue(pid, out var product) ? product.Name : null;
            Execute(connection,
                "INSERT INTO OrderItems(orderId, productId, snapshotName, snapshotPrice, quantity, lineTotal, createdAt, updatedAt) " +
                "VALUES($orderId, $productId, $name, $price, $quantity, $lineTotal, $now, $now)",
                ("$orderId", orderId), ("$productId", line.IsFree ? null : line.ProductId), ("$name", name),
                ("$price", OrderCalculator.UnitPrice(line, products)), ("$quantity", line.Quantity),
                ("$lineTotal", OrderCalculator.LineTotal(line, products)), ("$now", now));
        }

        transaction.Commit();
        return orderId;
    }

    public void SetStatus(long id, OrderStatus status)
    {
        using var connection = database.Open();
        Execute(connection, "UPDATE Orders SET status = $status, updatedAt = $now WHERE id = $id",
            ("$status", (long)status), ("$now", MeRonDatabase.Now()), ("$id", id));
    }

    public void Delete(long id)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, "DELETE FROM OrderItems WHERE orderId = $id", ("$id", id));
        Execute(connection, "DELETE FROM Orders WHERE id = $id", ("$id", id));
        transaction.Commit();
    }

    /// <summary>
    /// "Thanh toán toàn bộ": chuyển mọi đơn Đã lên đơn trong danh sách sang Đã thanh toán (1 transaction). Như điện thoại:
    /// có đơn nào trong danh sách đã thanh toán từ trước thì từ chối cả lần (tránh thanh toán trùng). Đơn Huỷ bỏ qua.
    /// Trả về số đơn đã chuyển.
    /// </summary>
    public int PayAll(IReadOnlyList<Order> orders)
    {
        if (orders.Any(o => o.Status == OrderStatus.Completed))
        {
            throw new MeRonValidationException("Không cho phép thanh toán với đơn có trạng thái đã thanh toán.");
        }
        return PayShipping(orders.Where(o => o.Status == OrderStatus.Shipping).Select(o => o.Id).ToList());
    }

    /// <summary>Chuyển các đơn (theo mã) đang Đã lên đơn sang Đã thanh toán trong 1 lần ghi, ghi ngày thanh toán =
    /// <paramref name="paidAt"/> (mặc định lúc bấm; cùng định dạng form đơn ghi khi tích "Đã thanh toán trước") - đơn đã đổi
    /// trạng thái (đã thanh toán / huỷ ở nơi khác) thì bỏ qua. Trả số đơn đã chuyển. Dùng cho "Thanh toán toàn bộ" (Chi tiết)
    /// và "Thanh toán hết" (Công nợ).</summary>
    public int PayShipping(IReadOnlyList<long> ids, DateTime? paidAt = null)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var now = MeRonDatabase.Now();
        var payment = DbDate.Format(paidAt ?? DateTime.Now);
        var changed = 0;
        foreach (var id in ids)
        {
            changed += Execute(connection,
                "UPDATE Orders SET status = $status, paymentDatetime = $payment, updatedAt = $now WHERE id = $id AND status = $shipping",
                ("$status", (long)OrderStatus.Completed), ("$payment", payment), ("$now", now), ("$id", id), ("$shipping", (long)OrderStatus.Shipping));
        }
        transaction.Commit();
        return changed;
    }

    // ---- tiện ích ----

    private static List<Order> Read(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        using var r = command.ExecuteReader();
        var list = new List<Order>();
        while (r.Read())
        {
            list.Add(new Order(
                r.GetInt64(0),
                r.IsDBNull(1) ? null : r.GetInt64(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                OrderStatusText.FromValue(r.IsDBNull(3) ? null : r.GetInt64(3)),
                r.IsDBNull(4) ? null : Convert.ToString(r.GetValue(4)),
                r.IsDBNull(5) ? null : Convert.ToString(r.GetValue(5)),
                r.IsDBNull(6) ? 0 : r.GetInt64(6),
                r.IsDBNull(7) ? 0 : r.GetInt64(7),
                r.IsDBNull(8) ? 0 : r.GetInt64(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                []));
        }
        return list;
    }

    /// <summary>Dòng món của các đơn (1 truy vấn cho cả danh sách, giữ thứ tự id như điện thoại).</summary>
    private static List<Order> AttachItems(SqliteConnection connection, List<Order> orders)
    {
        if (orders.Count == 0)
        {
            return orders;
        }
        var items = new Dictionary<long, List<OrderItem>>();
        var ids = orders.Select(o => o.Id).ToHashSet();
        var sql = orders.Count <= 500
            ? $"SELECT id, orderId, productId, snapshotName, snapshotPrice, quantity, lineTotal FROM OrderItems WHERE orderId IN ({string.Join(",", ids)}) ORDER BY id"
            : "SELECT id, orderId, productId, snapshotName, snapshotPrice, quantity, lineTotal FROM OrderItems ORDER BY id";
        using (var command = Command(connection, sql, []))
        using (var r = command.ExecuteReader())
        {
            while (r.Read())
            {
                var orderId = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                if (!ids.Contains(orderId))
                {
                    continue;
                }
                if (!items.TryGetValue(orderId, out var list))
                {
                    items[orderId] = list = [];
                }
                list.Add(new OrderItem(r.GetInt64(0), orderId, r.IsDBNull(2) ? null : r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? 0 : r.GetInt64(4), r.IsDBNull(5) ? 0 : r.GetInt64(5), r.IsDBNull(6) ? 0 : r.GetInt64(6)));
            }
        }
        return orders.Select(o => items.TryGetValue(o.Id, out var list) ? o with { Items = list } : o).ToList();
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

    private static long LastId(SqliteConnection connection)
    {
        using var command = Command(connection, "SELECT last_insert_rowid()", []);
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
