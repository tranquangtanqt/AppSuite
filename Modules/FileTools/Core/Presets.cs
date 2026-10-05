using System.Reflection;
using System.Text.Json;

namespace FileTools.Core;

/// <summary>1 mẫu thiết lập đã lưu của 1 trang: tên + giá trị các tuỳ chọn (dạng chuỗi).</summary>
public sealed record Preset(string Name, Dictionary<string, string> Values);

/// <summary>
/// Lưu / nạp mẫu thiết lập (preset) cho từng trang vào 1 file JSON: <c>{ "MergePage": [ { Name, Values } ] }</c>. Mặc định
/// <c>Data\Config\presets.json</c> cạnh exe (cùng quy ước với các module khác). Đọc lỗi (file hỏng) → coi như chưa có
/// mẫu nào, không làm hỏng app.
/// </summary>
public sealed class PresetStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string DefaultPath => System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "Config", "presets.json");

    public string Path { get; } = path;

    public IReadOnlyList<Preset> List(string page) =>
        Load().TryGetValue(page, out var list) ? list.OrderBy(p => p.Name, NaturalComparer.Instance).ToList() : [];

    /// <summary>Lưu (ghi đè mẫu cùng tên, không phân biệt hoa thường).</summary>
    public void Save(string page, Preset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            throw new ArgumentException("Tên mẫu trống.");
        }
        var all = Load();
        if (!all.TryGetValue(page, out var list))
        {
            list = [];
            all[page] = list;
        }
        list.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        list.Add(preset with { Name = preset.Name.Trim() });
        Write(all);
    }

    public bool Delete(string page, string name)
    {
        var all = Load();
        if (!all.TryGetValue(page, out var list) || list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }
        Write(all);
        return true;
    }

    /// <summary>Chuyển mẫu của các trang đã gộp sang trang mới (xem <see cref="PresetMigration"/>); chạy lúc mở app, không có gì
    /// để chuyển thì không ghi file. Trả về số mẫu đã chuyển / sửa.</summary>
    public int MigrateMergedPages()
    {
        var all = Load();
        int changed = PresetMigration.Migrate(all);
        if (changed > 0)
        {
            Write(all);
        }
        return changed;
    }

    private Dictionary<string, List<Preset>> Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<Dictionary<string, List<Preset>>>(File.ReadAllText(Path), Json) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Write(Dictionary<string, List<Preset>> all)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, Json));
        File.Move(temp, Path, overwrite: true);
    }
}

/// <summary>
/// Chụp / áp tuỳ chọn của 1 ViewModel bằng reflection: mọi thuộc tính public đọc-ghi kiểu string / bool / int / double /
/// enum (trừ danh sách loại trừ - trạng thái chạy như IsBusy, Progress...), cộng thuộc tính lồng 1 cấp được chỉ định
/// (vd "Output" → "Output.EncodingIndex"). Giá trị không đọc được / thuộc tính đã đổi tên trong bản mới thì bỏ qua.
/// </summary>
public static class PresetMapper
{
    private static bool Supported(Type t) =>
        t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(double) || t.IsEnum;

    public static Dictionary<string, string> Capture(object target, ISet<string> exclude, IEnumerable<string>? nested = null)
    {
        var values = new Dictionary<string, string>();
        CaptureInto(values, target, string.Empty, exclude);
        foreach (var name in nested ?? [])
        {
            if (target.GetType().GetProperty(name)?.GetValue(target) is { } child)
            {
                CaptureInto(values, child, name + ".", exclude);
            }
        }
        return values;
    }

