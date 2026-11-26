using System.Text;

namespace DbTools.Core;

/// <summary>1 bảng 1 câu ghi dữ liệu đụng tới: giờ (HH:mm:ss), lệnh (INSERT / UPDATE / DELETE / MERGE / TRUNCATE), tên bảng.</summary>
public sealed record LogTableUse(string Time, string Verb, string Table);

/// <summary>
/// Nhật ký server - Chép tên bảng: tên bảng của câu INSERT / UPDATE / DELETE / MERGE / TRUNCATE (cả WITH ... INSERT).
/// Làm trống chuỗi / chú thích / thân $$ trước, rồi lấy lệnh ở mức ngoặc ngoài cùng (WITH x AS (...) INSERT INTO t → t,
/// không lấy lệnh trong CTE / subquery). Tên giữ nguyên như câu (cả schema), bỏ dấu ".
/// </summary>
public static class LogTables
{
    private static readonly HashSet<string> Verbs = ["INSERT", "UPDATE", "DELETE", "MERGE", "TRUNCATE"];

    /// <summary>Lệnh + các bảng (TRUNCATE a, b có nhiều bảng); không phải câu ghi dữ liệu thì rỗng.</summary>
    public static (string Verb, IReadOnlyList<string> Tables) Of(string sql)
    {
        var plain = Blank(sql);
        var depth = 0;
        for (var i = 0; i < plain.Length; i++)
        {
            var c = plain[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && IsWordStart(plain, i))
            {
                var end = WordEnd(plain, i);
                var word = plain[i..end].ToUpperInvariant();
                if (Verbs.Contains(word))
                {
                    var tables = TablesAfter(word, sql, plain, end);
                    return tables.Count > 0 ? (word, tables) : (string.Empty, []);
                }
                if (word is "SELECT" or "VALUES" or "TABLE" or "CREATE" or "ALTER" or "DROP" or "COPY" or "CALL" or "DO" or "SET"
                    or "BEGIN" or "COMMIT" or "ROLLBACK" or "SAVEPOINT" or "RELEASE" or "SHOW" or "EXPLAIN")
                {
                    return (string.Empty, []); // câu đọc / khác: không tìm lệnh ghi phía sau (vd SELECT ... FOR UPDATE)
                }
                i = end - 1;
            }
        }
        return (string.Empty, []);
    }

    /// <summary>Theo thứ tự chạy: mỗi bảng của mỗi câu 1 dòng.</summary>
    public static List<LogTableUse> InOrder(IEnumerable<LogEntry> entries)
    {
        var list = new List<LogTableUse>();
        foreach (var e in entries)
        {
            if (e.Sql.Length == 0 || e.Kind is not (LogKind.Insert or LogKind.Update or LogKind.Delete))
            {
                continue;
            }
            var (verb, tables) = Of(e.Sql);
            var time = e.Time.Length >= 19 ? e.Time[11..19] : e.Time;
            list.AddRange(tables.Select(t => new LogTableUse(time, verb, t)));
        }
        return list;
    }

    /// <summary>"giờ ⇥ lệnh ⇥ bảng" mỗi dòng (dán Excel thành 3 cột).</summary>
    public static string InOrderText(IEnumerable<LogTableUse> uses) =>
        string.Concat(uses.Select(u => $"{u.Time}\t{u.Verb}\t{u.Table}\r\n"));

    /// <summary>Không trùng: mỗi bảng 1 dòng theo lần đầu xuất hiện (không phân biệt hoa thường, giữ cách viết đầu tiên)
    /// "bảng ⇥ các lệnh ⇥ số câu".</summary>
    public static string DistinctText(IEnumerable<LogTableUse> uses)
    {
        var order = new List<string>();
        var byName = new Dictionary<string, (string Name, List<string> Verbs, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in uses)
        {
            if (!byName.TryGetValue(u.Table, out var item))
            {
                order.Add(u.Table);
                item = (u.Table, [], 0);
            }
            if (!item.Verbs.Contains(u.Verb))
            {
                item.Verbs.Add(u.Verb);
            }
            byName[u.Table] = (item.Name, item.Verbs, item.Count + 1);
        }
        return string.Concat(order.Select(n => byName[n]).Select(x => $"{x.Name}\t{string.Join(", ", x.Verbs)}\t{x.Count}\r\n"));
    }

    private static List<string> TablesAfter(string verb, string sql, string plain, int pos)
    {
        var tables = new List<string>();
        pos = Skip(plain, pos, verb switch
        {
            "INSERT" or "MERGE" => ["INTO"],
            "DELETE" => ["FROM"],
            "TRUNCATE" => ["TABLE"],
            _ => [],
        });
        while (true)
        {
            pos = Skip(plain, pos, ["ONLY"]);
            var (name, end) = Name(sql, plain, pos);
            if (name.Length == 0)
            {
                break;
            }
            tables.Add(name);
            pos = SkipSpaces(plain, end);
            if (verb != "TRUNCATE" || pos >= plain.Length || plain[pos] != ',')
            {
                break;
            }
            pos++;
        }
        return tables;
    }

