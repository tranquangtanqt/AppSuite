using System;
using System.Collections.Generic;
using System.Linq;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>Everything one .db holds, for comparing (<see cref="RdbmsHtmlGeneratorDatabase.ReadSnapshot"/>).
/// <paramref name="HasExtendedInfo"/> = false for a .db read by an old version (no index / constraint data).</summary>
public sealed record SchemaSnapshot(
    string Name,
    List<DbTableRecord> Tables,
    List<DbColumnRecord> Columns,
    List<DbForeignKeyRecord> ForeignKeys,
    List<DbIndexRecord> Indexes,
    List<DbConstraintRecord> Constraints,
    bool HasExtendedInfo = true);

/// <summary>1 thuộc tính của cột khác nhau giữa A và B (vd "Kiểu": "VARCHAR(50)" → "VARCHAR(100)").</summary>
public sealed record FieldChange(string Field, string Before, string After);

public sealed record ColumnChange(string ColumnName, List<FieldChange> Changes);

/// <summary>Khác nhau của 1 bảng có ở cả 2 bên. Index / ràng buộc / khoá ngoại hiện bằng chuỗi mô tả
/// (<see cref="SchemaComparer.Describe(DbIndexRecord)"/>...).</summary>
public sealed record TableDiff(string TableName)
{
    public List<FieldChange> TableChanges { get; } = [];
    public List<DbColumnRecord> ColumnsOnlyInA { get; } = [];
    public List<DbColumnRecord> ColumnsOnlyInB { get; } = [];
    public List<ColumnChange> ColumnChanges { get; } = [];
    public List<string> ForeignKeysOnlyInA { get; } = [];
    public List<string> ForeignKeysOnlyInB { get; } = [];
    public List<string> IndexesOnlyInA { get; } = [];
    public List<string> IndexesOnlyInB { get; } = [];
    public List<string> ConstraintsOnlyInA { get; } = [];
    public List<string> ConstraintsOnlyInB { get; } = [];

    /// <summary>Chỉ khác mô tả (comment) - báo cáo cho phép ẩn loại này.</summary>
    public bool OnlyDescriptionChanges =>
        ColumnsOnlyInA.Count == 0 && ColumnsOnlyInB.Count == 0
        && ForeignKeysOnlyInA.Count == 0 && ForeignKeysOnlyInB.Count == 0
        && IndexesOnlyInA.Count == 0 && IndexesOnlyInB.Count == 0
        && ConstraintsOnlyInA.Count == 0 && ConstraintsOnlyInB.Count == 0
        && TableChanges.All(c => c.Field == SchemaComparer.DescriptionField)
        && ColumnChanges.All(c => c.Changes.All(f => f.Field == SchemaComparer.DescriptionField));

    public bool HasChanges =>
        TableChanges.Count > 0 || ColumnsOnlyInA.Count > 0 || ColumnsOnlyInB.Count > 0 || ColumnChanges.Count > 0
        || ForeignKeysOnlyInA.Count > 0 || ForeignKeysOnlyInB.Count > 0
        || IndexesOnlyInA.Count > 0 || IndexesOnlyInB.Count > 0
        || ConstraintsOnlyInA.Count > 0 || ConstraintsOnlyInB.Count > 0;
}

/// <summary>Kết quả so sánh A (gốc) với B. <paramref name="IndexesCompared"/> = false: bỏ qua so index / ràng buộc vì 1 bên
/// là .db đọc bằng bản cũ (không có dữ liệu này).</summary>
public sealed record SchemaDiff(
    string NameA,
    string NameB,
    List<DbTableRecord> TablesOnlyInA,
    List<DbTableRecord> TablesOnlyInB,
    List<TableDiff> ChangedTables,
    int UnchangedTableCount,
    bool IndexesCompared)
{
    public bool IsIdentical => TablesOnlyInA.Count == 0 && TablesOnlyInB.Count == 0 && ChangedTables.Count == 0;
}

