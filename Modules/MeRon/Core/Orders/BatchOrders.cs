using System.Globalization;
using System.Text.RegularExpressions;
using MeRon.Core.Models;

namespace MeRon.Core.Orders;

/// <summary>1 món trên 1 dòng chữ dán vào: số lượng + tên (chưa ghép với danh sách món). <see cref="Price"/> = giá ghi ngay
/// sau món ("1 phần cá 45k"); <see cref="Unsure"/> = có dấu ? (người / AI chép lại đọc không chắc). <see cref="Text"/> = chữ
/// như ghi trong sổ (cả số lượng, bỏ giá / dấu ?) - tên của món tự do ("5 lạng tôm").</summary>
public sealed record BatchItem(long Quantity, string Name, long? Price, bool Unsure, string Text);

/// <summary>1 dòng = 1 đơn: <c>khách | món; món | giá | CK</c>. <see cref="Number"/> = số dòng (từ 1) trong ô chữ.</summary>
public sealed record BatchLine(int Number, string Raw, string Customer, bool CustomerUnsure, IReadOnlyList<BatchItem> Items, long? Price, bool Paid,
    string? Error = null);

/// <summary>Kết quả đọc cả ô chữ: ngày giao (dòng "Ngày | 29/10", null = không ghi) + các dòng đơn.</summary>
public sealed record BatchText(DateOnly? Day, IReadOnlyList<BatchLine> Lines);

