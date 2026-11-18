namespace DbTools.Core;

/// <summary>1 câu SQL trong file. <paramref name="Text"/> không gồm dấu ; cuối; <paramref name="StartLine"/> đếm từ 1.</summary>
public sealed record SqlStatement(string Text, int StartLine, int Offset);

/// <summary>1 lệnh riêng của psql (dòng bắt đầu bằng \, vd <c>\encoding UTF-8</c>) - không phải SQL, không gửi lên server.</summary>
public sealed record PsqlMetaCommand(string Text, int Line)
{
    /// <summary>Tên lệnh viết thường, không có \ (vd "encoding").</summary>
    public string Name => Text.TrimStart('\\').Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;

    /// <summary>Phần sau tên lệnh (vd "UTF-8").</summary>
    public string Argument => Text.TrimStart('\\').Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1)?.Trim() ?? string.Empty;
}

public sealed record SplitResult(IReadOnlyList<SqlStatement> Statements, IReadOnlyList<PsqlMetaCommand> MetaCommands);

/// <summary>
/// Tách 1 file SQL thành từng câu theo dấu ; như psql: bỏ qua ; nằm trong chuỗi '...', E'...' (có \'), tên "...",
/// thân hàm $tag$...$tag$, chú thích -- và /* */ (lồng nhau được). Dòng bắt đầu bằng \ ở chỗ bắt đầu câu = lệnh psql.
/// <c>$</c> dính sau chữ (vd <c>msta_arap_data_$s</c>) là 1 phần của tên, không phải mở thân hàm.
/// </summary>
public static class SqlSplitter
{
    public static SplitResult Split(string text)
    {
        var statements = new List<SqlStatement>();
        var metas = new List<PsqlMetaCommand>();
        var lines = new LineIndex(text);
        var n = text.Length;
        var start = -1; // vị trí ký tự đầu của câu đang đọc; -1 = chưa vào câu nào
        var i = 0;

        void Emit(int end)
        {
            var body = text[start..end].TrimEnd();
            if (body.Length > 0)
            {
                statements.Add(new SqlStatement(body, lines.LineOf(start), start));
            }
            start = -1;
        }

        while (i < n)
        {
            var c = text[i];
            if (start < 0)
            {
                if (char.IsWhiteSpace(c) || c == ';')
                {
                    i++;
                    continue;
                }
                if (c == '\\')
                {
                    var eol = text.IndexOf('\n', i);
                    var end = eol < 0 ? n : eol;
                    metas.Add(new PsqlMetaCommand(text[i..end].TrimEnd(), lines.LineOf(i)));
                    i = end;
                    continue;
                }
                if (IsLineComment(text, i))
                {
                    i = SkipLineComment(text, i);
                    continue;
                }
                if (IsBlockComment(text, i))
                {
                    i = SkipBlockComment(text, i);
                    continue;
                }
                start = i;
            }

            if (IsLineComment(text, i))
            {
                i = SkipLineComment(text, i);
            }
            else if (IsBlockComment(text, i))
            {
                i = SkipBlockComment(text, i);
            }
            else if (c == '\'')
            {
                i = SkipQuoted(text, i, IsEscapeString(text, i));
            }
            else if (c == '"')
            {
                i = SkipDoubleQuoted(text, i);
            }
            else if (c == '$' && TryReadDollarTag(text, i, out var tag))
            {
                var close = text.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                i = close < 0 ? n : close + tag.Length;
            }
            else if (c == ';')
            {
                Emit(i);
                i++;
            }
            else
            {
                i++;
            }
        }
        if (start >= 0)
        {
            Emit(n);
        }
        return new SplitResult(statements, metas);
    }

    internal static bool IsLineComment(string s, int i) => s[i] == '-' && i + 1 < s.Length && s[i + 1] == '-';

    internal static bool IsBlockComment(string s, int i) => s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*';

    internal static int SkipLineComment(string s, int i)
    {
        var eol = s.IndexOf('\n', i);
        return eol < 0 ? s.Length : eol;
    }

    /// <summary>Chú thích /* */ của PostgreSQL lồng nhau được.</summary>
    internal static int SkipBlockComment(string s, int i)
    {
        var depth = 0;
        while (i < s.Length)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                depth++;
                i += 2;
            }
            else if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                {
                    return i;
                }
            }
            else
            {
                i++;
            }
        }
        return s.Length;
    }

    /// <summary>E'...' (hoặc e'...'): \ thoát ký tự kế tiếp. Chữ E phải đứng riêng (không dính sau 1 tên).</summary>
    internal static bool IsEscapeString(string s, int quote) =>
        quote > 0 && (s[quote - 1] is 'E' or 'e') && (quote < 2 || !IsIdentChar(s[quote - 2]));

    /// <summary>Bỏ qua chuỗi '...' ('' = 1 dấu nháy). Trả vị trí ngay sau dấu đóng.</summary>
    internal static int SkipQuoted(string s, int i, bool backslashEscapes)
    {
        i++;
        while (i < s.Length)
        {
            if (backslashEscapes && s[i] == '\\')
            {
                i += 2;
                continue;
            }
            if (s[i] == '\'')
            {
                if (i + 1 < s.Length && s[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            i++;
        }
        return s.Length;
    }

    internal static int SkipDoubleQuoted(string s, int i)
    {
        i++;
        while (i < s.Length)
        {
            if (s[i] == '"')
            {
                if (i + 1 < s.Length && s[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            i++;
        }
        return s.Length;
    }

    /// <summary>$$ hoặc $tên$ mở thân hàm. Không tính khi $ dính sau chữ / số (1 phần của tên) hoặc là $1 (tham số).</summary>
    internal static bool TryReadDollarTag(string s, int i, out string tag)
    {
        tag = string.Empty;
        if (i > 0 && IsIdentChar(s[i - 1]))
        {
            return false;
        }
        var j = i + 1;
        if (j < s.Length && (char.IsLetter(s[j]) || s[j] == '_'))
        {
            j++;
            while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_'))
            {
                j++;
            }
        }
        if (j < s.Length && s[j] == '$')
        {
            tag = s[i..(j + 1)];
            return true;
        }
        return false;
    }

    internal static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    /// <summary>Đổi vị trí ký tự → số dòng (từ 1).</summary>
    private sealed class LineIndex
    {
        private readonly List<int> _starts = [0];

        public LineIndex(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    _starts.Add(i + 1);
                }
            }
        }

        public int LineOf(int offset)
        {
            var index = _starts.BinarySearch(offset);
            return (index >= 0 ? index : ~index - 1) + 1;
        }
    }
}
