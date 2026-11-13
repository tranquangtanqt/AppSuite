using System.Text.Json;

namespace MeRon.Core.Orders;

/// <summary>Món thứ <see cref="Index"/> (từ 0) của dòng đã chốt là món <see cref="ProductId"/>; null = món tự do (tên + giá như ghi).</summary>
public sealed record BatchPinItem(int Index, long? ProductId);

/// <summary>
/// "Ghim" của 1 dòng Nhập đơn hàng loạt: khách / món đã chọn trong popup Sửa đơn hoặc ô Chọn khách / Chọn món trên thẻ (người
/// dùng chốt 2026-11-13). Chữ dòng chỉ ghi được tên nên đọc lại có thể ra khác (khách trùng tên, món gần trùng tên, món tự do
/// trùng tên món có sẵn); ghim theo id để đọc lại vẫn đúng như đã chọn. Số lượng, giá, CK vẫn đọc từ chữ.
/// </summary>
public sealed record BatchPin(long? CustomerId, IReadOnlyList<BatchPinItem> Items)
{
    public static BatchPin Empty { get; } = new(null, []);

    public BatchPin WithCustomer(long customerId) => this with { CustomerId = customerId };

    public BatchPin WithItem(int index, long? productId) =>
        this with { Items = [.. Items.Where(i => i.Index != index), new BatchPinItem(index, productId)] };

    /// <summary>Ghim theo popup Sửa đơn: mỗi dòng món của popup là 1 món trên dòng chữ, cùng thứ tự (<see cref="BatchOrderParser.Compose"/>).</summary>
    public static BatchPin FromLines(long? customerId, IEnumerable<OrderLineDraft> lines) =>
        new(customerId, lines.Select((line, i) => new BatchPinItem(i, line.IsFree ? null : line.ProductId)).ToList());
}

/// <summary>Dòng số <see cref="Number"/> đã ghim vừa bị sửa tay: chữ <see cref="OldRaw"/> (đang ghim) thành <see cref="NewRaw"/>.</summary>
public sealed record BatchPinEdit(int Number, string OldRaw, string NewRaw);

/// <summary>
/// Các ghim theo chữ dòng (đã Trim). Lưu file cạnh ô chữ nháp (<see cref="FileName"/>) nên tắt / mở app vẫn còn. File hỏng /
/// không có → không có ghim nào (dòng đọc theo chữ như trước).
/// </summary>
public sealed class BatchPins
{
    public const string FileName = "nhap-don-hang-loat.ghim.json";

    private readonly Dictionary<string, BatchPin> _pins = new(StringComparer.Ordinal);

    public int Count => _pins.Count;

    public BatchPin? Get(string raw) => _pins.GetValueOrDefault(raw.Trim());

    public void Set(string raw, BatchPin pin) => _pins[raw.Trim()] = pin;

    public bool Remove(string raw) => _pins.Remove(raw.Trim());

    /// <summary>Bỏ ghim của các dòng không còn trong ô chữ (đã xoá, đã lưu thành đơn), trừ <paramref name="keep"/>. Trả về có bỏ gì không.</summary>
    public bool Prune(IEnumerable<string> lines, IEnumerable<string>? keep = null)
    {
        var alive = lines.Select(l => l.Trim()).Concat(keep ?? []).ToHashSet(StringComparer.Ordinal);
        var dead = _pins.Keys.Where(k => !alive.Contains(k)).ToList();
        dead.ForEach(k => _pins.Remove(k));
        return dead.Count > 0;
    }

    public static BatchPins Load(string path)
    {
        var pins = new BatchPins();
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, BatchPin>>(File.ReadAllText(path)) is { } stored)
            {
                foreach (var (raw, pin) in stored.Where(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value is not null))
                {
                    pins.Set(raw, pin with { Items = pin.Items ?? [] });
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // file hỏng → không ghim
        }
        return pins;
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(_pins, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}
