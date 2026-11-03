using MeRon.Core.Models;

namespace MeRon.Core.Orders;

public enum ReconcileKind
{
    /// <summary>Cùng khách, cùng món + số lượng, cùng trạng thái thanh toán.</summary>
    Matched,

    /// <summary>Cùng khách có đơn trong ngày nhưng lệch món / số lượng / thanh toán.</summary>
    Different,

    /// <summary>Dòng sổ chưa có đơn nào của khách trong ngày.</summary>
    OnlyInDraft,

    /// <summary>Đơn trong MeRon mà sổ không có dòng nào ghép được.</summary>
    OnlyInDatabase,

    /// <summary>Dòng sổ chưa ghép xong khách / món - chưa so được.</summary>
    NotComparable,
}

/// <param name="Differences">Chỉ có ở <see cref="ReconcileKind.Different"/>: lệch chỗ nào (sổ so với MeRon).</param>
public sealed record ReconcileEntry(ReconcileKind Kind, BatchLineMatch? Draft, Order? Order, IReadOnlyList<string> Differences);

/// <summary>
/// So khớp bảng nháp Nhập đơn hàng loạt với các đơn đã có trong MeRon của CÙNG ngày giao (bên gọi đã bỏ đơn Huỷ). Ghép theo
/// khách: 1 dòng sổ ứng với 1 đơn; khách có nhiều đơn / nhiều dòng thì ưu tiên cặp giống hệt trước, còn lại ghép theo thứ tự.
/// So món: món trong danh sách theo mã món, món tự do theo tên (không dấu) + đơn giá; số lượng cộng dồn theo từng món.
/// </summary>
public static class BatchReconciler
{
    public static IReadOnlyList<ReconcileEntry> Compare(IReadOnlyList<BatchLineMatch> drafts, IReadOnlyList<Order> orders)
    {
        var entries = new List<ReconcileEntry>();
        var unused = orders.ToList();
        var comparable = new List<BatchLineMatch>();
        foreach (var draft in drafts)
        {
            if (draft.CanSave && draft.Customer is not null)
            {
                comparable.Add(draft);
            }
            else
            {
                entries.Add(new ReconcileEntry(ReconcileKind.NotComparable, draft, null, []));
            }
        }

        // Lượt 1: cặp giống hệt (món + thanh toán) - khách đặt 2 đơn trong ngày thì không ghép nhầm đơn này với dòng kia.
        var pending = new List<BatchLineMatch>();
        foreach (var draft in comparable)
        {
            var same = unused.FirstOrDefault(o => o.UserId == draft.Customer!.Id && Differences(draft, o).Count == 0);
            if (same is not null)
            {
                unused.Remove(same);
                entries.Add(new ReconcileEntry(ReconcileKind.Matched, draft, same, []));
            }
            else
            {
                pending.Add(draft);
            }
        }

        // Lượt 2: còn đơn của khách đó → lệch; không còn → chỉ có trong sổ.
        foreach (var draft in pending)
        {
            var other = unused.FirstOrDefault(o => o.UserId == draft.Customer!.Id);
            if (other is not null)
            {
                unused.Remove(other);
                entries.Add(new ReconcileEntry(ReconcileKind.Different, draft, other, Differences(draft, other)));
            }
            else
            {
                entries.Add(new ReconcileEntry(ReconcileKind.OnlyInDraft, draft, null, []));
            }
        }

        entries.AddRange(unused.Select(o => new ReconcileEntry(ReconcileKind.OnlyInDatabase, null, o, [])));
        return entries;
    }

    /// <summary>Lệch giữa dòng sổ và đơn: từng món (số lượng sổ / MeRon) + trạng thái thanh toán. Rỗng = khớp.</summary>
    public static IReadOnlyList<string> Differences(BatchLineMatch draft, Order order)
    {
        var book = draft.Items
            .GroupBy(i => i.Product is { } p ? $"p{p.Id}" : $"f{TextSearch.Fold(i.Name).Trim()}|{i.FreePrice}")
            .ToDictionary(g => g.Key, g => (Name: g.First().Product?.Name ?? g.First().Name, Quantity: g.Sum(i => i.Source.Quantity)));
        var app = order.Items
            .GroupBy(i => i.ProductId is { } id ? $"p{id}" : $"f{TextSearch.Fold(i.SnapshotName).Trim()}|{i.SnapshotPrice}")
            .ToDictionary(g => g.Key, g => (Name: g.First().SnapshotName ?? string.Empty, Quantity: g.Sum(i => i.Quantity)));

        var differences = new List<string>();
        foreach (var (key, item) in book)
        {
            if (!app.TryGetValue(key, out var other))
            {
                differences.Add($"MeRon thiếu {item.Quantity} {item.Name}");
            }
            else if (other.Quantity != item.Quantity)
            {
                differences.Add($"{item.Name}: sổ {item.Quantity}, MeRon {other.Quantity}");
            }
        }
        foreach (var (key, item) in app.Where(a => !book.ContainsKey(a.Key)))
        {
            differences.Add($"MeRon thừa {item.Quantity} {item.Name}");
        }

        var paid = order.Status == OrderStatus.Completed;
        if (draft.Line.Paid != paid)
        {
            differences.Add(draft.Line.Paid ? "sổ ghi CK, MeRon chưa thanh toán" : "MeRon đã thanh toán, sổ không ghi CK");
        }
        return differences;
    }
}
