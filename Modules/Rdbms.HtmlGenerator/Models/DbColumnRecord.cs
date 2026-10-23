namespace Rdbms.HtmlGenerator.Models;

/// <summary>One column belonging to a <see cref="DbTableRecord"/>, read from PostgreSQL.
/// <see cref="Level"/> reuses Mcf.DbDef.HtmlGenerator's convention (0 = primary-key column, 1 = everything else) so
/// the shared HTML report's "khoa chinh" styling works unchanged. <see cref="IsCommon"/> is always
/// false - Postgres has no equivalent to the Excel workbooks' shared "$...$" column groups.</summary>
public sealed record DbColumnRecord
{
    public required string TableName { get; init; }
    public int OrdinalPosition { get; init; }
    public int? Level { get; init; }
    public required string ColumnName { get; init; }
    public string Meta { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string Length { get; init; } = string.Empty;
    public string Nullable { get; init; } = string.Empty;
    public string DefaultValue { get; init; } = string.Empty;
    public string JapaneseName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string ValueRestriction { get; init; } = string.Empty;
    public bool IsCommon { get; init; }

    /// <summary><see cref="DefaultValue"/> để hiển thị (HTML, Excel): bỏ cặp ngoặc bao ngoài mà SQL Server tự thêm -
    /// "((0))" → "0", "(getdate())" → "getdate()", "(N'abc')" → "N'abc'". Giá trị lưu / so sánh vẫn là bản gốc.</summary>
    public string DisplayDefaultValue => StripOuterParentheses(DefaultValue);

    /// <summary>Bỏ lặp lại cặp ngoặc bao trọn cả chuỗi. "(1)+(2)" giữ nguyên (ngoặc đầu không đóng ở cuối);
    /// ngoặc trong chuỗi '...' không tính.</summary>
    public static string StripOuterParentheses(string value)
    {
        var text = value.Trim();
        while (text.Length >= 2 && text[0] == '(' && text[^1] == ')' && ClosingIndex(text) == text.Length - 1)
        {
            text = text[1..^1].Trim();
        }
        return text;

        // Vị trí ngoặc đóng khớp với ngoặc mở ở đầu chuỗi (-1 nếu không khớp).
        static int ClosingIndex(string s)
        {
            var depth = 0;
            var inString = false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\'')
                {
                    inString = !inString;
                }
                else if (!inString && c == '(')
                {
                    depth++;
                }
                else if (!inString && c == ')' && --depth == 0)
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>Schema of <see cref="TableName"/> as read from the source DB - only used by
    /// TableNameQualifier to tell same-named tables in different schemas apart; not stored in SQLite.</summary>
    public string Schema { get; init; } = string.Empty;
}
