using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DbTools.Core;

public enum MockKind
{
    Integer,
    Decimal,
    Text,
    Date,
    Timestamp,
    Boolean,
    Uuid,
    Other,
}

/// <summary>Kiểu cột rút gọn để sinh dữ liệu: đọc từ format_type của PostgreSQL ("numeric(15,2)", "character varying(200)"...)
/// hoặc đoán từ giá trị mẫu trong file CSV.</summary>
public sealed partial record MockType(MockKind Kind, int? Length = null, int? Precision = null, int? Scale = null, string Display = "")
{
    /// <summary>Giá trị cần đặt trong '...' khi viết INSERT.</summary>
    public bool Quoted => Kind is not (MockKind.Integer or MockKind.Decimal or MockKind.Boolean);

    [GeneratedRegex(@"\((\d+)(?:\s*,\s*(\d+))?\)")]
    private static partial Regex ArgsRegex();

    public static MockType Parse(string formatType)
    {
        var t = formatType.Trim().ToLowerInvariant();
        var m = ArgsRegex().Match(t);
        int? a = m.Success ? int.Parse(m.Groups[1].Value) : null;
        int? b = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : null;
        var kind = t switch
        {
            _ when t.EndsWith("[]") => MockKind.Other,
            "smallint" or "integer" or "bigint" or "int" or "int2" or "int4" or "int8" => MockKind.Integer,
            _ when t.StartsWith("numeric") || t.StartsWith("decimal") || t is "real" or "double precision" or "money" => MockKind.Decimal,
            _ when t.StartsWith("character") || t.StartsWith("varchar") || t.StartsWith("char") || t is "text" or "bpchar" => MockKind.Text,
            "date" => MockKind.Date,
            _ when t.StartsWith("timestamp") => MockKind.Timestamp,
            "boolean" or "bool" => MockKind.Boolean,
            "uuid" => MockKind.Uuid,
            _ => MockKind.Other,
        };
        return kind switch
        {
            MockKind.Text => new MockType(kind, Length: a, Display: formatType),
            MockKind.Decimal => new MockType(kind, Precision: a, Scale: t.StartsWith("numeric") || t.StartsWith("decimal") ? b ?? (a is null ? null : 0) : null, Display: formatType),
            _ => new MockType(kind, Display: formatType),
        };
    }

    [GeneratedRegex(@"^-?\d+$")]
    private static partial Regex IntRegex();

    [GeneratedRegex(@"^-?\d+\.(\d+)$")]
    private static partial Regex DecimalRegex();

    /// <summary>Đoán kiểu từ giá trị mẫu (file CSV): toàn số nguyên / số thập phân / ngày / ngày giờ / true-false, còn lại là chuỗi.</summary>
    public static MockType Infer(IEnumerable<string?> samples)
    {
        var values = samples.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList();
        if (values.Count == 0)
        {
            return new MockType(MockKind.Text, Display: "text");
        }
        if (values.All(v => IntRegex().IsMatch(v) && !(v.Length > 1 && v.StartsWith('0'))))
        {
            return new MockType(MockKind.Integer, Display: "số nguyên");
        }
        if (values.All(v => IntRegex().IsMatch(v) || DecimalRegex().IsMatch(v)) && values.Any(v => v.Contains('.')))
        {
            var scale = values.Select(v => DecimalRegex().Match(v)).Where(x => x.Success).Max(x => x.Groups[1].Value.Length);
            return new MockType(MockKind.Decimal, Scale: scale, Display: $"số thập phân ({scale} số lẻ)");
        }
        if (values.All(v => v.Length <= 10 && MockValues.TryDate(v, out _)))
        {
            return new MockType(MockKind.Date, Display: "ngày");
        }
        if (values.All(v => MockValues.TryDate(v, out _)))
        {
            return new MockType(MockKind.Timestamp, Display: "ngày giờ");
        }
        if (values.All(v => v.ToLowerInvariant() is "true" or "false" or "t" or "f"))
        {
            return new MockType(MockKind.Boolean, Display: "true / false");
        }
        return new MockType(MockKind.Text, Display: "chuỗi");
    }
}

