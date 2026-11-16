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
/// Có <paramref name="history"/> thì mọi thao tác ghi (lưu, huỷ, xoá, thanh toán, đổi ngày giao, sửa hàng loạt, khôi phục)
/// đọc đơn trước / sau rồi ghi vào Lịch sử chỉnh sửa - ghi lịch sử lỗi không làm hỏng thao tác (báo qua <see cref="HistoryError"/>).
/// </summary>
public sealed class OrderRepository(MeRonDatabase database, History.OrderHistory? history = null)
{
    /// <summary>Ghi lịch sử chỉnh sửa lỗi (file bị khoá...) - thao tác chính vẫn xong; app ghi log.</summary>
    public Action<Exception>? HistoryError { get; set; }

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
    /// Trả về id đơn. <paramref name="action"/>: tên thao tác ghi vào lịch sử (mặc định Thêm đơn / Sửa đơn).</summary>
    public long Save(OrderDraft draft, IReadOnlyDictionary<long, Product> products, string? action = null) =>
        draft.Id == 0
            ? TrackNew(action ?? "Thêm đơn", () => SaveCore(draft, products))
            : Track(action ?? "Sửa đơn", [draft.Id], () => SaveCore(draft, products));

    private long SaveCore(OrderDraft draft, IReadOnlyDictionary<long, Product> products)
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

    public void SetStatus(long id, OrderStatus status) =>
        Track(status == OrderStatus.Cancelled ? "Huỷ đơn" : "Đổi trạng thái", [id], () =>
        {
            using var connection = database.Open();
            return Execute(connection, "UPDATE Orders SET status = $status, updatedAt = $now WHERE id = $id",
                ("$status", (long)status), ("$now", MeRonDatabase.Now()), ("$id", id));
        });

    /// <summary>Đổi ngày giao (menu chuột phải thẻ đơn: Giao hôm nay / ngày mai), giữ giờ giao cũ. Trả false nếu đơn không còn.</summary>
    public bool SetDeliveryDay(long id, DateOnly day) => Track("Đổi ngày giao", [id], () => SetDeliveryDayCore(id, day));