/// <summary>
/// Đọc chữ dán vào trang Nhập đơn hàng loạt - chép từ sổ ghi tay (nhờ Claude đọc ảnh), tin nhắn Zalo hoặc gõ tay. Mỗi dòng 1
/// đơn: <c>khách | món; món | giá | CK</c> (không có "|" thì "khách: món + món"). Món cách nhau bằng ";", "+" hoặc ",";
/// "3 Sữa chua uống", "3sc", "sc x3" đều là 3; giá "45k" = 45.000, "45.000" / "45000"; "CK" = đã chuyển khoản.
/// </summary>
public static partial class BatchOrderParser
{
    [GeneratedRegex(@"^\s*(\d{1,4})\s*(?:x\s+|[×*]\s*)?(\D.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex GluedQuantity();

    [GeneratedRegex(@"^(.*?)\s+(\d+(?:[.,]\d+)?k|\d{1,3}(?:[.,]\d{3})+|\d{4,})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingPrice();

    [GeneratedRegex(@"\s*\bCK\b\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingCk();

    [GeneratedRegex(@"^ng[aà]y\s*[|:]?\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DayLine();

    /// <summary>Tách dòng: \r\n, \n và \r (ô nhập nhiều dòng của WinUI dùng \r) - số dòng khớp với dòng nhìn thấy trong ô.</summary>
    public static string[] SplitLines(string? text) => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    public static BatchText Parse(string? text, DateOnly today)
    {
        DateOnly? day = null;
        var lines = new List<BatchLine>();
        var rawLines = BatchOrderParser.SplitLines(text);
        for (var i = 0; i < rawLines.Length; i++)
        {
            var raw = rawLines[i].Trim();
            if (raw.Length == 0)
            {
                continue;
            }
            if (DayLine().Match(raw) is { Success: true } dayMatch && DateInput.Parse(dayMatch.Groups[1].Value.Trim(), today, nearToday: true) is { } parsed)
            {
                day = parsed;
                continue;
            }
            lines.Add(ParseLine(i + 1, raw));
        }
        return new BatchText(day, lines);
    }

    private static BatchLine ParseLine(int number, string raw)
    {
        string[] parts;
        if (raw.Contains('|'))
        {
            parts = raw.Split('|');
        }
        else if (raw.IndexOf(':') is var colon and > 0)
        {
            parts = [raw[..colon], raw[(colon + 1)..]];
        }
        else
        {
            return new BatchLine(number, raw, raw, false, [], null, false, "Không tách được khách và món - viết dạng: khách | món; món");
        }

        var paid = false;
        long? price = null;
        foreach (var extra in parts.Skip(2).Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (string.Equals(extra, "CK", StringComparison.OrdinalIgnoreCase))
            {
                paid = true;
            }
            else if (ParsePrice(extra) is { } value)
            {
                price = value;
            }
        }

        var itemsText = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        if (TrailingCk().IsMatch(itemsText))
        {
            paid = true;
            itemsText = TrailingCk().Replace(itemsText, string.Empty);
        }
        var items = itemsText.Split([';', '+', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseItem)
            .Where(item => item.Name.Length > 0)
            .ToList();

        var customer = parts[0].Trim();
        var customerUnsure = customer.Contains('?');
        customer = customer.Replace("?", string.Empty).Trim();
        string? error = customer.Length == 0 ? "Thiếu khách" : items.Count == 0 ? "Thiếu món" : null;
        return new BatchLine(number, raw, customer, customerUnsure, items, price, paid, error);
    }

    private static BatchItem ParseItem(string text)
    {
        var unsure = text.Contains('?');
        var rest = text.Replace("?", string.Empty).Trim();
        long? price = null;
        if (TrailingPrice().Match(rest) is { Success: true } priceMatch && ParsePrice(priceMatch.Groups[2].Value) is { } value)
        {
            price = value;
            rest = priceMatch.Groups[1].Value.Trim();
        }
        var written = rest;

        long quantity = 1;
        if (GluedQuantity().Match(rest) is { Success: true } glued)
        {
            quantity = long.Parse(glued.Groups[1].Value, CultureInfo.InvariantCulture);
            rest = glued.Groups[2].Value.Trim();
        }
        else if (QuantityText.Split(rest) is { Quantity: { } q } split)
        {
            quantity = q;
            rest = split.Name.Trim();
        }
        return new BatchItem(Math.Max(1, quantity), rest, price, unsure, written);
    }

    /// <summary>"45k" / "1,5k" → 45000 / 1500; "45.000" / "45,000" / "45000" → 45000. Không phải giá → null.</summary>
    public static long? ParsePrice(string? text)
    {
        var value = (text ?? string.Empty).Trim().Replace(" ", string.Empty);
        if (value.EndsWith('k') || value.EndsWith('K'))
        {
            return decimal.TryParse(value[..^1].Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var thousands) && thousands > 0
                ? (long)Math.Round(thousands * 1000)
                : null;
        }
        value = value.Replace(".", string.Empty).Replace(",", string.Empty);
        return value.Length > 0 && value.All(char.IsAsciiDigit) && long.TryParse(value, out var number) && number > 0 ? number : null;
    }

    /// <summary>Viết lại 1 dòng sau khi chọn khách / món trên bảng nháp (bỏ dấu ? của phần đã chọn).</summary>
    public static string Format(BatchLine line, string? customer = null, int? itemIndex = null, string? itemName = null)
    {
        var items = line.Items.Select((item, i) =>
        {
            var name = i == itemIndex ? itemName! : item.Name;
            var unsure = i != itemIndex && item.Unsure;
            return $"{item.Quantity} {name}{(item.Price is { } p ? " " + PriceText(p) : string.Empty)}{(unsure ? "?" : string.Empty)}";
        });
        var customerText = customer is not null ? Clean(customer) : line.Customer + (line.CustomerUnsure ? "?" : string.Empty);
        return $"{customerText} | {string.Join("; ", items)} | {(line.Price is { } price ? PriceText(price) : string.Empty)} | {(line.Paid ? "CK" : string.Empty)}"
            .TrimEnd(' ', '|');
    }

    /// <summary>Viết lại cả dòng từ popup Sửa đơn: mọi món đã chọn xong nên không còn dấu ? và giá ở cột riêng. Món tự do ghi giá
    /// cả dòng, tên kèm số lượng ("2 phần cá 90k"): đọc lại sẽ là 1 × 90.000, đúng thành tiền đã thấy trên popup.
    /// <paramref name="linePrice"/> = giá ghi ở cột riêng của dòng cũ mà chưa món tự do nào dùng: giữ lại, không xoá chữ người dùng ghi.</summary>
    public static string Compose(string customer, IEnumerable<OrderLineDraft> lines, IReadOnlyDictionary<long, Product> products, bool paid,
        long? linePrice = null)
    {
        var items = lines.Select(line => line.IsFree
            ? $"{(line.Quantity > 1 ? $"{line.Quantity} " : string.Empty)}{Clean(line.FreeName)} {PriceText((line.FreePrice ?? 0) * line.Quantity)}"
            : $"{line.Quantity} {Clean(products[line.ProductId!.Value].Name)}");
        return $"{Clean(customer)} | {string.Join("; ", items)} | {(linePrice is { } price ? PriceText(price) : string.Empty)} | {(paid ? "CK" : string.Empty)}"
            .TrimEnd(' ', '|');
    }

    /// <summary>Giá ở cột riêng của dòng chưa món tự do nào dùng (mọi món tự do đều có giá ghi kèm) - Sửa đơn ghi lại cột này.</summary>
    public static long? UnusedLinePrice(BatchLineMatch match) =>
        match.Line.Price is { } price && !match.Items.Any(i => i.IsFree && i.Source.Price is null) ? price : null;

    /// <summary>Chữ khách ghi vào dòng khi chọn khách: tên; có khách khác trùng tên thì thêm địa chỉ ("Nhung Lê W3") để đọc lại
    /// vẫn ra đúng khách (cả khi bỏ ghim).</summary>
    public static string CustomerText(long id, string? name, string? address, IEnumerable<(long Id, string? Name)> customers)
    {
        var folded = TextSearch.Fold(name).Trim();
        var sameName = customers.Any(c => c.Id != id && TextSearch.Fold(c.Name).Trim() == folded);
        return sameName && !string.IsNullOrWhiteSpace(address) ? $"{name} {address}" : name ?? string.Empty;
    }

    /// <summary>Bỏ các dấu có nghĩa riêng trong dòng chữ, để tên có dấu phẩy không bị tách thành 2 món.</summary>
    private static string Clean(string? text) =>
        string.Join(" ", (text ?? string.Empty).Split(['|', ';', '+', ',', '?', ' '], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Dưới 1.000 phải có "k": số 3 chữ số đứng cuối món không được đọc là giá.</summary>
    internal static string PriceText(long price) =>
        price % 1000 == 0 ? $"{price / 1000}k"
        : price < 1000 ? (price / 1000m).ToString(CultureInfo.InvariantCulture) + "k"
        : price.ToString(CultureInfo.InvariantCulture);

    /// <summary>Thay dòng số <paramref name="number"/> (từ 1) trong ô chữ.</summary>
    public static string ReplaceLine(string text, int number, string newLine)
    {
        var lines = SplitLines(text);
        if (number >= 1 && number <= lines.Length)
        {
            lines[number - 1] = newLine;
        }
        return string.Join("\n", lines);
    }

    /// <summary>Cùng nội dung, không tính kiểu xuống dòng (ô nhập của WinUI đổi \n thành \r).</summary>
    public static bool SameText(string? first, string? second) => SplitLines(first).SequenceEqual(SplitLines(second));

    /// <summary>Dòng số <paramref name="number"/> (từ 1) có còn đúng chữ <paramref name="raw"/> không: ô chữ có thể vừa đổi
    /// mà bảng nháp chưa kịp đọc lại, ghi đè theo số dòng lúc đó sẽ trúng dòng khác.</summary>
    public static bool HasLine(string? text, int number, string raw)
    {
        var lines = SplitLines(text);
        return number >= 1 && number <= lines.Length && lines[number - 1].Trim() == raw;
    }

    /// <summary>Các dòng bị sửa ngay tại chỗ giữa 2 lần đọc: cùng số dòng, chữ cũ đã biến mất, chữ mới chưa từng có. Dòng chỉ bị
    /// đẩy lên / xuống (thêm, xoá dòng khác) không tính.</summary>
    public static IEnumerable<(string OldRaw, string NewRaw)> EditedLines(IReadOnlyList<BatchLine> before, IReadOnlyList<BatchLine> after)
    {
        var beforeRaws = before.Select(l => l.Raw).ToHashSet(StringComparer.Ordinal);
        var afterRaws = after.Select(l => l.Raw).ToHashSet(StringComparer.Ordinal);
        return before.Join(after, l => l.Number, l => l.Number, (oldLine, newLine) => (OldRaw: oldLine.Raw, NewRaw: newLine.Raw))
            .Where(pair => !afterRaws.Contains(pair.OldRaw) && !beforeRaws.Contains(pair.NewRaw));
    }

    /// <summary>Bỏ các dòng đã lưu thành đơn khỏi ô chữ (giữ dòng Ngày và dòng chưa lưu).</summary>
    public static string RemoveLines(string text, IEnumerable<int> numbers)
    {
        var drop = numbers.ToHashSet();
        return string.Join("\n", SplitLines(text).Where((_, i) => !drop.Contains(i + 1)));
    }
}

/// <summary>Bảng chữ viết tắt "sc = sữa chua" (mỗi dòng 1 chữ, không phân biệt hoa thường / dấu): thay từng TỪ trong tên món
/// trước khi ghép với danh sách món ("3 sc uống" → "sữa chua uống").</summary>
public sealed class Abbreviations
{
    public const string FileName = "viet-tat.txt";

    public const string DefaultText = "sc = sữa chua\npm = phô mai\nbb = bánh bao\npana = panacota";

    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public Abbreviations(string? text)
    {
        foreach (var line in BatchOrderParser.SplitLines(text))
        {
            var equal = line.IndexOf('=');
            if (equal > 0 && TextSearch.Fold(line[..equal]).Trim() is { Length: > 0 } key && line[(equal + 1)..].Trim() is { Length: > 0 } value)
            {
                _map[key] = value;
            }
        }
    }

    public int Count => _map.Count;

    public string Expand(string name) =>
        string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => _map.TryGetValue(TextSearch.Fold(word), out var full) ? full : word));
}

/// <summary>1 món đã ghép: <see cref="Product"/> (món trong danh sách) hoặc món tự do có <see cref="FreePrice"/>. Món tự do
/// (người dùng chốt 2026-11-03): tên = chữ như ghi trong sổ kể cả số lượng ("5 lạng tôm"), số lượng 1, đơn giá = giá ghi
/// ("5 lạng tôm 100k" → 1 × 100.000, không phải 5 × 100.000).</summary>
public sealed record BatchItemMatch(BatchItem Source, string Name, Product? Product, IReadOnlyList<Product> Candidates, long? FreePrice)
{
    public bool IsResolved => Product is not null || FreePrice is > 0;

    public bool IsFree => Product is null && FreePrice is > 0;

    public long Quantity => IsFree ? 1 : Source.Quantity;

    /// <summary>Tên lưu vào đơn (món tự do) / hiện trên bảng nháp.</summary>
    public string FreeName => Source.Text;

    public string Label => Product is { } p ? $"{Source.Quantity} {p.Name}" : IsFree ? FreeName : $"{Source.Quantity} {Name}";

    public long UnitPrice => Product?.Price ?? FreePrice ?? 0;
}

/// <summary>1 dòng đã ghép khách + món. <see cref="Problems"/> rỗng = lưu được ngay; <see cref="Warnings"/> = lưu được nhưng
/// nên xem lại (đọc không chắc, trùng đơn) - mặc định không tick.</summary>
public sealed record BatchLineMatch(
    BatchLine Line,
    Customer? Customer,
    IReadOnlyList<Customer> CustomerCandidates,
    IReadOnlyList<BatchItemMatch> Items,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings)
{
    public bool CanSave => Problems.Count == 0;

    /// <summary>Khách / món lấy theo ghim (<see cref="BatchPin"/>) chứ không theo chữ.</summary>
    public bool Pinned { get; init; }

    public long Total => Items.Sum(i => i.UnitPrice * i.Quantity);

    public OrderDraft ToDraft(DateOnly day) => new(0, Customer?.Id, OrderStatus.Shipping, day.ToDateTime(TimeOnly.MinValue), Line.Paid, null, 0, null,
        Items.Select(i => i.Product is { } p
            ? new OrderLineDraft(p.Id, i.Quantity)
            : new OrderLineDraft(null, i.Quantity, true, i.FreeName, i.FreePrice)).ToList());
}

/// <summary>
/// Ghép dòng đã đọc với khách / món trong dữ liệu (không phân biệt hoa thường, dấu). Khách: mọi từ của chữ khách phải là đầu 1 từ trong
/// "Tên + Địa chỉ" của khách ("Hà" không khớp "Thảo") - từ có số (706, W1, 12a09) phải trùng nguyên từ; nhiều khách khớp thì ưu
/// tiên khách trùng hẳn tên.
/// Món (sau khi thay chữ viết tắt): trùng tên (bỏ dấu cách: "phomai" = "Phô mai") → tên bắt đầu bằng → chứa mọi từ; phải ra
/// đúng 1 món. Món không có trong danh sách nhưng có giá ghi kèm → món tự do. Giá ở cột riêng của dòng dùng cho món tự do duy
/// nhất của dòng đó.
/// </summary>
public static class BatchOrderMatcher
{
    /// <summary><paramref name="pin"/> = ghim của dòng (<see cref="BatchPin"/>): khách / món đã ghim mà còn trong dữ liệu thì dùng
    /// thay cho kết quả ghép theo chữ; khách / món đã bị xoá thì ghép theo chữ như thường.</summary>
    public static BatchLineMatch Match(BatchLine line, IReadOnlyList<Customer> customers, IReadOnlyList<Product> products, Abbreviations abbreviations,
        BatchPin? pin = null)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        if (line.Error is not null)
        {
            return new BatchLineMatch(line, null, [], [], [line.Error], []);
        }

        var pinned = false;
        var (customer, candidates) = line.Customer.Length == 0 ? (null, []) : MatchCustomer(line.Customer, customers);
        if (pin?.CustomerId is { } pinnedId && customers.FirstOrDefault(c => c.Id == pinnedId) is { } pinnedCustomer)
        {
            (customer, candidates, pinned) = (pinnedCustomer, [pinnedCustomer], true);
        }
        if (customer is null && line.Customer.Length > 0)
        {
            problems.Add(candidates.Count == 0 ? $"Chưa có khách \"{line.Customer}\"" : $"{candidates.Count} khách khớp \"{line.Customer}\" - chọn 1");
        }
        if (line.CustomerUnsure)
        {
            warnings.Add($"Đọc không chắc tên khách \"{line.Customer}\"");
        }

        var items = line.Items.Select(item =>
        {
            var name = abbreviations.Expand(item.Name);
            // Tên món có giá ở cuối ("Yến chưng 40k"): số đó là tên, không phải giá ghi kèm.
            if (item.Price is { } written && products.FirstOrDefault(p => Key(p.Name) == Key($"{name} {BatchOrderParser.PriceText(written)}")) is { } priced)
            {
                return new BatchItemMatch(item with { Price = null }, name, priced, [priced], null);
            }
            var (product, productCandidates) = MatchProduct(name, products);
            return new BatchItemMatch(item, name, product, productCandidates, product is null ? item.Price : null);
        }).ToList();

        foreach (var pinItem in pin?.Items ?? [])
        {
            if (pinItem.Index < 0 || pinItem.Index >= items.Count)
            {
                continue;
            }
            var current = items[pinItem.Index];
            if (pinItem.ProductId is { } productId)
            {
                if (products.FirstOrDefault(p => p.Id == productId) is { } pinnedProduct)
                {
                    pinned = true;
                    if (current.Product?.Id != productId)
                    {
                        items[pinItem.Index] = new BatchItemMatch(current.Source, current.Name, pinnedProduct, [pinnedProduct], null);
                    }
                }
            }
            else
            {
                // Món tự do đã chốt: không ghép vào món có sẵn tên gần giống ("cá 45k" ≠ món "Cá kho"). Chưa có giá ghi kèm thì lấy giá cột riêng bên dưới.
                pinned = true;
                items[pinItem.Index] = new BatchItemMatch(current.Source, current.Name, null, [], current.Source.Price);
            }
        }

        // Giá ghi ở cột riêng ("1201 | 1 phần cá | 45k | CK") → đơn giá của món tự do duy nhất chưa có giá.
        var unpriced = items.Where(i => !i.IsResolved).ToList();
        if (line.Price is { } linePrice)
        {
            if (unpriced.Count == 1)
            {
                items[items.IndexOf(unpriced[0])] = unpriced[0] with { FreePrice = linePrice };
            }
            else if (unpriced.Count == 0 && items.All(i => i.Source.Price is null))
            {
                warnings.Add($"Giá ghi {VnFormat.Money(linePrice)} chưa dùng - mọi món đều có trong danh sách (tính theo giá món)");
            }
        }

        foreach (var item in items.Where(i => !i.IsResolved))
        {
            problems.Add(item.Candidates.Count > 1
                ? $"\"{item.Name}\": {item.Candidates.Count} món khớp - chọn 1"
                : $"Món lạ \"{item.Name}\" - chọn món, thêm chữ viết tắt, hoặc ghi giá (vd 45k) để làm món tự do");
        }
        foreach (var item in items.Where(i => i.Source.Unsure))
        {
            warnings.Add($"Đọc không chắc \"{item.Source.Name}\"");
        }
        foreach (var item in items.Where(i => i.Product is not null && i.Source.Price is { } p && p != i.Product.Price))
        {
            warnings.Add($"\"{item.Name}\" ghi giá {VnFormat.Money(item.Source.Price)} - tính theo giá món {VnFormat.Money(item.Product!.Price ?? 0)}");
        }
        return new BatchLineMatch(line, customer, candidates, items, problems, warnings) { Pinned = pinned };
    }

    private static (Customer? Match, IReadOnlyList<Customer> Candidates) MatchCustomer(string text, IReadOnlyList<Customer> customers)
    {
        var tokens = Words(text);
        var candidates = customers.Where(c =>
        {
            var hayWords = Words($"{c.Name} {c.Address}");
            return tokens.All(t => t.Any(char.IsAsciiDigit)
                ? hayWords.Contains(t)
                : hayWords.Any(w => w.StartsWith(t, StringComparison.Ordinal)));
        }).ToList();
        if (candidates.Count == 1)
        {
            return (candidates[0], candidates);
        }
        // Trùng nguyên tên trước: tên khách thường kèm số phòng / toà ("w3 Ngọc"), bỏ số đi thì không so được.
        var sameName = candidates.Where(c => Words(c.Name ?? string.Empty).SequenceEqual(tokens)).ToList();
        if (sameName.Count == 1)
        {
            return (sameName[0], candidates);
        }
        var nameOnly = string.Join(" ", tokens.Where(t => !t.Any(char.IsAsciiDigit)));
        var exact = candidates.Where(c => string.Join(" ", Words(c.Name ?? string.Empty)) == (nameOnly.Length > 0 ? nameOnly : string.Join(" ", tokens))).ToList();
        return exact.Count == 1 ? (exact[0], candidates) : (null, candidates);
    }

    private static (Product? Match, IReadOnlyList<Product> Candidates) MatchProduct(string name, IReadOnlyList<Product> products)
    {
        var key = Key(name);
        if (key.Length == 0)
        {
            return (null, []);
        }
        foreach (var step in new Func<Product, bool>[]
                 {
                     p => Key(p.Name) == key,
                     p => Key(p.Name).StartsWith(key, StringComparison.Ordinal),
                     p => TextSearch.MatchesAllWords(p.Name, name),
                 })
        {
            var found = products.Where(step).ToList();
            if (found.Count == 1)
            {
                return (found[0], found);
            }
            if (found.Count > 1)
            {
                return (null, found.Take(20).ToList());
            }
        }
        return (null, []);
    }

    private static string Key(string? text) => TextSearch.Fold(text).Replace(" ", string.Empty);

    private static List<string> Words(string text) =>
        Regex.Split(TextSearch.Fold(text), @"[^\p{L}\p{Nd}]+").Where(w => w.Length > 0).ToList();
}

/// <summary>
/// Ô "Tick hết đơn sẵn sàng" của bảng nháp (người dùng yêu cầu 2026-11-13). Đơn sẵn sàng = lưu được, không cảnh báo, chưa có
/// trong MeRon; dòng cần xem (cảnh báo, trùng, đã có / khác trong MeRon) không tự tick để khỏi lưu trùng.
/// </summary>
public static class BatchCheckAll
{
    /// <summary>true = mọi đơn sẵn sàng đã tick (có ít nhất 1), false = chưa tick dòng nào, null = tick 1 phần.</summary>
    public static bool? State(IReadOnlyList<(bool Ready, bool Checked)> rows)
    {
        if (!rows.Any(r => r.Checked))
        {
            return false;
        }
        return rows.Any(r => r.Ready) && rows.Where(r => r.Ready).All(r => r.Checked) ? true : null;
    }

    /// <summary>Bấm ô: mọi đơn sẵn sàng đã tick (hoặc không có đơn sẵn sàng) mà đang có dòng tick → bỏ tick hết; không thì tick
    /// mọi đơn sẵn sàng, dòng khác giữ nguyên. Trả về tick mới của từng dòng.</summary>
    public static bool[] Toggle(IReadOnlyList<(bool Ready, bool Checked)> rows)
    {
        var untickAll = rows.Any(r => r.Checked) && rows.Where(r => r.Ready).All(r => r.Checked);
        return rows.Select(r => !untickAll && (r.Ready || r.Checked)).ToArray();
    }
}
