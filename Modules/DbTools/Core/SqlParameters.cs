using System.Text;

namespace DbTools.Core;

/// <summary>Kết quả thay tham số: câu mới, số chỗ đã thay, các $n có trong câu mà log không ghi giá trị (giữ nguyên).</summary>
public sealed record InlinedSql(string Sql, int Replaced, IReadOnlyList<int> Missing);

/// <summary>
/// Câu ứng dụng gửi kiểu có tham số ($1, $2...) được server ghi log kèm dòng "DETAIL:  parameters: $1 = '...', $2 = NULL"
/// (PostgreSQL 16+ viết hoa "Parameters:")
/// (lỗi thì "CONTEXT:  unnamed portal with parameters: ..."). Tách giá trị rồi thay vào câu để chép ra chạy thử được;
/// dòng có thay thì thêm chú thích cuối dòng "-- $1 = '...'".
/// </summary>
public static class SqlParameters
{
    private const string Marker = "parameters: ";

    /// <summary>Chú thích cuối dòng: giá trị dài hơn thì cắt bớt (câu đã thay vẫn đủ giá trị).</summary>
    public const int CommentValueMax = 80;

    /// <summary>$n → giá trị đúng như log ghi: chuỗi trong nháy đơn ('' bên trong giữ nguyên) hoặc NULL. Rỗng nếu không có.</summary>
    public static IReadOnlyDictionary<int, string> Parse(string detail)
    {
        var result = new Dictionary<int, string>();
        // PostgreSQL 16+ ghi "Parameters:", bản cũ (vd FEP 15) "parameters:"
        var start = detail.IndexOf(Marker + "$", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return result;
        }
        var i = start + Marker.Length;
        var len = detail.Length;
        while (i < len && detail[i] == '$')
        {
            var j = i + 1;
            while (j < len && char.IsAsciiDigit(detail[j]))
            {
                j++;
            }
            if (j == i + 1 || !int.TryParse(detail.AsSpan(i + 1, j - i - 1), out var n) || !detail.AsSpan(j).StartsWith(" = "))
            {
                break;
            }
            j += 3;
            string value;
            if (detail.AsSpan(j).StartsWith("NULL"))
            {
                value = "NULL";
                j += 4;
            }
            else if (j < len && detail[j] == '\'')
            {
                var k = j + 1;
                while (true)
                {
                    if (k >= len)
                    {
                        return result; // log bị cắt giữa giá trị
                    }
                    if (detail[k] == '\'')
                    {
                        if (k + 1 < len && detail[k + 1] == '\'')
                        {
                            k += 2;
                            continue;
                        }
                        break;
                    }
                    k++;
                }
                value = detail[j..(k + 1)];
                j = k + 1;
            }
            else
            {
                break;
            }
            result[n] = value;
            if (!detail.AsSpan(j).StartsWith(", $"))
            {
                break;
            }
            i = j + 2;
        }
        return result;
    }

    private enum State { Normal, Quote, EQuote, Ident, LineComment, BlockComment, Dollar }

    /// <summary>
    /// Thay $n bằng giá trị. Bỏ qua $n nằm trong chuỗi '...', E'...', tên "...", chú thích -- / /* */, thân $$...$$ / $tag$...$tag$.
    /// Dòng có thay (và cuối dòng không còn nằm trong chuỗi / chú thích nhiều dòng) thì thêm "  -- $1 = '...'".
    /// </summary>
    public static InlinedSql Inline(string sql, IReadOnlyDictionary<int, string> values)
    {
        sql = sql.ReplaceLineEndings("\n");
        var sb = new StringBuilder(sql.Length + 64);
        var line = new List<int>();
        var missing = new SortedSet<int>();
        var replaced = 0;
        var state = State.Normal;
        var depth = 0;
        var tag = string.Empty;

        void EndLine()
        {
            if (line.Count > 0 && state is State.Normal or State.LineComment)
            {
                sb.Append("  -- ").Append(string.Join(", ", line.Distinct().Select(n => $"${n} = {Short(values[n])}")));
            }
            line.Clear();
        }

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (c == '\n')
            {
                EndLine();
                if (state == State.LineComment)
                {
                    state = State.Normal;
                }
                sb.Append(c);
                continue;
            }
            switch (state)
            {
                case State.Normal:
                    if (c == '-' && next == '-')
                    {
                        state = State.LineComment;
                    }
                    else if (c == '/' && next == '*')
                    {
                        state = State.BlockComment;
                        depth = 1;
                        sb.Append("/*");
                        i++;
                        continue;
                    }
                    else if (c == '\'')
                    {
                        state = i > 0 && sql[i - 1] is 'E' or 'e' && (i < 2 || !IsIdent(sql[i - 2])) ? State.EQuote : State.Quote;
                    }
                    else if (c == '"')
                    {
                        state = State.Ident;
                    }
                    else if (c == '$' && (i == 0 || !IsIdent(sql[i - 1])))
                    {
                        if (char.IsAsciiDigit(next))
                        {
                            var j = i + 1;
                            while (j < sql.Length && char.IsAsciiDigit(sql[j]))
                            {
                                j++;
                            }
                            if (int.TryParse(sql.AsSpan(i + 1, j - i - 1), out var n) && values.TryGetValue(n, out var value))
                            {
                                sb.Append(value);
                                line.Add(n);
                                replaced++;
                            }
                            else
                            {
                                sb.Append(sql, i, j - i);
                                if (n > 0)
                                {
                                    missing.Add(n);
                                }
                            }
                            i = j - 1;
                            continue;
                        }
                        var end = sql.IndexOf('$', i + 1);
                        if (end > i && sql.AsSpan(i + 1, end - i - 1) is var t && (t.Length == 0 || IsTag(t)))
                        {
                            tag = sql[i..(end + 1)];
                            state = State.Dollar;
                            sb.Append(tag);
                            i = end;
                            continue;
                        }
                    }
                    break;
                case State.Quote:
                    if (c == '\'')
                    {
                        if (next == '\'')
                        {
                            sb.Append("''");
                            i++;
                            continue;
                        }
                        state = State.Normal;
                    }
                    break;
                case State.EQuote:
                    if (c == '\\' && next != '\0' && next != '\n')
                    {
                        sb.Append(c).Append(next);
                        i++;
                        continue;
                    }
                    goto case State.Quote;
                case State.Ident:
                    if (c == '"')
                    {
                        state = State.Normal;
                    }
                    break;
                case State.BlockComment:
                    if (c == '/' && next == '*')
                    {
                        depth++;
                        sb.Append("/*");
                        i++;
                        continue;
                    }
                    if (c == '*' && next == '/')
                    {
                        if (--depth == 0)
                        {
                            state = State.Normal;
                        }
                        sb.Append("*/");
                        i++;
                        continue;
                    }
                    break;
                case State.Dollar:
                    if (c == '$' && string.CompareOrdinal(sql, i, tag, 0, tag.Length) == 0)
                    {
                        sb.Append(tag);
                        i += tag.Length - 1;
                        state = State.Normal;
                        continue;
                    }
                    break;
            }
            sb.Append(c);
        }
        EndLine();
        return new InlinedSql(sb.ToString(), replaced, [.. missing]);
    }

    private static string Short(string value) =>
        value.Length <= CommentValueMax ? value.ReplaceLineEndings(" ") : value[..(CommentValueMax - 2)].ReplaceLineEndings(" ") + "…'";

    private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    private static bool IsTag(ReadOnlySpan<char> t)
    {
        if (char.IsAsciiDigit(t[0]))
        {
            return false;
        }
        foreach (var c in t)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }
        return true;
    }
}