    private bool SetDeliveryDayCore(long id, DateOnly day)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        string? old;
        using (var command = Command(connection, "SELECT deliveryDatetime FROM Orders WHERE id = $id", [("$id", id)]))
        {
            old = command.ExecuteScalar() as string;
        }
        var time = DbDate.Parse(old) is { } d ? TimeOnly.FromDateTime(d) : TimeOnly.MinValue;
        var changed = Execute(connection, "UPDATE Orders SET deliveryDatetime = $delivery, updatedAt = $now WHERE id = $id",
            ("$delivery", DbDate.Format(day.ToDateTime(time))), ("$now", MeRonDatabase.Now()), ("$id", id));
        transaction.Commit();
        return changed > 0;
    }

    /// <param name="action">Tên thao tác ghi vào lịch sử (mặc định Xoá đơn).</param>
    public void Delete(long id, string? action = null) =>
        Track(action ?? "Xoá đơn", [id], () =>
        {
            using var connection = database.Open();
            using var transaction = connection.BeginTransaction();
            Execute(connection, "DELETE FROM OrderItems WHERE orderId = $id", ("$id", id));
            var deleted = Execute(connection, "DELETE FROM Orders WHERE id = $id", ("$id", id));
            transaction.Commit();
            return deleted;
        });

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
    public int PayShipping(IReadOnlyList<long> ids, DateTime? paidAt = null) => Track("Thanh toán", ids, () => PayShippingCore(ids, paidAt));

    private int PayShippingCore(IReadOnlyList<long> ids, DateTime? paidAt)
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

    /// <summary>Sửa hàng loạt (<see cref="BulkEdit"/>): ghi trạng thái mới của mọi đơn trong 1 transaction - đơn sửa thì ghi
    /// trạng thái, ngày giao / thanh toán, tiền, các dòng món (theo mã dòng, không thêm / bớt dòng); <c>After</c> null thì xoá đơn.
    /// Đơn không còn thì bỏ qua. Trả số đơn đã ghi. <paramref name="action"/>: tên thao tác ghi vào lịch sử.</summary>
    public int ApplyBulk(IReadOnlyList<BulkChange> changes, BulkProductRename? rename = null, string action = "Sửa hàng loạt") =>
        Track(action, changes.Select(c => c.Before.Id).ToList(), () =>
            WriteStates(changes.Select(c => (c.Before.Id, c.After, (Order?)null)).ToList(), rename is null ? null : (rename.ProductId, rename.NewName)));

    /// <summary>Hoàn tác lô vừa sửa: đơn đã sửa ghi lại như trước, đơn đã xoá chèn lại đúng mã đơn / mã dòng món;
    /// <paramref name="rename"/> = trả tên món trong Danh mục về tên cũ.</summary>
    public int RevertBulk(IReadOnlyList<BulkChange> changes, BulkProductRename? rename = null) =>
        Track("Hoàn tác sửa hàng loạt", changes.Select(c => c.Before.Id).ToList(), () =>
            WriteStates(changes.Select(c => (c.Before.Id, (Order?)c.Before, c.After is null ? c.Before : null)).ToList(),
                rename is null ? null : (rename.ProductId, rename.OldName)));

    // ---- Lịch sử chỉnh sửa ----

    /// <summary>Khách còn trong danh sách? (khôi phục đơn của khách đã bị xoá / nhập backup không có khách đó)</summary>
    public bool CustomerExists(long userId)
    {
        using var connection = database.Open();
        using var command = Command(connection, "SELECT COUNT(*) FROM Users WHERE id = $id", [("$id", userId)]);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// Khôi phục (trang Lịch sử chỉnh sửa): ghi lại từng đơn đúng như trước lần thay đổi (<see cref="History.RestorePlan"/>) trong
    /// 1 transaction - đơn còn thì ghi đè, không còn thì chèn lại đúng mã đơn, <see cref="History.RestorePlan.AsNew"/> thì thành
    /// đơn mới; dòng món giữ mã cũ nếu mã đó còn trống. Bản bị chặn (<c>Blocked</c>) bỏ qua. Ghi vào lịch sử là "Khôi phục"
    /// (khôi phục nhầm thì khôi phục lại được). Trả số đơn đã ghi.
    /// </summary>
    public int Restore(IReadOnlyList<History.RestorePlan> plans)
    {
        var usable = plans.Where(p => p.Blocked is null && p.Target is not null).ToList();
        if (usable.Count == 0)
        {
            return 0;
        }
        var before = history is null ? null : TryGetMany(usable.Where(p => !p.AsNew).Select(p => p.Entry.OrderId).ToList());
        var written = new List<(long Id, long? OldId)>();
        using (var connection = database.Open())
        using (var transaction = connection.BeginTransaction())
        {
            foreach (var plan in usable)
            {
                written.Add((WriteOrder(connection, plan.Target!, plan.AsNew), plan.AsNew ? null : plan.Entry.OrderId));
            }
            transaction.Commit();
        }
        if (before is not null)
        {
            RecordSafe("Khôi phục", written.Select(w => w.Id).ToList(),
                written.Where(w => w.OldId is { } old && before.ContainsKey(old)).ToDictionary(w => w.Id, w => before[w.OldId!.Value]));
        }
        return written.Count;
    }

    /// <summary>Ghi cả đơn + dòng món đúng như <paramref name="order"/>. Trả mã đơn.</summary>
    private static long WriteOrder(SqliteConnection connection, Order order, bool asNew)
    {
        var now = MeRonDatabase.Now();
        var values = new (string, object?)[]
        {
            ("$userId", order.UserId), ("$status", (long)order.Status), ("$delivery", order.DeliveryDatetime), ("$payment", order.PaymentDatetime),
            ("$discount", order.DiscountValue), ("$subTotal", order.SubTotal), ("$total", order.Total), ("$note", order.Note ?? string.Empty),
            ("$now", now), ("$id", order.Id),
        };
        long id;
        if (asNew)
        {
            Execute(connection,
                "INSERT INTO Orders(userId, status, deliveryDatetime, paymentDatetime, discountValue, subTotal, total, note, createdAt, updatedAt) " +
                "VALUES($userId, $status, $delivery, $payment, $discount, $subTotal, $total, $note, $now, $now)", values);
            id = LastId(connection);
        }
        else
        {
            id = order.Id;
            var updated = Execute(connection,
                "UPDATE Orders SET userId = $userId, status = $status, deliveryDatetime = $delivery, paymentDatetime = $payment, " +
                "discountValue = $discount, subTotal = $subTotal, total = $total, note = $note, updatedAt = $now WHERE id = $id", values);
            if (updated == 0)
            {
                Execute(connection,
                    "INSERT INTO Orders(id, userId, status, deliveryDatetime, paymentDatetime, discountValue, subTotal, total, note, createdAt, updatedAt) " +
                    "VALUES($id, $userId, $status, $delivery, $payment, $discount, $subTotal, $total, $note, $now, $now)", values);
            }
        }
        Execute(connection, "DELETE FROM OrderItems WHERE orderId = $id", ("$id", id));
        foreach (var item in order.Items)
        {
            Execute(connection,
                "INSERT INTO OrderItems(id, orderId, productId, snapshotName, snapshotPrice, quantity, lineTotal, createdAt, updatedAt) " +
                "VALUES(CASE WHEN EXISTS(SELECT 1 FROM OrderItems WHERE id = $itemId) THEN NULL ELSE $itemId END, $orderId, $productId, $name, $price, $quantity, $lineTotal, $now, $now)",
                ("$itemId", item.Id), ("$orderId", id), ("$productId", item.ProductId), ("$name", item.SnapshotName),
                ("$price", item.SnapshotPrice), ("$quantity", item.Quantity), ("$lineTotal", item.LineTotal), ("$now", now));
        }
        return id;
    }

    /// <summary>Đọc đơn trước, chạy <paramref name="write"/>, đọc lại rồi ghi lịch sử các đơn <paramref name="ids"/>.</summary>
    private T Track<T>(string action, IReadOnlyList<long> ids, Func<T> write)
    {
        var before = history is null ? null : TryGetMany(ids);
        var result = write();
        if (before is not null)
        {
            RecordSafe(action, ids, before);
        }
        return result;
    }

    /// <summary>Thêm đơn mới: trước = không có.</summary>
    private long TrackNew(string action, Func<long> write)
    {
        var id = write();
        if (history is not null)
        {
            RecordSafe(action, [id], new Dictionary<long, Order>());
        }
        return id;
    }

    private Dictionary<long, Order>? TryGetMany(IReadOnlyList<long> ids)
    {
        try
        {
            return GetMany(ids);
        }
        catch (Exception ex)
        {
            HistoryError?.Invoke(ex);
            return null;
        }
    }

    private void RecordSafe(string action, IReadOnlyList<long> ids, IReadOnlyDictionary<long, Order> before)
    {
        try
        {
            var after = GetMany(ids);
            history!.Record(action, ids.Distinct().Select(id => (before.GetValueOrDefault(id), after.GetValueOrDefault(id))), DateTime.Now);
        }
        catch (Exception ex)
        {
            HistoryError?.Invoke(ex);
        }
    }

    private Dictionary<long, Order> GetMany(IReadOnlyList<long> ids)
    {
        var result = new Dictionary<long, Order>();
        using var connection = database.Open();
        foreach (var chunk in ids.Distinct().Chunk(500))
        {
            var orders = Read(connection, $"SELECT {OrderColumns} FROM Orders AS O LEFT JOIN Users AS U ON O.userId = U.id WHERE O.id IN ({string.Join(",", chunk)})");
            foreach (var order in AttachItems(connection, orders))
            {
                result[order.Id] = order;
            }
        }
        return result;
    }

    private int WriteStates(IReadOnlyList<(long Id, Order? State, Order? Insert)> states, (long Id, string Name)? productName = null)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var now = MeRonDatabase.Now();
        var written = 0;
        if (productName is { } product)
        {
            Execute(connection, "UPDATE Products SET name = $name, updatedAt = $now WHERE id = $id",
                ("$name", product.Name), ("$now", now), ("$id", product.Id));
        }
        foreach (var (id, state, insert) in states)
        {
            if (insert is not null)
            {
                Execute(connection,
                    "INSERT INTO Orders(id, userId, status, deliveryDatetime, paymentDatetime, discountValue, subTotal, total, note, createdAt, updatedAt) " +
                    "VALUES($id, $userId, $status, $delivery, $payment, $discount, $subTotal, $total, $note, $now, $now)",
                    ("$id", insert.Id), ("$userId", insert.UserId), ("$status", (long)insert.Status), ("$delivery", insert.DeliveryDatetime),
                    ("$payment", insert.PaymentDatetime), ("$discount", insert.DiscountValue), ("$subTotal", insert.SubTotal), ("$total", insert.Total),
                    ("$note", insert.Note ?? string.Empty), ("$now", now));
                foreach (var item in insert.Items)
                {
                    Execute(connection,
                        "INSERT INTO OrderItems(id, orderId, productId, snapshotName, snapshotPrice, quantity, lineTotal, createdAt, updatedAt) " +
                        "VALUES($id, $orderId, $productId, $name, $price, $quantity, $lineTotal, $now, $now)",
                        ("$id", item.Id), ("$orderId", insert.Id), ("$productId", item.ProductId), ("$name", item.SnapshotName),
                        ("$price", item.SnapshotPrice), ("$quantity", item.Quantity), ("$lineTotal", item.LineTotal), ("$now", now));
                }
                written++;
            }
            else if (state is null)
            {
                Execute(connection, "DELETE FROM OrderItems WHERE orderId = $id", ("$id", id));
                written += Execute(connection, "DELETE FROM Orders WHERE id = $id", ("$id", id));
            }
            else
            {
                var changed = Execute(connection,
                    "UPDATE Orders SET status = $status, deliveryDatetime = $delivery, paymentDatetime = $payment, subTotal = $subTotal, " +
                    "total = $total, updatedAt = $now WHERE id = $id",
                    ("$status", (long)state.Status), ("$delivery", state.DeliveryDatetime), ("$payment", state.PaymentDatetime),
                    ("$subTotal", state.SubTotal), ("$total", state.Total), ("$now", now), ("$id", id));
                if (changed == 0)
                {
                    continue;
                }
                written++;
                foreach (var item in state.Items)
                {
                    Execute(connection,
                        "UPDATE OrderItems SET productId = $productId, snapshotName = $name, snapshotPrice = $price, lineTotal = $lineTotal, updatedAt = $now " +
                        "WHERE id = $itemId AND orderId = $id AND (productId IS NOT $productId OR snapshotName IS NOT $name OR snapshotPrice IS NOT $price OR lineTotal IS NOT $lineTotal)",
                        ("$productId", item.ProductId), ("$name", item.SnapshotName), ("$price", item.SnapshotPrice), ("$lineTotal", item.LineTotal),
                        ("$now", now), ("$itemId", item.Id), ("$id", id));
                }
            }
        }
        transaction.Commit();
        return written;
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