/// <summary>Cách sinh 1 cột.</summary>
public enum MockRule
{
    /// <summary>Không ghi cột này (DB tự sinh: identity, serial, cột tính).</summary>
    Skip,
    /// <summary>Tăng dần từ giá trị bắt đầu: số (1001, 1002...) hoặc mã giữ tiền tố / số chữ số (ORD000124, ORD000125...).</summary>
    Sequence,
    /// <summary>Số ngẫu nhiên trong khoảng "min..max".</summary>
    Number,
    /// <summary>Ngày / ngày giờ ngẫu nhiên trong khoảng "yyyy-MM-dd..yyyy-MM-dd".</summary>
    DateRange,
    /// <summary>Chọn ngẫu nhiên trong danh sách "A|B|C".</summary>
    Pick,
    /// <summary>Chuỗi theo mẫu có {n} = số thứ tự dòng (1, 2, 3...), vd "khách {n}".</summary>
    Pattern,
    /// <summary>Giữ nguyên 1 giá trị cho mọi dòng.</summary>
    Constant,
    Null,
    Boolean,
    Uuid,
    /// <summary>Chọn ngẫu nhiên trong các giá trị đang có ở bảng được tham chiếu (khoá ngoại).</summary>
    ForeignKey,
}

/// <summary>1 cột + cách sinh (đã chỉnh trên trang). <paramref name="Values"/>: danh sách giá trị của khoá ngoại.</summary>
public sealed record MockColumnSpec(string Name, MockType Type, bool NotNull, MockRule Rule, string Param, IReadOnlyList<string>? Values = null);

public sealed class MockError(string message) : Exception(message);

public static class MockValues
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-M-d", "yyyy/M/d", "yyyyMMdd",
        "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy/MM/dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFF", "yyyy/MM/dd H:mm:ss", "yyyy/M/d H:mm", "yyyy/M/d H:mm:ss",
    ];

    public static bool TryDate(string text, out DateTime value)
    {
        var t = text.Trim();
        var plus = t.IndexOfAny(['+']);
        if (plus > 10)
        {
            t = t[..plus]; // bỏ múi giờ "+09"
        }
        return DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    public static bool TryNumber(string text, out decimal value) =>
        decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Tách "a..b" (khoảng số / ngày).</summary>
    public static (string Min, string Max)? Range(string param)
    {
        var i = param.IndexOf("..", StringComparison.Ordinal);
        return i < 0 ? null : (param[..i].Trim(), param[(i + 2)..].Trim());
    }
}

/// <summary>Đề xuất cách sinh cho 1 cột từ kiểu, khoá và dữ liệu mẫu (người dùng chỉnh lại được).</summary>
public static partial class MockSuggest
{
    [GeneratedRegex(@"^(.*?)(\d+)(\D*)$")]
    private static partial Regex CodeRegex();

