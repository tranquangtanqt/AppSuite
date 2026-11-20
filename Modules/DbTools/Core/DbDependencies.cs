namespace DbTools.Core;

/// <summary>1 chỗ khớp trong mã nguồn: dòng (từ 1), vị trí trong cả đoạn, độ dài, chữ của dòng đó.</summary>
public sealed record SourceMatch(int Line, int Index, int Length, string LineText, bool Qualified = false);

/// <summary>Loại đối tượng phụ thuộc vào 1 bảng (tab Phụ thuộc trong chi tiết bảng).</summary>
public enum DependentKind
{
    /// <summary>View / materialized view dùng bảng (pg_depend - chắc chắn); Via = view trung gian nếu gián tiếp.</summary>
    View,
    /// <summary>Khoá ngoại ở bảng khác trỏ tới bảng này.</summary>
    ForeignKey,
    /// <summary>Trigger trên chính bảng này (xoá theo bảng).</summary>
    Trigger,
    /// <summary>Sequence thuộc 1 cột của bảng (OWNED BY / identity - xoá theo bảng).</summary>
    Sequence,
    /// <summary>Hàm có thân SQL chuẩn (BEGIN ATOMIC, PostgreSQL 14+) - PostgreSQL ghi nhận phụ thuộc.</summary>
    TrackedFunction,
    /// <summary>Hàm / procedure nhắc tên bảng trong mã nguồn (dò chữ - PostgreSQL không theo dõi).</summary>
    Function,
    /// <summary>Trigger trên bảng khác gọi 1 hàm ở <see cref="Function"/>.</summary>
    IndirectTrigger,
}

/// <param name="Name">Tên đầy đủ (schema.tên; trigger: "tên trên bảng").</param>
/// <param name="Relation">Mô tả liên quan thế nào, vd "dùng cột id, amount", "qua view a.v1", "3 chỗ, dòng 12".</param>
/// <param name="Columns">Cột của bảng bị dùng (view, khoá ngoại) - rỗng nếu không biết.</param>
/// <param name="Detail">Hiện khi chọn dòng: định nghĩa / các dòng khớp.</param>
public sealed record DependentItem(DependentKind Kind, string Name, string Relation, IReadOnlyList<string> Columns, string Detail, int Depth = 1);

/// <summary>Tìm chữ trong mã nguồn view / hàm (tab "Tìm trong mã nguồn") và dò tên bảng trong thân hàm (tab Phụ thuộc).</summary>
public static class SourceSearch
{
    /// <summary>Mọi chỗ có <paramref name="term"/> (tìm chữ thường, cả chú thích). <paramref name="wholeWord"/>: 2 bên không phải
    /// chữ / số / _ / $ (như tên trong SQL).</summary>
    public static List<SourceMatch> Find(string text, string term, bool wholeWord, bool caseSensitive)
    {
        var result = new List<SourceMatch>();
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(text))
        {
            return result;
        }
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var i = text.IndexOf(term, comparison); i >= 0; i = text.IndexOf(term, i + Math.Max(1, term.Length), comparison))
        {
            if (wholeWord && (IsNameChar(text, i - 1) || IsNameChar(text, i + term.Length)))
            {
                continue;
            }
            result.Add(At(text, i, term.Length));
        }
        return result;
    }

    /// <summary>
    /// Chỗ mã nguồn (thân hàm) nhắc tới bảng <paramref name="schema"/>.<paramref name="table"/>: đọc bằng <see cref="SqlLexer"/>
    /// nên bỏ qua chú thích; chuỗi '...' (SQL động trong EXECUTE) đọc thêm 1 lớp. Không tính: cột cùng tên (x.ten, schema khác),
    /// gọi hàm cùng tên (ten(...)), tên ngay sau FUNCTION / PROCEDURE. <see cref="SourceMatch.Qualified"/>: có ghi schema
    /// (chắc chắn đúng bảng); không ghi = theo tên (đúng nếu search_path tìm tới schema của bảng).
    /// </summary>
    public static List<SourceMatch> FindTable(string source, string schema, string table)
    {
        var result = new List<SourceMatch>();
        Scan(source, SqlLexer.Tokenize(source), 0, schema, table, result, nested: false);
        return result.DistinctBy(m => m.Index).OrderBy(m => m.Index).ToList();
    }

    private static void Scan(string source, List<SqlToken> tokens, int baseOffset, string schema, string table, List<SourceMatch> result, bool nested)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == SqlTokenKind.String && !nested && t.Value.Length > 0)
            {
                Scan(source, SqlLexer.Tokenize(t.Value), t.Offset + 1, schema, table, result, nested: true);
                continue;
            }
            if (!NameIs(t, table))
            {
                continue;
            }
            if (i + 1 < tokens.Count && tokens[i + 1].IsSymbol('('))
            {
                continue; // gọi hàm cùng tên
            }
            var qualified = false;
            if (i >= 1 && tokens[i - 1].IsSymbol('.'))
            {
                if (i >= 2 && NameIs(tokens[i - 2], schema))
                {
                    qualified = true;
                }
                else
                {
                    continue; // alias.cột hoặc bảng ở schema khác
                }
            }
            var before = qualified ? i - 3 : i - 1;
            if (before >= 0 && (tokens[before].IsWord("function") || tokens[before].IsWord("procedure")))
            {
                continue;
            }
            var start = qualified ? tokens[i - 2].Offset : t.Offset;
            var end = t.Offset + (t.Kind == SqlTokenKind.QuotedName ? t.Value.Length + 2 : t.Value.Length);
            var index = Math.Min(baseOffset + start, source.Length);
            result.Add(At(source, index, Math.Max(1, Math.Min(end - start, source.Length - index))) with { Qualified = qualified });
        }
    }

    /// <summary>Tên chưa đặt trong "..." PostgreSQL hạ chữ thường → so với tên bảng như lưu trong catalog.</summary>
    private static bool NameIs(SqlToken t, string name) =>
        (t.Kind == SqlTokenKind.Word && t.Value == name) || (t.Kind == SqlTokenKind.QuotedName && t.Value == name);

    private static bool IsNameChar(string s, int i) => i >= 0 && i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '$');

    private static SourceMatch At(string text, int index, int length)
    {
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }
        var lineEnd = text.IndexOf('\n', lineStart);
        var lineText = (lineEnd < 0 ? text[lineStart..] : text[lineStart..lineEnd]).TrimEnd('\r').Trim();
        return new SourceMatch(line, index, length, lineText);
    }

    /// <summary>"3 chỗ · dòng 12: SELECT ..." - mô tả ngắn cho 1 dòng kết quả.</summary>
    public static string Summary(IReadOnlyList<SourceMatch> matches)
    {
        if (matches.Count == 0)
        {
            return string.Empty;
        }
        var first = matches[0];
        var text = first.LineText.Length > 90 ? first.LineText[..90] + "…" : first.LineText;
        return $"{matches.Count} chỗ · dòng {first.Line}: {text}";
    }

    /// <summary>Các dòng khớp (mỗi dòng 1 lần) "dòng 12: ...", tối đa <paramref name="max"/> dòng.</summary>
    public static string Lines(IReadOnlyList<SourceMatch> matches, int max = 30)
    {
        var lines = matches.GroupBy(m => m.Line).Select(g => $"dòng {g.Key}: {g.First().LineText}").ToList();
        return string.Join("\n", lines.Take(max)) + (lines.Count > max ? $"\n... và {lines.Count - max} dòng khác" : string.Empty);
    }
}

