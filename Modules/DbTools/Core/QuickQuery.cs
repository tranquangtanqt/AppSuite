using System.Collections;
using System.Globalization;
using System.Text;

namespace DbTools.Core;

/// <summary>1 câu sẽ chạy ở trang Chạy SQL: nội dung, dòng bắt đầu trong ô soạn (để báo lỗi đúng dòng), câu trả về dòng
/// có đọc bằng cursor được không (<see cref="QuickQuery.UsesCursor"/>).</summary>
public sealed record QueryStatement(string Text, int Line, bool Cursor);

/// <summary>Kết quả tách câu: các câu sẽ chạy + lệnh bị từ chối / bỏ qua (kèm lý do).</summary>
public sealed record QueryPlan(IReadOnlyList<QueryStatement> Statements, IReadOnlyList<string> Rejected, IReadOnlyList<string> Skipped);

/// <summary>
/// Phần không phụ thuộc DB / giao diện của trang "Chạy SQL" (unit test): chọn đoạn sẽ chạy, tách câu, chặn lệnh tự mở / đóng
/// giao dịch (DbTools luôn tự BEGIN READ ONLY / BEGIN ... COMMIT - người dùng gõ COMMIT giữa chừng là phá chế độ chỉ đọc),
/// đổi giá trị ô thành chữ, chép TSV / CSV, ước độ rộng cột.
/// </summary>
public static class QuickQuery
{
    /// <summary>Có chọn đoạn (không chỉ khoảng trắng) thì chạy đoạn đó, không thì cả ô.</summary>
    public static (string Text, int LineOffset) Pick(string all, string? selected, int selectionStart)
    {
        if (!string.IsNullOrWhiteSpace(selected))
        {
            var before = all[..Math.Clamp(selectionStart, 0, all.Length)];
            return (selected, CountLines(before));
        }
        return (all, 0);
    }