    /// <param name="existingMax">Giá trị lớn nhất đang có trong DB (khoá chính / duy nhất) - Tăng dần bắt đầu sau đó.</param>
    /// <param name="dbGenerated">Identity / serial (DEFAULT nextval) / cột tính: DB tự sinh → Bỏ qua.</param>
    public static (MockRule Rule, string Param) For(string name, MockType type, bool notNull, bool unique, bool dbGenerated,
        IReadOnlyList<string?> samples, string? existingMax, bool hasForeignValues)
    {
        if (dbGenerated)
        {
            return (MockRule.Skip, string.Empty);
        }
        if (hasForeignValues)
        {
            return (MockRule.ForeignKey, string.Empty);
        }
        var values = samples.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList();
        if (type.Kind == MockKind.Uuid)
        {
            return (MockRule.Uuid, string.Empty);
        }
        if (type.Kind == MockKind.Boolean)
        {
            return values.Count > 0 && values.Distinct().Count() == 1 && !unique ? (MockRule.Constant, values[0]) : (MockRule.Boolean, string.Empty);
        }
        if (unique)
        {
            return UniqueRule(type, values, existingMax);
        }
        if (values.Count == 0)
        {
            return notNull ? DefaultFor(name, type) : (MockRule.Null, string.Empty);
        }
        switch (type.Kind)
        {
            case MockKind.Integer or MockKind.Decimal:
            {
                var nums = values.Select(v => MockValues.TryNumber(v, out var d) ? d : (decimal?)null).Where(d => d is not null).Select(d => d!.Value).ToList();
                if (nums.Count == 0)
                {
                    return (MockRule.Constant, values[0]);
                }
                var (lo, hi) = (nums.Min(), nums.Max());
                // 1, 2, 3... (cột id của file CSV - không biết khoá) → tiếp tục tăng dần thay vì số ngẫu nhiên dễ trùng.
                if (type.Kind == MockKind.Integer && nums.Count >= 2 && nums.Distinct().Count() == nums.Count && hi - lo == nums.Count - 1)
                {
                    return (MockRule.Sequence, MockValues.Number(hi + 1));
                }
                if (nums.Distinct().Count() <= 3 && values.Count > nums.Distinct().Count())
                {
                    return (MockRule.Pick, string.Join('|', values.Distinct()));
                }
                if (lo == hi)
                {
                    (lo, hi) = lo >= 0 ? (0m, lo == 0 ? 10m : lo * 2) : (lo * 2, 0m);
                }
                return (MockRule.Number, $"{MockValues.Number(lo)}..{MockValues.Number(hi)}");
            }
            case MockKind.Date or MockKind.Timestamp:
            {
                var dates = values.Select(v => MockValues.TryDate(v, out var d) ? d : (DateTime?)null).Where(d => d is not null).Select(d => d!.Value).ToList();
                return dates.Count == 0
                    ? (MockRule.Constant, values[0])
                    : (MockRule.DateRange, $"{dates.Min().AddDays(-30):yyyy-MM-dd}..{dates.Max().AddDays(30):yyyy-MM-dd}");
            }
            case MockKind.Text:
            {
                var distinct = values.Distinct().ToList();
                // Cờ / mã ngắn (del_flg, kbn, char(1)...) hoặc giá trị lặp lại → chọn trong các giá trị đã có.
                if (type.Length is <= 3 || distinct.Count < values.Count || values.All(v => v.Length <= 2))
                {
                    return (MockRule.Pick, string.Join('|', distinct));
                }
                // Mã cùng tiền tố, số ở cuối (A001, A002) → tăng dần giữ số chữ số (A003...) thay vì A1, A2.
                var codes = values.Select(v => CodeRegex().Match(v)).ToList();
                if (codes.All(m => m.Success && m.Groups[3].Value.Length == 0) && codes.Select(m => m.Groups[1].Value).Distinct().Count() == 1)
                {
                    return (MockRule.Sequence, Next(values.OrderBy(CodeNumber).Last(), 1));
                }
                return (MockRule.Pattern, PatternOf(values[0], type.Length));
            }
            default:
                return (MockRule.Constant, values[0]);
        }
    }

    private static (MockRule, string) UniqueRule(MockType type, List<string> values, string? existingMax)
    {
        if (type.Kind == MockKind.Integer)
        {
            var max = values.Concat(existingMax is null ? [] : [existingMax])
                .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (long?)null).Max() ?? 0;
            return (MockRule.Sequence, (max + 1).ToString(CultureInfo.InvariantCulture));
        }
        if (type.Kind == MockKind.Text)
        {
            var codes = values.Concat(existingMax is null ? [] : [existingMax]).Where(v => CodeRegex().IsMatch(v)).ToList();
            if (codes.Count > 0)
            {
                var top = codes.OrderBy(v => CodeNumber(v)).Last();
                return (MockRule.Sequence, Next(top, 1));
            }
            return (MockRule.Pattern, PatternOf(values.FirstOrDefault() ?? "ma", type.Length));
        }
        if (type.Kind is MockKind.Date or MockKind.Timestamp)
        {
            return (MockRule.DateRange, $"{DateTime.Today.AddYears(-1):yyyy-MM-dd}..{DateTime.Today:yyyy-MM-dd}");
        }
        return (MockRule.Pattern, PatternOf(values.FirstOrDefault() ?? "x", type.Length));
    }

    private static (MockRule, string) DefaultFor(string name, MockType type) => type.Kind switch
    {
        MockKind.Integer => (MockRule.Number, "1..1000"),
        MockKind.Decimal => (MockRule.Number, "0..1000"),
        MockKind.Date or MockKind.Timestamp => (MockRule.DateRange, $"{DateTime.Today.AddYears(-1):yyyy-MM-dd}..{DateTime.Today:yyyy-MM-dd}"),
        _ => (MockRule.Pattern, PatternOf(name, type.Length)),
    };

    /// <summary>Mẫu chuỗi có {n}: số ở cuối thay bằng {n} ("khách 12" → "khách {n}"), không có số thì thêm "_{n}".
    /// Cột có độ dài giới hạn: cắt bớt phần chữ để còn chỗ cho 6 chữ số.</summary>
    public static string PatternOf(string sample, int? length)
    {
        var m = CodeRegex().Match(sample);
        var head = m.Success ? m.Groups[1].Value : sample + "_";
        var tail = m.Success ? m.Groups[3].Value : string.Empty;
        if (length is { } max && head.Length + tail.Length + 6 > max)
        {
            head = head[..Math.Max(0, max - tail.Length - 6)];
        }
        return head + "{n}" + tail;
    }

    private static long CodeNumber(string code) =>
        long.TryParse(CodeRegex().Match(code).Groups[2].Value, out var n) ? n : 0;

    /// <summary>Mã kế tiếp, giữ tiền tố + số chữ số: ORD000123 + 1 → ORD000124.</summary>
    public static string Next(string code, long step)
    {
        var m = CodeRegex().Match(code);
        if (!m.Success)
        {
            throw new MockError($"\"{code}\" không có số để tăng dần.");
        }
        var digits = m.Groups[2].Value;
        var number = (long.Parse(digits) + step).ToString(CultureInfo.InvariantCulture);
        return m.Groups[1].Value + number.PadLeft(digits.Length, '0') + m.Groups[3].Value;
    }
}

