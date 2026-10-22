using System;
using System.Collections.Generic;
using System.Linq;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>Mức của 1 phát hiện: Nên sửa (đỏ) / Cân nhắc (vàng) / Thông tin (xám).</summary>
public enum QualitySeverity
{
    Error,
    Warning,
    Info,
}

/// <summary>1 quy tắc kiểm tra (tiêu đề + vì sao đáng để ý) - trang kết quả nhóm phát hiện theo quy tắc.</summary>
public sealed record QualityRule(string Id, QualitySeverity Severity, string Title, string Why);

/// <summary>1 chỗ vi phạm. <paramref name="Sql"/> = câu lệnh gợi ý (chỉ tham khảo, không tự chạy), null nếu không có.</summary>
public sealed record QualityFinding(QualityRule Rule, string TableName, string Message, string? Sql = null);

/// <summary>Kết quả kiểm tra 1 file .db. <paramref name="IndexesChecked"/> = false: .db đọc bằng bản cũ (chưa có index /
/// thống kê) nên bỏ các quy tắc cần dữ liệu đó.</summary>
public sealed record QualityReport(string Name, int TableCount, int ViewCount, List<QualityFinding> Findings, bool IndexesChecked)
{
    public int Count(QualitySeverity severity) => Findings.Count(f => f.Rule.Severity == severity);
}

/// <summary>
/// "Kiểm tra chất lượng schema": soát những chỗ thiết kế đáng để ý trong 1 lần đọc (file .db), không cần kết nối lại DB.
/// Bảng thật mới xét khoá chính / khoá ngoại / index / thống kê; view chỉ xét mô tả và tên. Index theo biểu thức (có dấu
/// ngoặc) không dùng để so cột vì không tách được cột chắc chắn.
/// </summary>
public static class SchemaQualityChecker
{
    public static readonly QualityRule NoPrimaryKey = new("no-pk", QualitySeverity.Error, "Bảng không có khoá chính",
        "Không xác định được 1 dòng duy nhất: dễ có dữ liệu trùng, khó sửa / xoá đúng 1 dòng; nhiều công cụ đồng bộ / replication cần khoá chính.");

    public static readonly QualityRule ForeignKeyTypeMismatch = new("fk-type", QualitySeverity.Error, "Khoá ngoại khác kiểu với cột được tham chiếu",
        "So sánh phải ép kiểu (JOIN chậm, có thể không dùng được index); độ dài / phạm vi khác nhau dễ lỗi khi dữ liệu lớn dần.");

    public static readonly QualityRule ForeignKeyWithoutIndex = new("fk-index", QualitySeverity.Warning, "Khoá ngoại không có index",
        "JOIN theo khoá ngoại và lệnh xoá / sửa khoá ở bảng cha phải quét cả bảng con (có thể khoá cả bảng). PostgreSQL / Oracle / SQL Server không tự tạo index cho khoá ngoại.");

    public static readonly QualityRule RedundantIndex = new("dup-index", QualitySeverity.Warning, "Index trùng hoặc bị index khác bao trùm",
        "Index có cột là phần đầu của 1 index khác thì gần như thừa: không giúp truy vấn thêm mà làm chậm INSERT / UPDATE và tốn chỗ.");

    public static readonly QualityRule NoStatistics = new("no-stats", QualitySeverity.Warning, "Bảng chưa có thống kê số dòng",
        "Bộ tối ưu truy vấn thiếu thông tin có thể chọn sai kế hoạch. Chạy ANALYZE (PostgreSQL) / DBMS_STATS (Oracle) / ANALYZE TABLE (MySQL).");

    public static readonly QualityRule NoTableDescription = new("no-table-desc", QualitySeverity.Info, "Bảng / view thiếu mô tả",
        "Từ điển dữ liệu không nói bảng dùng để làm gì - người mới phải đoán theo tên.");

    public static readonly QualityRule NoColumnDescription = new("no-col-desc", QualitySeverity.Info, "Cột thiếu mô tả",
        "Từ điển dữ liệu không nói cột chứa gì (đơn vị, mã nghĩa là gì...).");

    public static readonly QualityRule NamingStyle = new("naming", QualitySeverity.Info, "Tên không theo quy ước chung",
        "Tên lẫn kiểu (snake_case với camelCase) hoặc có khoảng trắng / ký tự đặc biệt phải đặt trong ngoặc kép mỗi lần viết SQL, dễ gõ nhầm.");