    /// <summary>Bỏ các từ khoá tuỳ chọn (INTO / FROM / TABLE / ONLY) nếu có.</summary>
    private static int Skip(string plain, int pos, string[] words)
    {
        pos = SkipSpaces(plain, pos);
        foreach (var w in words)
        {
            if (pos + w.Length <= plain.Length && string.Compare(plain, pos, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) == 0
                && WordEnd(plain, pos) == pos + w.Length)
            {
                pos = SkipSpaces(plain, pos + w.Length);
            }
        }
        return pos;
    }

    /// <summary>Tên a / "A b" / s.t / s."T" tại <paramref name="pos"/>: đọc từ câu gốc (ruột "..." ở bản plain đã thành _).</summary>
    private static (string Name, int End) Name(string sql, string plain, int pos)
    {
        var sb = new StringBuilder();
        while (pos < sql.Length)
        {
            if (sql[pos] == '"')
            {
                var close = pos + 1;
                while (close < sql.Length && !(sql[close] == '"' && (close + 1 >= sql.Length || sql[close + 1] != '"')))
                {
                    close += sql[close] == '"' ? 2 : 1;
                }
                sb.Append(sql[(pos + 1)..Math.Min(close, sql.Length)].Replace("\"\"", "\""));
                pos = close + 1;
            }
            else if (IsWordStart(plain, pos))
            {
                var end = WordEnd(plain, pos);
                sb.Append(sql[pos..end]);
                pos = end;
            }
            else
            {
                break;
            }
            if (pos < sql.Length && sql[pos] == '.')
            {
                sb.Append('.');
                pos++;
                continue;
            }
            break;
        }
        return (sb.ToString(), pos);
    }

    private static int SkipSpaces(string s, int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
        {
            pos++;
        }
        return pos;
    }

    private static bool IsWordStart(string s, int i) =>
        (char.IsLetter(s[i]) || s[i] == '_') && (i == 0 || !(char.IsLetterOrDigit(s[i - 1]) || s[i - 1] is '_' or '$'));

    private static int WordEnd(string s, int i)
    {
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '$'))
        {
            i++;
        }
        return i;
    }

    /// <summary>Cùng độ dài với câu gốc; chuỗi '...', chú thích, thân $$...$$ thành dấu cách, ruột tên "..." thành _ (giữ vị trí).</summary>
    private static string Blank(string sql)
    {
        var chars = sql.ToCharArray();
        var i = 0;
        void Clear(int from, int to)
        {
            for (var k = from; k < Math.Min(to, chars.Length); k++)
            {
                if (chars[k] is not ('\n' or '\r'))
                {
                    chars[k] = ' ';
                }
            }
        }
        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (c == '-' && next == '-')
            {
                var end = sql.IndexOf('\n', i);
                end = end < 0 ? sql.Length : end;
                Clear(i, end);
                i = end;
            }
            else if (c == '/' && next == '*')
            {
                var depth = 1;
                var k = i + 2;
                while (k < sql.Length && depth > 0)
                {
                    if (sql[k] == '/' && k + 1 < sql.Length && sql[k + 1] == '*')
                    {
                        depth++;
                        k += 2;
                    }
                    else if (sql[k] == '*' && k + 1 < sql.Length && sql[k + 1] == '/')
                    {
                        depth--;
                        k += 2;
                    }
                    else
                    {
                        k++;
                    }
                }
                Clear(i, k);
                i = k;
            }
            else if (c is '\'' or '"')
            {
                var escapes = c == '\'' && i > 0 && sql[i - 1] is 'E' or 'e';
                var k = i + 1;
                while (k < sql.Length)
                {
                    if (escapes && sql[k] == '\\')
                    {
                        k += 2;
                        continue;
                    }
                    if (sql[k] == c)
                    {
                        if (k + 1 < sql.Length && sql[k + 1] == c)
                        {
                            k += 2;
                            continue;
                        }
                        break;
                    }
                    k++;
                }
                if (c == '"')
                {
                    // tên "...": giữ dấu " (đọc tên từ câu gốc), ruột thành _ để không bị coi là từ khoá / dấu cách
                    for (var m = i + 1; m < Math.Min(k, chars.Length); m++)
                    {
                        chars[m] = '_';
                    }
                }
                else
                {
                    Clear(i, k + 1);
                }
                i = k + 1;
            }
            else if (c == '$' && (i == 0 || !(char.IsLetterOrDigit(sql[i - 1]) || sql[i - 1] == '_')) && !char.IsAsciiDigit(next)
                     && sql.IndexOf('$', i + 1) is var close && close > i
                     && sql.AsSpan(i + 1, close - i - 1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_") < 0)
            {
                var tag = sql[i..(close + 1)];
                var end = sql.IndexOf(tag, close + 1, StringComparison.Ordinal);
                end = end < 0 ? sql.Length : end + tag.Length;
                Clear(i, end);
                i = end;
            }
            else
            {
                i++;
            }
        }
        return new string(chars);
    }
}
