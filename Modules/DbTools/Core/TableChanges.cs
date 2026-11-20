namespace DbTools.Core;

public enum TableChangeKind
{
    DropTable,
    /// <summary>ALTER TABLE ... RENAME TO / SET SCHEMA.</summary>
    RenameTable,
    DropColumn,
    /// <summary>ALTER COLUMN c TYPE / SET DATA TYPE.</summary>
    AlterColumnType,
    RenameColumn,
}

/// <summary>1 thao tác đổi bảng đang có trong script. <paramref name="FromDropFirst"/>: câu DROP của "Xoá đối tượng cũ rồi tạo lại".</summary>
public sealed record TableChange(TableChangeKind Kind, QualifiedName Table, string? Column, string File, int Line, bool Cascade = false,
    bool FromDropFirst = false)
{
    public string Text => Kind switch
    {
        TableChangeKind.DropTable => $"DROP TABLE {Table}{(Cascade ? " CASCADE" : string.Empty)}",
        TableChangeKind.RenameTable => $"đổi tên / chuyển schema bảng {Table}",
        TableChangeKind.DropColumn => $"xoá cột {Table}.{Column}",
        TableChangeKind.AlterColumnType => $"đổi kiểu cột {Table}.{Column}",
        _ => $"đổi tên cột {Table}.{Column}",
    };

    public string Where => FromDropFirst ? "Xoá đối tượng cũ rồi tạo lại" : $"{File} dòng {Line}";
}

/// <summary>
/// Trang Chạy - cảnh báo phụ thuộc: tìm trong script các câu xoá / sửa bảng đang có (<see cref="Find"/>), rồi từ danh sách đối
/// tượng dùng bảng đó (<c>PgInfo.DependentsAsync</c>, như tab Phụ thuộc) chọn ra cái bị ảnh hưởng thật (<see cref="Describe"/>).
/// </summary>
public static class TableChanges
{
    public static List<TableChange> Find(IEnumerable<SqlScriptFile> files)
    {
        var result = new List<TableChange>();
        foreach (var file in files)
        {
            foreach (var s in file.Statements)
            {
                var tokens = SqlLexer.Tokenize(s.Statement.Text);
                if (tokens.Count >= 3 && tokens[0].IsWord("drop") && tokens[1].IsWord("table"))
                {
                    var cascade = tokens.Any(t => t.IsWord("cascade"));
                    var i = 2;
                    Skip(tokens, ref i, "if", "exists");
                    while (ReadName(tokens, ref i) is { } name)
                    {
                        result.Add(new TableChange(TableChangeKind.DropTable, name, null, file.RelativePath, s.Statement.StartLine, cascade));
                        if (i >= tokens.Count || !tokens[i].IsSymbol(','))
                        {
                            break;
                        }
                        i++;
                    }
                }
                else if (tokens.Count >= 3 && tokens[0].IsWord("alter") && tokens[1].IsWord("table"))
                {
                    var i = 2;
                    Skip(tokens, ref i, "if", "exists");
                    Skip(tokens, ref i, "only");
                    if (ReadName(tokens, ref i) is not { } table)
                    {
                        continue;
                    }
                    foreach (var action in Actions(tokens, i))
                    {
                        if (ReadAction(action, table, file.RelativePath, s.Statement.StartLine) is { } change)
                        {
                            result.Add(change);
                        }
                    }
                }
            }
        }
        return result;
    }

    /// <summary>Các thao tác của ALTER TABLE cách nhau bằng dấu phẩy ngoài ngoặc.</summary>
    private static IEnumerable<List<SqlToken>> Actions(List<SqlToken> tokens, int from)
    {
        var current = new List<SqlToken>();
        var depth = 0;
        for (var i = from; i < tokens.Count; i++)
        {
            var t = tokens[i];
            depth += t.IsSymbol('(') ? 1 : t.IsSymbol(')') ? -1 : 0;
            if (t.IsSymbol(',') && depth == 0)
            {
                yield return current;
                current = [];
                continue;
            }
            current.Add(t);
        }
        yield return current;
    }

    private static TableChange? ReadAction(List<SqlToken> a, QualifiedName table, string file, int line)
    {
        if (a.Count == 0)
        {
            return null;
        }
        var i = 1;
        TableChange Make(TableChangeKind kind, string? column = null) => new(kind, table, column, file, line);
        if (a[0].IsWord("drop"))
        {
            if (i < a.Count && a[i].IsWord("constraint"))
            {
                return null;
            }
            Skip(a, ref i, "column");
            Skip(a, ref i, "if", "exists");
            return i < a.Count && a[i].IsName ? Make(TableChangeKind.DropColumn, a[i].Value) : null;
        }
        if (a[0].IsWord("alter"))
        {
            Skip(a, ref i, "column");
            if (i + 1 < a.Count && a[i].IsName && (a[i + 1].IsWord("type") || (a[i + 1].IsWord("set") && i + 2 < a.Count && a[i + 2].IsWord("data"))))
            {
                return Make(TableChangeKind.AlterColumnType, a[i].Value);
            }
            return null;
        }
        if (a[0].IsWord("rename"))
        {
            if (i < a.Count && a[i].IsWord("to"))
            {
                return Make(TableChangeKind.RenameTable);
            }
            if (i < a.Count && a[i].IsWord("constraint"))
            {
                return null;
            }
            Skip(a, ref i, "column");
            return i < a.Count && a[i].IsName ? Make(TableChangeKind.RenameColumn, a[i].Value) : null;
        }
        return a[0].IsWord("set") && a.Count > 1 && a[1].IsWord("schema") ? Make(TableChangeKind.RenameTable) : null;
    }