    public static IReadOnlyList<QualityRule> AllRules { get; } =
        [NoPrimaryKey, ForeignKeyTypeMismatch, ForeignKeyWithoutIndex, RedundantIndex, NoStatistics, NoTableDescription, NoColumnDescription, NamingStyle];

    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    public static bool IsView(DbTableRecord table) => table.Kind.Contains("VIEW", StringComparison.OrdinalIgnoreCase);

    public static QualityReport Check(SchemaSnapshot snapshot)
    {
        var findings = new List<QualityFinding>();
        var columnsByTable = snapshot.Columns.GroupBy(c => c.TableName, NameComparer)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.OrdinalPosition).ToList(), NameComparer);
        var indexesByTable = snapshot.Indexes.GroupBy(i => i.TableName, NameComparer).ToDictionary(g => g.Key, g => g.ToList(), NameComparer);
        var tablesByName = snapshot.Tables.ToDictionary(t => t.TableName, NameComparer);
        var extended = snapshot.HasExtendedInfo;

        foreach (var table in snapshot.Tables)
        {
            var columns = columnsByTable.GetValueOrDefault(table.TableName) ?? [];
            var indexes = indexesByTable.GetValueOrDefault(table.TableName) ?? [];

            if (!IsView(table))
            {
                if (!columns.Any(c => c.Level == 0) && !indexes.Any(i => i.IsPrimaryKey))
                {
                    findings.Add(new(NoPrimaryKey, table.TableName, $"{table.TableName} ({columns.Count} cột) không có khoá chính."));
                }

                if (extended && table.EstimatedRows is null)
                {
                    findings.Add(new(NoStatistics, table.TableName, $"{table.TableName}: database chưa có thống kê số dòng."));
                }
            }

            if (string.IsNullOrWhiteSpace(table.Description))
            {
                findings.Add(new(NoTableDescription, table.TableName, $"{table.TableName}{(IsView(table) ? " (view)" : "")} chưa có mô tả."));
            }

            var undescribed = columns.Where(c => string.IsNullOrWhiteSpace(c.Description)).Select(c => c.ColumnName).ToList();
            if (undescribed.Count > 0)
            {
                findings.Add(new(NoColumnDescription, table.TableName,
                    $"{table.TableName}: {undescribed.Count} / {columns.Count} cột thiếu mô tả - {string.Join(", ", undescribed)}"));
            }

            if (extended && !IsView(table))
            {
                findings.AddRange(CheckRedundantIndexes(table.TableName, indexes));
            }
        }

        foreach (var foreignKey in snapshot.ForeignKeys)
        {
            if (!tablesByName.TryGetValue(foreignKey.TableName, out var table) || IsView(table))
            {
                continue;
            }
            var localColumns = SplitList(foreignKey.LocalColumns, ',');
            var referencedColumns = SplitList(string.IsNullOrEmpty(foreignKey.ReferencedColumns) ? foreignKey.LocalColumns : foreignKey.ReferencedColumns, ',');
            var label = $"{foreignKey.TableName} ({string.Join(", ", localColumns)}) → {foreignKey.ReferencedTable} ({string.Join(", ", referencedColumns)})";

            // Kiểu: chỉ so khi bảng tham chiếu nằm trong lần đọc (schema khác không đọc thì không có cột của nó).
            var local = columnsByTable.GetValueOrDefault(foreignKey.TableName) ?? [];
            var referenced = columnsByTable.GetValueOrDefault(foreignKey.ReferencedTable);
            if (referenced is not null && localColumns.Count == referencedColumns.Count)
            {
                var mismatches = new List<string>();
                for (var i = 0; i < localColumns.Count; i++)
                {
                    var a = local.Find(c => NameComparer.Equals(c.ColumnName, localColumns[i]));
                    var b = referenced.Find(c => NameComparer.Equals(c.ColumnName, referencedColumns[i]));
                    if (a is not null && b is not null && !NameComparer.Equals(FullType(a), FullType(b)))
                    {
                        mismatches.Add($"{a.ColumnName} {FullType(a)} ≠ {b.ColumnName} {FullType(b)}");
                    }
                }
                if (mismatches.Count > 0)
                {
                    findings.Add(new(ForeignKeyTypeMismatch, foreignKey.TableName, $"{label}: {string.Join("; ", mismatches)}"));
                }
            }

            if (extended)
            {
                var indexes = indexesByTable.GetValueOrDefault(foreignKey.TableName) ?? [];
                var covered = indexes.Any(i => IndexColumns(i) is { } cols && cols.Count >= localColumns.Count
                    && localColumns.Select((c, n) => NameComparer.Equals(c, cols[n])).All(x => x));
                if (!covered)
                {
                    var name = SafeIdentifier($"ix_{BareName(foreignKey.TableName)}_{string.Join("_", localColumns)}");
                    findings.Add(new(ForeignKeyWithoutIndex, foreignKey.TableName, label,
                        $"CREATE INDEX {name} ON {foreignKey.TableName} ({string.Join(", ", localColumns)});"));
                }
            }
        }

        findings.AddRange(CheckNaming(snapshot.Tables, snapshot.Columns));

        // Thứ tự: theo quy tắc (AllRules), trong quy tắc theo tên bảng.
        var order = AllRules.Select((r, i) => (r, i)).ToDictionary(x => x.r.Id, x => x.i);
        findings = findings.OrderBy(f => order[f.Rule.Id]).ThenBy(f => f.TableName, NameComparer).ToList();
        var viewCount = snapshot.Tables.Count(IsView);
        return new QualityReport(snapshot.Name, snapshot.Tables.Count - viewCount, viewCount, findings, extended);
    }

    /// <summary>Index A thừa khi cột của A là phần đầu (hoặc trùng hẳn) cột của index B. Không báo A là khoá chính / UNIQUE
    /// (còn giữ ràng buộc) hay partial / filtered (WHERE - phạm vi khác) hay theo biểu thức.</summary>
    private static IEnumerable<QualityFinding> CheckRedundantIndexes(string tableName, List<DbIndexRecord> indexes)
    {
        var reported = new HashSet<string>(NameComparer);
        foreach (var a in indexes)
        {
            if (a.IsPrimaryKey || a.IsUnique || a.Note.Contains("WHERE", StringComparison.OrdinalIgnoreCase) || IndexColumns(a) is not { } colsA)
            {
                continue;
            }
            foreach (var b in indexes)
            {
                if (ReferenceEquals(a, b) || IndexColumns(b) is not { } colsB || colsB.Count < colsA.Count
                    || b.Note.Contains("WHERE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // Cùng cột hẳn mà B cũng là index thường: chỉ báo 1 trong 2 (A đứng trước theo tên).
                var same = colsA.Count == colsB.Count;
                if (same && !b.IsPrimaryKey && !b.IsUnique && NameComparer.Compare(a.IndexName, b.IndexName) > 0)
                {
                    continue;
                }
                if (colsA.Select((c, n) => NameComparer.Equals(c, colsB[n])).All(x => x) && reported.Add(a.IndexName))
                {
                    var relation = same ? "trùng cột với" : "bị bao trùm bởi";
                    yield return new(RedundantIndex, tableName,
                        $"{tableName}: {a.IndexName} ({a.Columns}) {relation} {b.IndexName} ({b.Columns})",
                        $"DROP INDEX {a.IndexName};");
                    break;
                }
            }
        }
    }

    /// <summary>Quy ước = kiểu chiếm đa số trong cùng loại tên (bảng / cột): snake_case (chữ thường hoặc HOA + _) hay
    /// camelCase / PascalCase. Tên khác kiểu đa số, hoặc có khoảng trắng / ký tự lạ, bị báo.</summary>
    private static IEnumerable<QualityFinding> CheckNaming(List<DbTableRecord> tables, List<DbColumnRecord> columns)
    {
        var tableStyles = tables.Select(t => (t.TableName, Name: BareName(t.TableName), Style: StyleOf(BareName(t.TableName)))).ToList();
        var tableMajority = Majority(tableStyles.Select(x => x.Style));
        foreach (var (table, name, style) in tableStyles)
        {
            if (Problem(name, style, tableMajority) is { } problem)
            {
                yield return new(NamingStyle, table, $"Bảng {table}: {problem}.");
            }
        }

        var columnStyles = columns.Select(c => (c.TableName, c.ColumnName, Style: StyleOf(c.ColumnName))).ToList();
        var columnMajority = Majority(columnStyles.Select(x => x.Style));
        foreach (var group in columnStyles.GroupBy(x => x.TableName, NameComparer))
        {
            var odd = group.Select(x => (x.ColumnName, Problem: Problem(x.ColumnName, x.Style, columnMajority)))
                .Where(x => x.Problem is not null).ToList();
            if (odd.Count > 0)
            {
                yield return new(NamingStyle, group.Key,
                    $"{group.Key}: {string.Join("; ", odd.Select(x => $"cột {x.ColumnName} {x.Problem}"))}.");
            }
        }
    }

    private enum NameStyle { Snake, Camel, Single, Special }

    private static NameStyle StyleOf(string name)
    {
        if (name.Length == 0 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '$' || c == '#')))
        {
            return NameStyle.Special;
        }
        var hasUpper = name.Any(char.IsAsciiLetterUpper);
        var hasLower = name.Any(char.IsAsciiLetterLower);
        if (name.Contains('_'))
        {
            return hasUpper && hasLower ? NameStyle.Special : NameStyle.Snake; // Order_Date: lẫn cả 2 kiểu
        }
        // 1 từ (id, ORDERS) hợp với cả 2 kiểu; có cả hoa lẫn thường mà không có _ = camel / Pascal.
        return hasUpper && hasLower ? NameStyle.Camel : NameStyle.Single;
    }

    private static NameStyle? Majority(IEnumerable<NameStyle> styles)
    {
        var counts = styles.Where(s => s is NameStyle.Snake or NameStyle.Camel).GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
        var snake = counts.GetValueOrDefault(NameStyle.Snake);
        var camel = counts.GetValueOrDefault(NameStyle.Camel);
        return snake == camel ? null : snake > camel ? NameStyle.Snake : NameStyle.Camel;
    }

    private static string? Problem(string name, NameStyle style, NameStyle? majority) => style switch
    {
        NameStyle.Special when name.Contains('_') && name.Any(char.IsAsciiLetterUpper) && name.Any(char.IsAsciiLetterLower)
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') => "lẫn chữ hoa / thường với dấu _",
        NameStyle.Special => "có khoảng trắng / ký tự đặc biệt (phải đặt trong ngoặc kép khi viết SQL)",
        NameStyle.Snake when majority == NameStyle.Camel => "dạng snake_case, khác đa số (camelCase)",
        NameStyle.Camel when majority == NameStyle.Snake => "dạng camelCase, khác đa số (snake_case)",
        _ => null,
    };

    /// <summary>Kiểu đầy đủ để so khoá ngoại: DataType đã gồm độ dài ở cả 4 nguồn; Length riêng (nếu có) thì nối thêm.</summary>
    private static string FullType(DbColumnRecord column) =>
        !string.IsNullOrEmpty(column.Length) && !column.DataType.Contains('(', StringComparison.Ordinal)
            ? $"{column.DataType}({column.Length})"
            : column.DataType;

    /// <summary>Cột của index theo thứ tự, bỏ ASC / DESC. Null = index theo biểu thức (có dấu ngoặc) - không tách chắc được.</summary>
    private static List<string>? IndexColumns(DbIndexRecord index)
    {
        if (index.Columns.Length == 0 || index.Columns.Contains('(', StringComparison.Ordinal))
        {
            return null;
        }
        return SplitList(index.Columns, ',')
            .Select(c => c.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase) ? c[..^5].Trim()
                : c.EndsWith(" ASC", StringComparison.OrdinalIgnoreCase) ? c[..^4].Trim() : c)
            .Select(c => c.Trim('"', '[', ']', '`'))
            .ToList();
    }

    private static List<string> SplitList(string text, char separator) =>
        text.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>"sales.orders" → "orders" (tên trùng ở nhiều schema được TableNameQualifier ghi dạng schema.bảng).</summary>
    private static string BareName(string tableName)
    {
        var dot = tableName.LastIndexOf('.');
        return dot >= 0 ? tableName[(dot + 1)..] : tableName;
    }

    private static string SafeIdentifier(string name)
    {
        var chars = name.Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var result = new string(chars);
        return result.Length > 60 ? result[..60] : result;
    }
}
