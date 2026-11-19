namespace DbTools.Core;

/// <summary>
/// "Sao lưu trước khi chạy": những gì thư mục script sẽ đụng tới, để dump phần đang có trong DB trước khi Chạy.
/// <list type="bullet">
/// <item><see cref="DataRelations"/> - bảng bị đổi / mất dữ liệu: INSERT / UPDATE / DELETE / TRUNCATE vào, DROP, ALTER TABLE ... DROP,
/// CREATE TABLE khi bật "Xoá đối tượng cũ rồi tạo lại" → dump cả cấu trúc + dữ liệu.</item>
/// <item><see cref="StructureRelations"/> - bảng / view / sequence bị thay hoặc thêm trigger / index / ràng buộc → chỉ cấu trúc
/// (bảng nền lớn chỉ thêm trigger thì không dump cả triệu dòng).</item>
/// <item><see cref="Functions"/> / <see cref="Triggers"/> - hàm bị CREATE OR REPLACE / DROP, trigger bị tạo lại → định nghĩa hiện tại
/// (pg_get_functiondef / pg_get_triggerdef) ghi ra file .sql.</item>
/// </list>
/// Chỉ là danh sách ứng viên (tên chưa chắc có trong DB): đối chiếu catalog lúc sao lưu, cái nào chưa có thì bỏ.
/// </summary>
public sealed record BackupPlan(
    IReadOnlyList<QualifiedName> DataRelations,
    IReadOnlyList<QualifiedName> StructureRelations,
    IReadOnlyList<QualifiedName> Functions,
    IReadOnlyList<QualifiedName> Triggers)
{
    public bool IsEmpty => DataRelations.Count + StructureRelations.Count + Functions.Count + Triggers.Count == 0;

    private static readonly HashSet<string> DropWords = new(StringComparer.Ordinal)
    {
        "drop", "table", "view", "materialized", "function", "procedure", "sequence", "index", "trigger", "if", "exists",
        "cascade", "restrict", "on", "type", "domain", "schema", "concurrently", "foreign", "rule", "policy", "aggregate",
    };

    public static BackupPlan Build(IReadOnlyList<SqlScriptFile> files, bool dropFirst)
    {
        var data = new List<QualifiedName>();
        var structure = new List<QualifiedName>();
        var functions = new List<QualifiedName>();
        var triggers = new List<QualifiedName>();

        foreach (var s in files.SelectMany(f => f.Statements))
        {
            switch (s.Kind)
            {
                case StatementKind.Insert or StatementKind.Update or StatementKind.Delete or StatementKind.Truncate when s.Table is not null:
                    data.Add(s.Table);
                    break;
                case StatementKind.AlterOther when s.Table is not null:
                    (s.Names.Contains("drop") ? data : structure).Add(s.Table);
                    break;
                case StatementKind.CreateTable when s.Object is not null:
                    (dropFirst ? data : structure).Add(s.Object);
                    break;
                case StatementKind.CreateView or StatementKind.CreateSequence when s.Object is not null:
                    structure.Add(s.Object);
                    break;
                case StatementKind.CreateIndex or StatementKind.AddConstraint when s.Table is not null:
                    structure.Add(s.Table);
                    break;
                case StatementKind.CreateTrigger when s.Object is not null:
                    triggers.Add(s.Object);
                    if (s.Table is not null)
                    {
                        structure.Add(s.Table);
                    }
                    break;
                case StatementKind.CreateFunction or StatementKind.CreateProcedure when s.Object is not null:
                    functions.Add(s.Object);
                    break;
                case StatementKind.Drop:
                    // Không biết chắc tên nào là đối tượng bị xoá → mọi tên trong câu (trừ từ khoá) đều là ứng viên; đối chiếu
                    // catalog sẽ bỏ tên không có thật.
                    foreach (var name in s.Names.Where(n => !DropWords.Contains(n)))
                    {
                        data.Add(new QualifiedName(null, name, false));
                        functions.Add(new QualifiedName(null, name, false));
                    }
                    break;
            }
        }
        var dataSet = Distinct(data);
        var dataKeys = dataSet.Select(Key).ToHashSet(StringComparer.Ordinal);
        return new BackupPlan(dataSet, Distinct(structure).Where(n => !dataKeys.Contains(Key(n))).ToList(), Distinct(functions), Distinct(triggers));
    }

    private static string Key(QualifiedName n) => (n.Schema ?? string.Empty) + "." + n.Name;

    private static List<QualifiedName> Distinct(IEnumerable<QualifiedName> names) =>
        names.GroupBy(Key, StringComparer.Ordinal).Select(g => g.First()).ToList();
}
