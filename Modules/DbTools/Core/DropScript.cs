using System.Text;

namespace DbTools.Core;

/// <summary>1 câu DROP sinh ra: <paramref name="Sql"/> không có dấu ; cuối.</summary>
public sealed record DropStatement(string Sql, StatementKind SourceKind, QualifiedName Object, string SourceFile, int SourceLine);

/// <summary>Script xoá ngược + các câu không tự đảo ngược được (thêm / sửa / xoá dữ liệu).</summary>
public sealed record DropScript(IReadOnlyList<DropStatement> Statements, IReadOnlyList<string> NotReversible)
{
    /// <summary>Bảng sẽ bị xoá (mất cả dữ liệu) nếu chạy script này.</summary>
    public IEnumerable<QualifiedName> Tables => Statements.Where(s => s.SourceKind == StatementKind.CreateTable).Select(s => s.Object);

    public string ToSql(string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- {title}");
        sb.AppendLine($"-- Tạo bởi DbTools lúc {DateTime.Now:yyyy-MM-dd HH:mm:ss}. Xoá các đối tượng mà thư mục script tạo ra, theo thứ tự ngược lại.");
        sb.AppendLine("-- Không dùng CASCADE: đối tượng khác (ngoài thư mục) còn dùng tới thì câu DROP báo lỗi thay vì xoá lan.");
        sb.AppendLine("-- CHÚ Ý: DROP TABLE xoá luôn dữ liệu trong bảng.");
        sb.AppendLine();
        string? lastFile = null;
        foreach (var s in Statements)
        {
            if (s.SourceFile != lastFile)
            {
                sb.AppendLine($"-- {s.SourceFile}");
                lastFile = s.SourceFile;
            }
            sb.AppendLine(s.Sql + ";");
        }
        if (NotReversible.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("-- Các câu dưới đây thay đổi dữ liệu, không tự đảo ngược được - kiểm tra tay:");
            foreach (var line in NotReversible)
            {
                sb.AppendLine("--   " + line);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Đi ngược thứ tự chạy, mỗi đối tượng được tạo → 1 câu DROP ... IF EXISTS. Index / ràng buộc gắn vào bảng /
    /// view mà chính thư mục tạo ra thì bỏ (xoá bảng là mất theo). Đối tượng tạo nhiều lần (CREATE OR REPLACE ở 2 file) chỉ xoá 1 lần.
    /// <paramref name="schema"/> (schema đã chọn ở trang Chạy): bảng / view / hàm / sequence / kiểu không ghi schema trong file được
    /// ghi rõ schema đó - search_path còn có public thì DROP không ghi schema có thể xoá nhầm đối tượng cùng tên ở public.
    /// Index / trigger / ràng buộc trên bảng có sẵn (không do thư mục tạo) giữ nguyên, tìm theo search_path như lúc tạo.
    /// </summary>
    public static DropScript Build(IReadOnlyList<SqlScriptFile> files, string? schema = null)
    {
        QualifiedName Own(QualifiedName q) => schema is null || q.Schema is not null ? q : q with { Schema = schema };

        var all = files.SelectMany(f => f.Statements.Select(s => (File: f, Info: s))).ToList();
        var createdRelations = all
            .Where(x => x.Info.Kind is StatementKind.CreateTable or StatementKind.CreateView && x.Info.Object is not null)
            .Select(x => x.Info.Object!.Key)
            .ToHashSet(StringComparer.Ordinal);

        var statements = new List<DropStatement>();
        var notReversible = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var k = all.Count - 1; k >= 0; k--)
        {
            var (file, s) = all[k];
            var line = s.Statement.StartLine;
            if (s.Kind is StatementKind.Insert or StatementKind.Update or StatementKind.Delete or StatementKind.Truncate)
            {
                notReversible.Insert(0, $"{file.RelativePath} dòng {line}: {s.Verb}{(s.Table is null ? string.Empty : " " + s.Table)}");
                continue;
            }
            if (s.Object is null)
            {
                continue;
            }
            var onCreatedRelation = s.Table is not null && createdRelations.Contains(s.Table.Key);
            var obj = s.Kind is StatementKind.CreateTable or StatementKind.CreateView or StatementKind.CreateFunction
                or StatementKind.CreateProcedure or StatementKind.CreateSequence or StatementKind.CreateType or StatementKind.CreateDomain
                ? Own(s.Object)
                : s.Object;
            var sql = s.Kind switch
            {
                // Trigger luôn xoá riêng, kể cả trên bảng do thư mục tạo: hàm trigger thường được tạo trước trigger nên xoá
                // trước bảng - còn trigger thì DROP FUNCTION lỗi. Bảng chưa có thì IF EXISTS chỉ báo NOTICE.
                StatementKind.CreateTrigger when s.Table is not null =>
                    $"DROP TRIGGER IF EXISTS {obj.Sql} ON {(onCreatedRelation ? Own(s.Table) : s.Table).Sql}",
                StatementKind.CreateIndex when !onCreatedRelation => $"DROP INDEX IF EXISTS {obj.Sql}",
                StatementKind.AddConstraint when !onCreatedRelation && s.Table is not null => $"ALTER TABLE IF EXISTS {s.Table.Sql} DROP CONSTRAINT IF EXISTS {obj.Sql}",
                StatementKind.CreateView => $"DROP {(s.Materialized ? "MATERIALIZED " : string.Empty)}VIEW IF EXISTS {obj.Sql}",
                StatementKind.CreateFunction => $"DROP FUNCTION IF EXISTS {obj.Sql}{(s.FunctionArgs is null ? string.Empty : "(" + s.FunctionArgs + ")")}",
                StatementKind.CreateProcedure => $"DROP PROCEDURE IF EXISTS {obj.Sql}{(s.FunctionArgs is null ? string.Empty : "(" + s.FunctionArgs + ")")}",
                StatementKind.CreateTable => $"DROP TABLE IF EXISTS {obj.Sql}",
                StatementKind.CreateSequence => $"DROP SEQUENCE IF EXISTS {obj.Sql}",
                StatementKind.CreateType => $"DROP TYPE IF EXISTS {obj.Sql}",
                StatementKind.CreateDomain => $"DROP DOMAIN IF EXISTS {obj.Sql}",
                StatementKind.CreateSchema => $"DROP SCHEMA IF EXISTS {obj.Sql}",
                _ => null,
            };
            if (sql is not null && seen.Add(sql))
            {
                statements.Add(new DropStatement(sql, s.Kind, obj, file.RelativePath, line));
            }
        }
        return new DropScript(statements, notReversible);
    }
}
