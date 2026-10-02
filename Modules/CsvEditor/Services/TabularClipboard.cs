using System.Text;

namespace CsvEditor.Services;

/// <summary>Chuyển bảng ↔ text dạng Tab theo cách Excel dùng với clipboard: ô có tab / xuống dòng / dấu ngoặc kép thì
/// bọc trong "…" (ngoặc kép bên trong nhân đôi). Không bọc thì ô có xuống dòng bị Excel tách thành nhiều dòng khi dán.</summary>
public static class TabularClipboard
{
    public static string Format(IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            var first = true;
            foreach (var cell in row)
            {
                if (!first)
                {
                    sb.Append('\t');
                }
                first = false;
                sb.Append(cell.IndexOfAny(['\t', '\r', '\n', '"']) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell);
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Đọc text dạng Tab (từ Excel hoặc từ <see cref="Format"/>): ô bắt đầu bằng " là ô bọc ngoặc (được chứa
    /// tab / xuống dòng); xuống dòng cuối cùng không tạo thêm dòng rỗng.</summary>
    public static List<IReadOnlyList<string>> Parse(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (cell.Length == 0 && text[i] == '"' && TryReadQuoted(text, ref i, cell))
            {
                continue;
            }

            var c = text[i++];
            if (c == '\t')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i < text.Length && text[i] == '\n')
                {
                    i++;
                }
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Đọc 1 ô bọc ngoặc bắt đầu ở <paramref name="i"/>. Chỉ nhận khi ngoặc đóng nằm ngay trước tab / xuống
    /// dòng / hết text - không thì (vd <c>"abc"def</c>) trả false, đọc ô như text thường.</summary>
    private static bool TryReadQuoted(string text, ref int i, StringBuilder cell)
    {
        var value = new StringBuilder();
        var j = i + 1;
        while (j < text.Length)
        {
            if (text[j] != '"')
            {
                value.Append(text[j++]);
                continue;
            }
            if (j + 1 < text.Length && text[j + 1] == '"')
            {
                value.Append('"');
                j += 2;
                continue;
            }
            if (j + 1 == text.Length || text[j + 1] is '\t' or '\r' or '\n')
            {
                cell.Append(value);
                i = j + 1;
                return true;
            }
            return false;
        }
        return false;
    }
}