    private static int CountLines(string s)
    {
        // TextBox của WinUI giữ xuống dòng bằng \r: đếm cả \r\n, \r và \n.
        var n = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\n' || (s[i] == '\r' && (i + 1 >= s.Length || s[i + 1] != '\n')))
            {
                n++;
            }
        }
        return n;
    }

    public static QueryPlan Plan(string text, int lineOffset = 0)
    {
        // TextBox của WinUI xuống dòng bằng \r: đổi hết về \n, không thì SqlSplitter đếm sai dòng và chú thích -- nuốt cả phần sau.
        var split = SqlSplitter.Split(text.ReplaceLineEndings("\n"));
        var statements = new List<QueryStatement>();
        var rejected = new List<string>();
        foreach (var s in split.Statements)
        {
            var line = s.StartLine + lineOffset;
            if (TransactionControl(s.Text) is { } what)
            {
                rejected.Add($"Dòng {line}: {what} - DbTools tự mở / đóng giao dịch (chỉ đọc: luôn ROLLBACK; cho phép ghi: COMMIT khi mọi câu chạy được). Bỏ câu này rồi chạy lại.");
                continue;
            }
            statements.Add(new QueryStatement(s.Text, line, UsesCursor(s.Text)));
        }
        var skipped = split.MetaCommands.Select(m => $"Dòng {m.Line + lineOffset}: bỏ qua lệnh psql {m.Text.Trim()}").ToList();
        return new QueryPlan(statements, rejected, skipped);
    }

    /// <summary>Lệnh tự mở / kết thúc / đổi chế độ giao dịch → mô tả (null = câu thường). SAVEPOINT / ROLLBACK TO vẫn cho.</summary>
    public static string? TransactionControl(string sql)
    {
        var words = SqlLexer.Tokenize(sql).Take(3).Select(t => t.Kind == SqlTokenKind.Word ? t.Value : string.Empty).ToArray();
        var first = words.ElementAtOrDefault(0) ?? string.Empty;
        var second = words.ElementAtOrDefault(1) ?? string.Empty;
        return first switch
        {
            "begin" or "start" when first == "begin" || second == "transaction" => "BEGIN / START TRANSACTION",
            "commit" or "end" => "COMMIT",
            "abort" => "ROLLBACK",
            "rollback" when second is not ("to") => "ROLLBACK",
            "prepare" when second == "transaction" => "PREPARE TRANSACTION",
            "set" when second == "transaction" || (second == "session" && words.ElementAtOrDefault(2) == "characteristics") => "SET TRANSACTION",
            _ => null,
        };
    }

    /// <summary>Câu đọc qua cursor được (SELECT / VALUES / TABLE, không có SELECT INTO): chỉ lấy đủ số dòng giới hạn,
    /// không kéo cả bảng về máy. Câu khác (WITH có thể chứa ghi, INSERT ... RETURNING, SHOW, EXPLAIN) chạy thẳng.</summary>
    public static bool UsesCursor(string sql)
    {
        var tokens = SqlLexer.Tokenize(sql);
        if (tokens.Count == 0 || !(tokens[0].IsWord("select") || tokens[0].IsWord("values") || tokens[0].IsWord("table")))
        {
            return false;
        }
        // SELECT ... INTO bảng_mới (tạo bảng) và SELECT ... FOR UPDATE không đặt trong cursor chỉ đọc được.
        return !tokens.Any(t => t.IsWord("into"));
    }

    /// <summary>Câu dùng khi đọc qua cursor.</summary>
    public static string DeclareCursor(string sql, string cursor) => $"DECLARE {cursor} NO SCROLL CURSOR FOR {TrimEnd(sql)}";

    private static string TrimEnd(string sql) => sql.TrimEnd().TrimEnd(';').TrimEnd();

    // ----- Giá trị ô -----

    /// <summary>Giá trị ô thành chữ (null = NULL). Số / ngày theo dạng của PostgreSQL (không theo vùng của Windows).</summary>
    public static string? Format(object? value) => value switch
    {
        null or DBNull => null,
        string s => s,
        bool b => b ? "true" : "false",
        // timestamptz (Npgsql trả DateTime UTC) → giờ máy này + độ lệch, như psql; timestamp giữ nguyên.
        DateTime d when d.Kind == DateTimeKind.Utc => Stamp(d.ToLocalTime()) + Offset(TimeZoneInfo.Local.GetUtcOffset(d)),
        DateTime d => Stamp(d),
        DateTimeOffset o => Stamp(o.DateTime) + Offset(o.Offset),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
        TimeSpan t => t.ToString(t.Days != 0 ? @"d\.hh\:mm\:ss\.FFFFFF" : @"hh\:mm\:ss\.FFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
        byte[] bytes => "\\x" + Convert.ToHexString(bytes).ToLowerInvariant(),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable list => "{" + string.Join(",", list.Cast<object?>().Select(v => Format(v) is { } s ? QuoteArrayItem(s) : "NULL")) + "}",
        _ => value.ToString(),
    };

    private static string Stamp(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture).TrimEnd('.');

    private static string Offset(TimeSpan o) => (o < TimeSpan.Zero ? "-" : "+") + o.Duration().ToString(o.Minutes == 0 ? "hh" : @"hh\:mm", CultureInfo.InvariantCulture);

    private static string QuoteArrayItem(string s) =>
        s.Length > 0 && !s.Any(c => c is ',' or '"' or '{' or '}' or '\\' || char.IsWhiteSpace(c)) && !s.Equals("NULL", StringComparison.OrdinalIgnoreCase)
            ? s
            : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // ----- Chép / xuất -----

    /// <summary>Dạng dán thẳng vào Excel: cột cách nhau Tab, ô có Tab / xuống dòng / " thì bọc "..." (Excel hiểu); NULL = ô trống.</summary>
    public static string ToTsv(IReadOnlyList<string>? header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        if (header is not null)
        {
            sb.Append(string.Join('\t', header.Select(TsvField))).Append("\r\n");
        }
        foreach (var row in rows)
        {
            sb.Append(string.Join('\t', row.Select(TsvField))).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string TsvField(string? v) => v is null ? string.Empty
        : v.IndexOfAny(['\t', '\r', '\n', '"']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    /// <summary>Tên file xuất: &lt;hồ sơ&gt;_&lt;lúc&gt;.csv, bỏ ký tự Windows không cho.</summary>
    public static string ExportFileName(string profile, DateTime time)
    {
        var name = new string(profile.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c).ToArray()).Trim('_');
        return $"{(name.Length == 0 ? "ketqua" : name)}_{time:yyyyMMdd-HHmmss}.csv";
    }

    // ----- Độ rộng cột -----

    /// <summary>Độ rộng cột (DIP) theo chữ dài nhất trong tiêu đề + tối đa <paramref name="sample"/> dòng đầu: 60 - 420.</summary>
    public static List<double> ColumnWidths(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string?>> rows, int sample = 200)
    {
        var widths = new List<double>(header.Count);
        for (var c = 0; c < header.Count; c++)
        {
            var chars = header[c].Length + 2; // + chỗ cho chữ kiểu dữ liệu
            foreach (var row in rows.Take(sample))
            {
                var v = c < row.Count ? row[c] : null;
                chars = Math.Max(chars, v is null ? 4 : FirstLine(v).Length);
            }
            widths.Add(Math.Clamp(chars * 7.5 + 20, 60, 420));
        }
        return widths;
    }

    /// <summary>Chữ hiện trong ô (1 dòng): dòng đầu, có xuống dòng thì thêm " ⏎…".</summary>
    public static string CellText(string? v)
    {
        if (v is null)
        {
            return "NULL";
        }
        var first = FirstLine(v);
        return first.Length == v.Length ? v : first + " ⏎…";
    }

    private static string FirstLine(string v)
    {
        var i = v.IndexOfAny(['\r', '\n']);
        var line = i < 0 ? v : v[..i];
        return line.Length > 500 ? line[..500] : line;
    }
}

/// <summary>1 câu đã chạy trong lần Chạy SQL (thẻ Thông báo + lịch sử).</summary>
public sealed record QueryStatementResult(int Line, string Kind, long? Rows, long DurationMs, string? Error = null)
{
    /// <summary>Vd "Dòng 3 · SELECT · 25 dòng · 12 ms".</summary>
    public string Describe() => $"Dòng {Line} · {Kind}"
        + (Error is not null ? " · LỖI: " + Error
            : Rows is { } n ? $" · {n:N0} dòng" : string.Empty)
        + $" · {DurationMs:N0} ms";
}

/// <summary>Bảng kết quả (câu trả về dòng cuối cùng): tên cột, kiểu, các dòng đã đọc, còn dòng chưa đọc không.</summary>
public sealed record QueryTable(IReadOnlyList<string> Columns, IReadOnlyList<string> Types, IReadOnlyList<string?[]> Rows, bool Truncated,
    string Sql, bool Cursor);