    /// <param name="applyFirst">Áp các khoá này trước (vd đường dẫn nguồn - đổi nguồn làm trang tự đặt lại đường dẫn kết
    /// quả mặc định, nên nguồn phải đặt trước để giá trị kết quả trong mẫu không bị ghi đè).</param>
    /// <returns>Số giá trị đã áp.</returns>
    public static int Apply(object target, IReadOnlyDictionary<string, string> values, IEnumerable<string>? applyFirst = null)
    {
        int applied = 0;
        var first = applyFirst?.ToList() ?? [];
        var ordered = values.OrderBy(kv => first.IndexOf(kv.Key) is var i && i >= 0 ? i : int.MaxValue);
        foreach (var (key, raw) in ordered)
        {
            object? owner = target;
            var name = key;
            int dot = key.IndexOf('.');
            if (dot > 0)
            {
                owner = target.GetType().GetProperty(key[..dot])?.GetValue(target);
                name = key[(dot + 1)..];
            }
            var prop = owner?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop is not { CanWrite: true } || !Supported(prop.PropertyType) || prop.SetMethod?.IsPublic != true)
            {
                continue;
            }
            try
            {
                object value = prop.PropertyType switch
                {
                    var t when t == typeof(string) => raw,
                    var t when t == typeof(bool) => bool.Parse(raw),
                    var t when t == typeof(int) => int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture),
                    var t when t == typeof(double) => double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture),
                    var t => Enum.Parse(t, raw),
                };
                prop.SetValue(owner, value);
                applied++;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            {
                // Giá trị cũ không hợp lệ → bỏ qua ô đó.
            }
        }
        return applied;
    }

    private static void CaptureInto(Dictionary<string, string> values, object target, string prefix, ISet<string> exclude)
    {
        foreach (var prop in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite || prop.SetMethod?.IsPublic != true || prop.GetIndexParameters().Length > 0
                || !Supported(prop.PropertyType) || exclude.Contains(prop.Name))
            {
                continue;
            }
            var value = prop.GetValue(target);
            values[prefix + prop.Name] = value switch
            {
                null => string.Empty,
                double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
        }
    }
}

/// <summary>
/// 2026-10-05 gộp 8 trang thành 4: Lọc dòng → Tìm / Lọc dòng (SearchPage), Đổi xuống dòng → Đổi encoding / xuống dòng
/// (EncodingPage), Đổi dấu phân cách → Chọn cột / đổi dấu phân cách (ColumnsPage), Trích dòng → Xem file (InfoPage). Mẫu đã
/// lưu của trang cũ chuyển sang trang mới, đổi tên tuỳ chọn cho khớp; trùng tên với mẫu có sẵn thì thêm tên trang cũ.
/// </summary>
public static class PresetMigration
{
    public static readonly IReadOnlyList<(string From, string To, string Label)> Moves =
    [
        ("FilterPage", "SearchPage", "Lọc dòng"),
        ("NewlinePage", "EncodingPage", "Đổi xuống dòng"),
        ("DelimiterPage", "ColumnsPage", "Đổi dấu phân cách"),
        ("ExtractPage", "InfoPage", "Trích dòng"),
    ];

    /// <summary>Sửa <paramref name="all"/> tại chỗ; trả về số mẫu đã chuyển / sửa.</summary>
    public static int Migrate(Dictionary<string, List<Preset>> all)
    {
        int changed = 0;
        // Trang Tìm cũ: ô "Query" (1 từ) → "Terms" (mỗi dòng 1 từ) của trang gộp.
        if (all.TryGetValue("SearchPage", out var search))
        {
            foreach (var p in search.Where(p => p.Values.ContainsKey("Query")))
            {
                Rename(p.Values, "Query", "Terms");
                changed++;
            }
        }
        foreach (var (from, to, label) in Moves)
        {
            if (!all.Remove(from, out var old))
            {
                continue;
            }
            if (!all.TryGetValue(to, out var target))
            {
                target = [];
                all[to] = target;
            }
            foreach (var preset in old)
            {
                var name = preset.Name;
                if (target.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    name = $"{name} ({label})";
                }
                target.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                target.Add(new Preset(name, Convert(from, preset.Values)));
                changed++;
            }
        }
        return changed;
    }

    /// <summary>Giá trị của 1 mẫu trang cũ theo tên tuỳ chọn của trang mới.</summary>
    public static Dictionary<string, string> Convert(string fromPage, IReadOnlyDictionary<string, string> values)
    {
        var result = new Dictionary<string, string>(values);
        switch (fromPage)
        {
            case "NewlinePage":
                // Đổi xuống dòng: TargetIndex 0 = CRLF, 1 = LF; encoding giữ như nguồn.
                if (result.Remove("TargetIndex", out var newline))
                {
                    result["Output.NewlineIndex"] = newline == "1" ? "2" : "1";
                }
                result["Output.EncodingIndex"] = ((int)OutputEncoding.SameAsSource).ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "DelimiterPage":
                // Đổi dấu phân cách: TargetIndex 0..3 (, Tab ; |) → OutputDelimiterIndex 1..4 (0 = giữ như nguồn).
                if (result.Remove("TargetIndex", out var target) && int.TryParse(target, out int t))
                {
                    result["OutputDelimiterIndex"] = (Math.Clamp(t, 0, 3) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                break;
        }
        return result;
    }

    private static void Rename(Dictionary<string, string> values, string from, string to)
    {
        if (values.Remove(from, out var value) && !values.ContainsKey(to))
        {
            values[to] = value;
        }
    }
}
