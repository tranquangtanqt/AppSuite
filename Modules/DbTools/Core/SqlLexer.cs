namespace DbTools.Core;

public enum SqlTokenKind
{
    /// <summary>Từ khoá hoặc tên chưa đặt trong "..." - Value viết thường (PostgreSQL tự hạ chữ thường).</summary>
    Word,
    /// <summary>Tên trong "..." - Value giữ nguyên hoa / thường.</summary>
    QuotedName,
    /// <summary>Chuỗi '...'.</summary>
    String,
    Number,
    /// <summary>1 ký tự dấu: ( ) , . ; = ...</summary>
    Symbol,
}

public readonly record struct SqlToken(SqlTokenKind Kind, string Value, int Offset)
{
    public bool IsWord(string word) => Kind == SqlTokenKind.Word && Value == word;

    public bool IsName => Kind is SqlTokenKind.Word or SqlTokenKind.QuotedName;

    public bool IsSymbol(char c) => Kind == SqlTokenKind.Symbol && Value.Length == 1 && Value[0] == c;
}

/// <summary>
/// Đọc 1 câu SQL thành token để nhận loại lệnh / tên đối tượng / các tên được nhắc tới. Bỏ chú thích. Dấu mở / đóng thân
/// hàm $tag$ được bỏ qua nhưng phần thân vẫn đọc như SQL - để biết hàm / trigger dùng tới bảng nào (sắp thứ tự chạy).
/// </summary>
public static class SqlLexer
{
    public static List<SqlToken> Tokenize(string s)
    {
        var tokens = new List<SqlToken>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (SqlSplitter.IsLineComment(s, i))
            {
                i = SqlSplitter.SkipLineComment(s, i);
            }
            else if (SqlSplitter.IsBlockComment(s, i))
            {
                i = SqlSplitter.SkipBlockComment(s, i);
            }
            else if (c == '\'')
            {
                var escape = SqlSplitter.IsEscapeString(s, i);
                if (escape && tokens.Count > 0 && tokens[^1].Offset == i - 1)
                {
                    tokens.RemoveAt(tokens.Count - 1); // chữ E của E'...' đã bị đọc thành 1 từ
                }
                var end = SqlSplitter.SkipQuoted(s, i, escape);
                var inner = s[(i + 1)..Math.Max(i + 1, end - 1)].Replace("''", "'");
                tokens.Add(new SqlToken(SqlTokenKind.String, inner, i));
                i = end;
            }
            else if (c == '"')
            {
                var end = SqlSplitter.SkipDoubleQuoted(s, i);
                var inner = s[(i + 1)..Math.Max(i + 1, end - 1)].Replace("\"\"", "\"");
                tokens.Add(new SqlToken(SqlTokenKind.QuotedName, inner, i));
                i = end;
            }
            else if (c == '$' && SqlSplitter.TryReadDollarTag(s, i, out var tag))
            {
                i += tag.Length;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < s.Length && SqlSplitter.IsIdentChar(s[i]))
                {
                    i++;
                }
                tokens.Add(new SqlToken(SqlTokenKind.Word, s[start..i].ToLowerInvariant(), start));
            }
            else if (char.IsDigit(c))
            {
                var start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.'))
                {
                    i++;
                }
                tokens.Add(new SqlToken(SqlTokenKind.Number, s[start..i], start));
            }
            else
            {
                tokens.Add(new SqlToken(SqlTokenKind.Symbol, c.ToString(), i));
                i++;
            }
        }
        return tokens;
    }
}