    /// <summary>
    /// Đối tượng bị ảnh hưởng bởi <paramref name="change"/> trong <paramref name="dependents"/> (đối tượng dùng bảng đó), bỏ những
    /// cái chính thư mục tạo lại (<paramref name="recreated"/>: tên không schema). Mỗi dòng 1 cảnh báo ngắn.
    /// </summary>
    public static List<string> Describe(TableChange change, IReadOnlyList<DependentItem> dependents, IReadOnlySet<string> recreated)
    {
        var deps = dependents.Where(d => !recreated.Contains(BareName(d))).ToList();
        var views = deps.Where(d => d.Kind == DependentKind.View).ToList();
        var fks = deps.Where(d => d.Kind == DependentKind.ForeignKey).ToList();
        var tracked = deps.Where(d => d.Kind == DependentKind.TrackedFunction).ToList();
        var functions = deps.Where(d => d.Kind == DependentKind.Function).ToList();
        var lines = new List<string>();
        string Names(IEnumerable<DependentItem> items) => Shorten(items.Select(d => d.Name).ToList());

        switch (change.Kind)
        {
            case TableChangeKind.DropTable:
                if (views.Count + fks.Count + tracked.Count > 0)
                {
                    var used = Names(views.Concat(fks).Concat(tracked));
                    lines.Add(change.Cascade
                        ? $"CASCADE sẽ xoá / bỏ kèm: {used}"
                        : $"sẽ LỖI (rollback cả lô) vì đang được dùng bởi: {used}");
                }
                if (functions.Count > 0)
                {
                    lines.Add($"hàm nhắc tên bảng sẽ lỗi lúc chạy: {Names(functions)}");
                }
                break;
            case TableChangeKind.RenameTable:
                if (functions.Count > 0)
                {
                    lines.Add($"hàm gọi bảng theo tên cũ sẽ lỗi lúc chạy (view / khoá ngoại tự theo tên mới): {Names(functions)}");
                }
                break;
            default:
                var column = change.Column!;
                var viewCols = views.Where(v => v.Columns.Contains(column)).ToList();
                var fkCols = fks.Where(f => f.Columns.Contains(column)).ToList();
                var fnCols = functions.Where(f => SourceSearch.Find(f.Detail, column, wholeWord: true, caseSensitive: false).Count > 0).ToList();
                if (change.Kind == TableChangeKind.DropColumn && viewCols.Count + fkCols.Count > 0)
                {
                    lines.Add($"sẽ LỖI (rollback cả lô) vì cột đang được dùng bởi: {Names(viewCols.Concat(fkCols))}");
                }
                if (change.Kind == TableChangeKind.AlterColumnType && viewCols.Count > 0)
                {
                    lines.Add($"sẽ LỖI (rollback cả lô): PostgreSQL không cho đổi kiểu cột view đang dùng - {Names(viewCols)}");
                }
                if (fnCols.Count > 0)
                {
                    lines.Add(change.Kind == TableChangeKind.AlterColumnType
                        ? $"xem lại hàm dùng cột (kiểu mới có thể không khớp): {Names(fnCols)}"
                        : $"hàm nhắc tên cột sẽ lỗi lúc chạy: {Names(fnCols)}");
                }
                break;
        }
        return lines.Select(l => $"{change.Text} ({change.Where}): {l}").ToList();
    }

    /// <summary>Tên không schema / tham số để so với tên thư mục tạo ra: "s.v" → v, "s.f(int)" → f, "tr trên s.t" → tr.</summary>
    private static string BareName(DependentItem d)
    {
        var name = d.Kind is DependentKind.IndirectTrigger or DependentKind.Trigger ? d.Name.Split(' ')[0] : d.Name;
        var paren = name.IndexOf('(');
        if (paren > 0)
        {
            name = name[..paren];
        }
        var dot = name.LastIndexOf('.');
        return (dot >= 0 ? name[(dot + 1)..] : name).Trim('"');
    }

    private static string Shorten(List<string> names) =>
        string.Join(", ", names.Take(6)) + (names.Count > 6 ? $" ... và {names.Count - 6} đối tượng khác" : string.Empty);

    private static void Skip(List<SqlToken> tokens, ref int i, params string[] words)
    {
        var j = i;
        foreach (var w in words)
        {
            if (j >= tokens.Count || !tokens[j].IsWord(w))
            {
                return;
            }
            j++;
        }
        i = j;
    }

    private static QualifiedName? ReadName(List<SqlToken> tokens, ref int i)
    {
        if (i >= tokens.Count || !tokens[i].IsName)
        {
            return null;
        }
        var first = tokens[i++];
        if (i + 1 < tokens.Count && tokens[i].IsSymbol('.') && tokens[i + 1].IsName)
        {
            var second = tokens[i + 1];
            i += 2;
            return new QualifiedName(first.Value, second.Value, second.Kind == SqlTokenKind.QuotedName);
        }
        return new QualifiedName(null, first.Value, first.Kind == SqlTokenKind.QuotedName);
    }
}
