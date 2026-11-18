namespace DbTools.Core;

/// <summary>Đối tượng đã có trong DB. <paramref name="Kind"/>: table, view, materialized view, sequence, index, func, trigger,
/// constraint, type, schema. <paramref name="Table"/>: bảng của trigger / ràng buộc.</summary>
public sealed record ExistingObject(string Kind, string Name, string Table);

/// <summary>
/// Đối chiếu các câu CREATE với đối tượng đã có trong DB (Kiểm tra trước khi chạy): CREATE không có OR REPLACE / IF NOT EXISTS
/// mà đối tượng đã có → câu đó sẽ lỗi (Cảnh báo), trừ khi bật "Xoá đối tượng cũ rồi tạo lại" (Thông tin).
/// </summary>
public static class Precheck
{
    /// <summary>Mọi tên đối tượng các file tạo ra - để hỏi catalog 1 lần.</summary>
    public static IReadOnlyCollection<string> CreatedNames(IEnumerable<SqlScriptFile> files) =>
        files.SelectMany(f => f.CreatedNames).Distinct(StringComparer.Ordinal).ToList();

    public static List<ScriptIssue> Compare(SqlScriptFile file, IReadOnlyList<ExistingObject> existing, bool dropFirst)
    {
        var issues = new List<ScriptIssue>();
        foreach (var s in file.Statements.Where(s => s.CreatesObject && s.Object is not null))
        {
            var match = existing.FirstOrDefault(e => Matches(s, e));
            if (match is null)
            {
                continue;
            }
            var what = $"{s.Verb} {s.Object}";
            if (s.Rerunnable)
            {
                issues.Add(new ScriptIssue(IssueLevel.Info, s.Statement.StartLine, $"{what}: đã có trong DB, sẽ được thay (OR REPLACE / IF NOT EXISTS)."));
            }
            else if (dropFirst)
            {
                issues.Add(new ScriptIssue(IssueLevel.Info, s.Statement.StartLine, $"{what}: đã có trong DB, sẽ xoá rồi tạo lại."));
            }
            else
            {
                issues.Add(new ScriptIssue(IssueLevel.Warning, s.Statement.StartLine,
                    $"{what}: đã có trong DB ({match.Kind}) - câu này sẽ lỗi. Bật \"Xoá đối tượng cũ rồi tạo lại\" hoặc xoá tay trước."));
            }
        }
        return issues;
    }

    private static bool Matches(StatementInfo s, ExistingObject e)
    {
        if (!string.Equals(s.Object!.Key, e.Name, StringComparison.Ordinal))
        {
            return false;
        }
        static bool IsRelation(string kind) => kind is "table" or "view" or "materialized view" or "sequence" or "index" or "relation";
        return s.Kind switch
        {
            // Bảng / view / sequence / index dùng chung 1 không gian tên (pg_class): trùng tên là lỗi dù khác loại.
            StatementKind.CreateTable or StatementKind.CreateView or StatementKind.CreateSequence or StatementKind.CreateIndex => IsRelation(e.Kind),
            StatementKind.CreateFunction or StatementKind.CreateProcedure => e.Kind == "func",
            StatementKind.CreateTrigger => e.Kind == "trigger" && (s.Table is null || s.Table.Key == e.Table),
            StatementKind.AddConstraint => e.Kind == "constraint" && (s.Table is null || s.Table.Key == e.Table),
            StatementKind.CreateType or StatementKind.CreateDomain => e.Kind == "type",
            StatementKind.CreateSchema => e.Kind == "schema",
            _ => false,
        };
    }
}