/// <summary>Tóm tắt "Ảnh hưởng" ở đầu tab Phụ thuộc: xoá bảng / đổi cột thì sao.</summary>
public static class DependencyImpact
{
    public static List<string> Describe(string table, IReadOnlyList<DependentItem> items)
    {
        var lines = new List<string>();
        var views = items.Where(i => i.Kind == DependentKind.View).ToList();
        var fks = items.Where(i => i.Kind == DependentKind.ForeignKey).ToList();
        var tracked = items.Where(i => i.Kind == DependentKind.TrackedFunction).ToList();
        var triggers = items.Count(i => i.Kind == DependentKind.Trigger);
        var sequences = items.Count(i => i.Kind == DependentKind.Sequence);
        var functions = items.Where(i => i.Kind == DependentKind.Function).ToList();
        var indirect = items.Count(i => i.Kind == DependentKind.IndirectTrigger);

        if (items.Count == 0)
        {
            lines.Add($"Không thấy đối tượng nào dùng {table}: không view, khoá ngoại, trigger, sequence; không hàm nào nhắc tên bảng.");
            return lines;
        }
        if (views.Count + fks.Count + tracked.Count > 0)
        {
            var blockers = Join(Count(views.Count, "view"), Count(fks.Count, "khoá ngoại từ bảng khác"), Count(tracked.Count, "hàm SQL chuẩn"));
            var cascade = Join(
                views.Count > 0 ? $"xoá {Count(views.Count, "view")}{(views.Any(v => v.Depth > 1) ? " (kể cả view dựng trên view đó)" : string.Empty)}" : string.Empty,
                fks.Count > 0 ? $"bỏ {Count(fks.Count, "khoá ngoại")} (dữ liệu bảng kia giữ nguyên)" : string.Empty,
                tracked.Count > 0 ? $"xoá {Count(tracked.Count, "hàm")}" : string.Empty);
            lines.Add($"⚠ DROP TABLE {table}: báo lỗi vì có {blockers} phụ thuộc. DROP ... CASCADE sẽ {cascade}.");
        }
        else
        {
            lines.Add($"DROP TABLE {table}: không bị chặn (không view / khoá ngoại nào phụ thuộc).");
        }
        if (triggers + sequences > 0)
        {
            lines.Add($"Xoá theo bảng: {Join(Count(triggers, "trigger"), Count(sequences, "sequence"))}.");
        }
        if (functions.Count > 0)
        {
            lines.Add($"⚠ {Count(functions.Count, "hàm / procedure")} nhắc tên bảng trong mã nguồn"
                + (indirect > 0 ? $" ({Count(indirect, "trigger")} trên bảng khác gọi các hàm này)" : string.Empty)
                + " - PostgreSQL không theo dõi thân hàm nên DROP / đổi tên vẫn chạy, hàm lỗi lúc được gọi.");
        }
        var columnUse = views.Where(v => v.Columns.Count > 0).ToList();
        if (columnUse.Count > 0)
        {
            var byColumn = columnUse.SelectMany(v => v.Columns.Select(c => (Column: c, View: v.Name)))
                .GroupBy(x => x.Column).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key} ({string.Join(", ", g.Select(x => x.View).Distinct())})");
            lines.Add("Đổi kiểu / xoá cột đang dùng trong view sẽ báo lỗi: " + string.Join("; ", byColumn) + ".");
        }
        return lines;
    }

    private static string Count(int n, string what) => n > 0 ? $"{n} {what}" : string.Empty;

    private static string Join(params string[] parts) => string.Join(", ", parts.Where(p => p.Length > 0));
}