/// <summary>
/// Sinh dữ liệu: mỗi dòng đi lần lượt từ dòng 0 với 1 Random theo seed → cùng seed thì 20 dòng xem trước = 20 dòng đầu của file.
/// Kiểm tra NOT NULL, độ dài varchar / char, số chữ số numeric(p,s), không trùng ở khoá chính / ràng buộc duy nhất.
/// </summary>
public static class MockGenerator
{
    public static List<string?[]> Generate(IReadOnlyList<MockColumnSpec> columns, int count, int seed,
        IReadOnlyList<IReadOnlyList<string>>? uniqueSets = null) => Stream(columns, count, seed, uniqueSets).ToList();

    /// <summary>Sinh từng dòng (không giữ cả khối trong bộ nhớ - ghi file 1 triệu dòng). Cùng seed → cùng dữ liệu, nên CSV và INSERT
    /// sinh 2 lượt vẫn khớp nhau. Lỗi (quá độ dài, trùng khoá...) ném <see cref="MockError"/> ngay ở dòng gặp lỗi.</summary>
    public static IEnumerable<string?[]> Stream(IReadOnlyList<MockColumnSpec> columns, int count, int seed,
        IReadOnlyList<IReadOnlyList<string>>? uniqueSets = null)
    {
        var cols = columns.Where(c => c.Rule != MockRule.Skip).ToList();
        if (cols.Count == 0)
        {
            throw new MockError("Mọi cột đều đang Bỏ qua - chọn cách sinh cho ít nhất 1 cột.");
        }
        var makers = cols.Select(Prepare).ToList();
        var checks = (uniqueSets ?? [])
            .Select(set => (Set: set, Index: set.Select(n => cols.FindIndex(c => c.Name == n)).ToList(), Seen: new HashSet<string>(StringComparer.Ordinal)))
            .Where(u => u.Index.All(i => i >= 0)) // có cột do DB tự sinh (Bỏ qua) - DB lo không trùng
            .ToList();
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var row = new string?[cols.Count];
            for (var c = 0; c < cols.Count; c++)
            {
                var value = makers[c](i, random);
                Check(cols[c], value);
                row[c] = value;
            }
            foreach (var (set, index, seen) in checks)
            {
                if (!seen.Add(string.Join('\u0001', index.Select(x => row[x] ?? "\u0000"))))
                {
                    throw new MockError($"{string.Join(", ", set)} phải không trùng (khoá chính / duy nhất) nhưng giá trị sinh ra bị trùng "
                        + $"({string.Join(", ", index.Select(x => row[x] ?? "NULL"))}) ở dòng {i + 1} - dùng Tăng dần hoặc mẫu chuỗi có {{n}}.");
                }
            }
            yield return row;
        }
    }

    private static Func<int, Random, string?> Prepare(MockColumnSpec col)
    {
        var p = col.Param.Trim();
        string Fail(string why) => throw new MockError($"Cột {col.Name}: {why}");
        switch (col.Rule)
        {
            case MockRule.Sequence:
                if (p.Length == 0)
                {
                    Fail("Tăng dần cần giá trị bắt đầu (vd 1001 hoặc ORD000124).");
                }
                if (col.Type.Kind is MockKind.Integer or MockKind.Decimal)
                {
                    if (!long.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start))
                    {
                        Fail($"giá trị bắt đầu \"{p}\" không phải số nguyên.");
                    }
                    return (i, _) => (start + i).ToString(CultureInfo.InvariantCulture);
                }
                MockSuggest.Next(p, 0); // báo lỗi sớm nếu không có số
                return (i, _) => MockSuggest.Next(p, i);

            case MockRule.Number:
            {
                if (MockValues.Range(p) is not { } r || !MockValues.TryNumber(r.Min, out var lo) || !MockValues.TryNumber(r.Max, out var hi) || lo > hi)
                {
                    Fail($"khoảng số \"{p}\" phải dạng min..max (vd 100..5000).");
                    return null!;
                }
                if (col.Type.Kind == MockKind.Integer)
                {
                    var (a, b) = ((long)Math.Ceiling(lo), (long)Math.Floor(hi));
                    return (_, rnd) => rnd.NextInt64(a, b + 1).ToString(CultureInfo.InvariantCulture);
                }
                var scale = col.Type.Scale ?? Math.Max(Decimals(r.Min), Decimals(r.Max));
                return (_, rnd) => MockValues.Number(Math.Round(lo + (hi - lo) * (decimal)rnd.NextDouble(), scale, MidpointRounding.AwayFromZero));
            }

            case MockRule.DateRange:
            {
                if (MockValues.Range(p) is not { } r || !MockValues.TryDate(r.Min, out var from) || !MockValues.TryDate(r.Max, out var to) || from > to)
                {
                    Fail($"khoảng ngày \"{p}\" phải dạng yyyy-MM-dd..yyyy-MM-dd.");
                    return null!;
                }
                if (col.Type.Kind == MockKind.Timestamp)
                {
                    var seconds = (long)(to.AddDays(1) - from).TotalSeconds;
                    return (_, rnd) => from.AddSeconds(rnd.NextInt64(0, seconds)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }
                var days = (int)(to.Date - from.Date).TotalDays;
                return (_, rnd) => from.Date.AddDays(rnd.Next(0, days + 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            case MockRule.Pick:
            {
                var values = p.Split('|');
                if (p.Length == 0)
                {
                    Fail("Chọn trong danh sách cần ít nhất 1 giá trị, cách nhau bằng | (vd 0|1).");
                }
                return (_, rnd) => values[rnd.Next(values.Length)];
            }

            case MockRule.ForeignKey:
            {
                var values = col.Values ?? [];
                if (values.Count == 0)
                {
                    Fail("bảng được tham chiếu (khoá ngoại) chưa có dòng nào - sinh dữ liệu cho bảng đó trước, hoặc đổi cách sinh.");
                }
                return (_, rnd) => values[rnd.Next(values.Count)];
            }

            case MockRule.Pattern:
                if (!p.Contains("{n}", StringComparison.Ordinal))
                {
                    Fail("mẫu chuỗi phải có {n} (số thứ tự dòng), vd \"khách {n}\".");
                }
                return (i, _) => p.Replace("{n}", (i + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

            case MockRule.Constant:
                return (_, _) => col.Param; // giữ nguyên cả khoảng trắng
            case MockRule.Null:
                if (col.NotNull)
                {
                    Fail("cột NOT NULL không để NULL được.");
                }
                return (_, _) => null;
            case MockRule.Boolean:
                return (_, rnd) => rnd.Next(2) == 0 ? "false" : "true";
            case MockRule.Uuid:
                return (_, rnd) =>
                {
                    var bytes = new byte[16];
                    rnd.NextBytes(bytes);
                    bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40); // phiên bản 4
                    bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
                    return new Guid(bytes).ToString();
                };
            default:
                return (_, _) => null;
        }
    }

    private static int Decimals(string number) => number.Contains('.') ? number.Length - number.IndexOf('.') - 1 : 0;

    private static void Check(MockColumnSpec col, string? value)
    {
        if (value is null)
        {
            return;
        }
        if (col.Type.Kind == MockKind.Text && col.Type.Length is { } max && value.Length > max)
        {
            throw new MockError($"Cột {col.Name}: \"{value}\" dài {value.Length} ký tự, quá {col.Type.Display} - rút ngắn mẫu / giá trị.");
        }
        if (col.Type.Kind == MockKind.Decimal && col.Type.Precision is { } p && col.Type.Scale is { } s && MockValues.TryNumber(value, out var d)
            && Math.Truncate(Math.Abs(d)).ToString(CultureInfo.InvariantCulture).TrimStart('0').Length > p - s)
        {
            throw new MockError($"Cột {col.Name}: {value} vượt quá {col.Type.Display} (tối đa {p - s} chữ số phần nguyên) - thu hẹp khoảng số.");
        }
    }

}

public enum CsvEncoding
{
    Utf8Bom,
    Utf8,
    ShiftJis,
}

public sealed record CsvOptions(CsvEncoding Encoding = CsvEncoding.Utf8Bom, bool Header = true, bool NullAsText = false);

/// <summary>Ghi CSV / INSERT, đọc file CSV mẫu.</summary>
public static class MockOutput
{
    public static Encoding EncodingOf(CsvEncoding e)
    {
        if (e == CsvEncoding.ShiftJis)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932);
        }
        return new UTF8Encoding(e == CsvEncoding.Utf8Bom);
    }

    public static string CsvField(string? value, bool nullAsText)
    {
        if (value is null)
        {
            return nullAsText ? "NULL" : string.Empty;
        }
        return value.Length == 0 || value.IndexOfAny([',', '"', '\r', '\n']) >= 0 || value != value.Trim()
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    public static void WriteCsv(TextWriter w, IReadOnlyList<string> header, IEnumerable<string?[]> rows, CsvOptions o)
    {
        if (o.Header)
        {
            w.Write(string.Join(',', header.Select(h => CsvField(h, false))));
            w.Write("\r\n");
        }
        foreach (var row in rows)
        {
            w.Write(string.Join(',', row.Select(v => CsvField(v, o.NullAsText))));
            w.Write("\r\n");
        }
    }

    public static string SqlLiteral(string? value, MockType type) => value is null
        ? "NULL"
        : type.Quoted ? PgSql.Literal(value) : value;

    /// <summary>INSERT nhiều dòng / câu (mỗi câu <paramref name="batch"/> dòng). Không BEGIN / COMMIT: chạy được bằng trang Chạy thư mục
    /// SQL (tự bọc 1 transaction) lẫn psql.</summary>
    public static void WriteInsert(TextWriter w, string table, IReadOnlyList<MockColumnSpec> cols, IEnumerable<string?[]> rows, string title, int batch = 500)
    {
        var used = cols.Where(c => c.Rule != MockRule.Skip).ToList();
        var head = $"INSERT INTO {table} ({string.Join(", ", used.Select(c => PgSql.QuoteIdentIfNeeded(c.Name)))}) VALUES";
        w.Write($"-- {title}\n-- Không có BEGIN / COMMIT: chạy bằng DbTools (Chạy thư mục SQL, 1 transaction) hoặc psql --single-transaction -f\n\n");
        var n = 0;
        foreach (var row in rows)
        {
            w.Write(n % batch == 0 ? (n == 0 ? head : ";\n\n" + head) + "\n    (" : ",\n    (");
            w.Write(string.Join(", ", row.Select((v, i) => SqlLiteral(v, used[i].Type))));
            w.Write(')');
            n++;
        }
        if (n > 0)
        {
            w.Write(";\n");
        }
    }

    /// <summary>Đọc file CSV mẫu (UTF-8 / Shift-JIS, dấu phẩy hoặc tab): dòng đầu là tên cột, ô trống = NULL.</summary>
    public static (List<string> Header, List<string?[]> Rows) ReadCsv(byte[] bytes)
    {
        var text = SqlScriptFile.Decode(bytes).Text;
        var firstLine = text.Split('\n')[0];
        var sep = firstLine.Count(c => c == '\t') > firstLine.Count(c => c == ',') ? '\t' : ',';
        var records = new List<List<string>>();
        var field = new StringBuilder();
        var record = new List<string>();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == sep)
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                record.Add(field.ToString());
                field.Clear();
                if (record.Count > 1 || record[0].Length > 0)
                {
                    records.Add(record);
                }
                record = [];
            }
            else
            {
                field.Append(c);
            }
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }
        if (records.Count == 0)
        {
            throw new MockError("File CSV trống.");
        }
        var header = records[0].Select(h => h.Trim()).ToList();
        var rows = records.Skip(1)
            .Select(r => Enumerable.Range(0, header.Count).Select(i => i < r.Count && r[i].Length > 0 && r[i] != "NULL" ? r[i] : null).ToArray())
            .ToList();
        return (header, rows);
    }
}