/// <summary>
/// So sánh schema của 2 lần đọc (2 file .db: dev ↔ prod, hoặc bản cũ ↔ bản mới). Tên bảng / cột so không phân biệt
/// hoa thường (Oracle viết hoa, PostgreSQL viết thường). Khoá ngoại, index và ràng buộc so theo NỘI DUNG (cột, bảng
/// tham chiếu, loại, định nghĩa) chứ không theo tên - tên do hệ thống tự sinh (SYS_C0012345, PK__orders__3213E83F...)
/// khác nhau ở mỗi môi trường dù định nghĩa giống hệt. Thứ tự cột không tính là khác.
/// </summary>
public static class SchemaComparer
{
    public const string DescriptionField = "Mô tả";

    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    public static SchemaDiff Compare(SchemaSnapshot a, SchemaSnapshot b)
    {
        var tablesA = a.Tables.GroupBy(t => t.TableName, NameComparer).ToDictionary(g => g.Key, g => g.First(), NameComparer);
        var tablesB = b.Tables.GroupBy(t => t.TableName, NameComparer).ToDictionary(g => g.Key, g => g.First(), NameComparer);
        var compareIndexes = a.HasExtendedInfo && b.HasExtendedInfo;

        var columnsA = ByTable(a.Columns, c => c.TableName);
        var columnsB = ByTable(b.Columns, c => c.TableName);
        var foreignKeysA = ByTable(a.ForeignKeys, f => f.TableName);
        var foreignKeysB = ByTable(b.ForeignKeys, f => f.TableName);
        var indexesA = ByTable(a.Indexes, i => i.TableName);
        var indexesB = ByTable(b.Indexes, i => i.TableName);
        var constraintsA = ByTable(a.Constraints, c => c.TableName);
        var constraintsB = ByTable(b.Constraints, c => c.TableName);

        var onlyInA = a.Tables.Where(t => !tablesB.ContainsKey(t.TableName)).OrderBy(t => t.TableName, NameComparer).ToList();
        var onlyInB = b.Tables.Where(t => !tablesA.ContainsKey(t.TableName)).OrderBy(t => t.TableName, NameComparer).ToList();

        var changed = new List<TableDiff>();
        var unchanged = 0;
        foreach (var tableA in tablesA.Values.Where(t => tablesB.ContainsKey(t.TableName)).OrderBy(t => t.TableName, NameComparer))
        {
            var tableB = tablesB[tableA.TableName];
            var diff = new TableDiff(tableA.TableName);

            AddIfDifferent(diff.TableChanges, "Loại", tableA.Kind, tableB.Kind);
            AddIfDifferent(diff.TableChanges, DescriptionField, tableA.Description, tableB.Description);

            CompareColumns(diff, Get(columnsA, tableA.TableName), Get(columnsB, tableA.TableName));

            CompareByContent(Get(foreignKeysA, tableA.TableName), Get(foreignKeysB, tableA.TableName), Describe,
                diff.ForeignKeysOnlyInA, diff.ForeignKeysOnlyInB);
            if (compareIndexes)
            {
                CompareByContent(Get(indexesA, tableA.TableName), Get(indexesB, tableA.TableName), Describe,
                    diff.IndexesOnlyInA, diff.IndexesOnlyInB);
                CompareByContent(Get(constraintsA, tableA.TableName), Get(constraintsB, tableA.TableName), Describe,
                    diff.ConstraintsOnlyInA, diff.ConstraintsOnlyInB);
            }

            if (diff.HasChanges)
            {
                changed.Add(diff);
            }
            else
            {
                unchanged++;
            }
        }

        return new SchemaDiff(a.Name, b.Name, onlyInA, onlyInB, changed, unchanged, compareIndexes);
    }

    private static void CompareColumns(TableDiff diff, List<DbColumnRecord> columnsA, List<DbColumnRecord> columnsB)
    {
        var byNameB = columnsB.GroupBy(c => c.ColumnName, NameComparer).ToDictionary(g => g.Key, g => g.First(), NameComparer);
        var namesA = columnsA.Select(c => c.ColumnName).ToHashSet(NameComparer);

        foreach (var columnA in columnsA)
        {
            if (!byNameB.TryGetValue(columnA.ColumnName, out var columnB))
            {
                diff.ColumnsOnlyInA.Add(columnA);
                continue;
            }

            var changes = new List<FieldChange>();
            AddIfDifferent(changes, "Kiểu", columnA.DataType, columnB.DataType);
            AddIfDifferent(changes, "Độ dài", columnA.Length, columnB.Length);
            AddIfDifferent(changes, "Null", columnA.Nullable, columnB.Nullable);
            AddIfDifferent(changes, "Mặc định", columnA.DefaultValue, columnB.DefaultValue);
            AddIfDifferent(changes, "Khoá chính", IsPrimaryKey(columnA) ? "Có" : "Không", IsPrimaryKey(columnB) ? "Có" : "Không");
            AddIfDifferent(changes, DescriptionField, columnA.Description, columnB.Description);
            if (changes.Count > 0)
            {
                diff.ColumnChanges.Add(new ColumnChange(columnA.ColumnName, changes));
            }
        }

        diff.ColumnsOnlyInB.AddRange(columnsB.Where(c => !namesA.Contains(c.ColumnName)));
    }

    private static bool IsPrimaryKey(DbColumnRecord column) => column.Level == 0;

    /// <summary>So 2 danh sách theo chuỗi mô tả nội dung (không theo tên); trùng nhiều lần thì so cả số lần.</summary>
    private static void CompareByContent<T>(List<T> itemsA, List<T> itemsB, Func<T, string> describe,
        List<string> onlyInA, List<string> onlyInB)
    {
        var remainingB = itemsB.Select(describe).ToList();
        foreach (var text in itemsA.Select(describe))
        {
            var index = remainingB.FindIndex(t => NameComparer.Equals(t, text));
            if (index >= 0)
            {
                remainingB.RemoveAt(index);
            }
            else
            {
                onlyInA.Add(text);
            }
        }

        onlyInB.AddRange(remainingB);
    }

    public static string Describe(DbForeignKeyRecord fk) =>
        $"({fk.LocalColumns}) → {fk.ReferencedTable}({fk.ReferencedColumns})";

    public static string Describe(DbIndexRecord index)
    {
        var kind = index.IsPrimaryKey ? "Khoá chính" : index.IsUnique ? "UNIQUE" : "Thường";
        var type = string.IsNullOrEmpty(index.IndexType) ? "" : $" {index.IndexType}";
        var note = string.IsNullOrEmpty(index.Note) ? "" : $" {index.Note}";
        return $"{kind}{type} ({index.Columns}){note}";
    }

    public static string Describe(DbConstraintRecord constraint) => $"{constraint.ConstraintType} {constraint.Definition}";

    private static void AddIfDifferent(List<FieldChange> changes, string field, string? before, string? after)
    {
        var a = (before ?? string.Empty).Trim();
        var b = (after ?? string.Empty).Trim();
        if (!string.Equals(a, b, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange(field, a, b));
        }
    }

    private static Dictionary<string, List<T>> ByTable<T>(IEnumerable<T> items, Func<T, string> table) =>
        items.GroupBy(table, NameComparer).ToDictionary(g => g.Key, g => g.ToList(), NameComparer);

    private static List<T> Get<T>(Dictionary<string, List<T>> byTable, string table) =>
        byTable.TryGetValue(table, out var list) ? list : [];
}
