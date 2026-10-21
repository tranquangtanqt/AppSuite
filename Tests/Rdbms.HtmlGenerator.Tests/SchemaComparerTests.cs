using Rdbms.HtmlGenerator.Models;
using Rdbms.HtmlGenerator.Services;

namespace Rdbms.HtmlGenerator.Tests;

public class SchemaComparerTests
{
    private static DbTableRecord Table(string name, string description = "") =>
        new() { TableName = name, Kind = "TABLE", Description = description, SourceFile = "", SourceSheet = "" };

    private static DbColumnRecord Column(string table, string name, string type = "VARCHAR", string length = "50",
        string nullable = "Y", bool pk = false, string description = "", string defaultValue = "") =>
        new()
        {
            TableName = table, ColumnName = name, DataType = type, Length = length, Nullable = nullable,
            Level = pk ? 0 : 1, Description = description, DefaultValue = defaultValue,
        };

    private static SchemaSnapshot Snapshot(string name, List<DbTableRecord> tables, List<DbColumnRecord> columns,
        List<DbForeignKeyRecord>? foreignKeys = null, List<DbIndexRecord>? indexes = null,
        List<DbConstraintRecord>? constraints = null, bool extended = true) =>
        new(name, tables, columns, foreignKeys ?? [], indexes ?? [], constraints ?? [], extended);

    [Fact]
    public void Giong_het_nhau_thi_IsIdentical()
    {
        var a = Snapshot("dev", [Table("orders")], [Column("orders", "id", pk: true), Column("orders", "name")]);
        var b = Snapshot("prod", [Table("orders")], [Column("orders", "id", pk: true), Column("orders", "name")]);

        var diff = SchemaComparer.Compare(a, b);

        Assert.True(diff.IsIdentical);
        Assert.Equal(1, diff.UnchangedTableCount);
    }

    [Fact]
    public void Bang_chi_co_o_A_hoac_B()
    {
        var a = Snapshot("dev", [Table("orders"), Table("old_log")], []);
        var b = Snapshot("prod", [Table("orders"), Table("audit")], []);

        var diff = SchemaComparer.Compare(a, b);

        Assert.Equal(["old_log"], diff.TablesOnlyInA.Select(t => t.TableName));
        Assert.Equal(["audit"], diff.TablesOnlyInB.Select(t => t.TableName));
        Assert.Empty(diff.ChangedTables);
    }

    [Fact]
    public void Ten_bang_va_cot_khong_phan_biet_hoa_thuong()
    {
        var a = Snapshot("ora", [Table("ORDERS")], [Column("ORDERS", "ID", pk: true)]);
        var b = Snapshot("pg", [Table("orders")], [Column("orders", "id", pk: true)]);

        Assert.True(SchemaComparer.Compare(a, b).IsIdentical);
    }

    [Fact]
    public void Cot_them_bot_va_doi_kieu_Null_khoa_chinh()
    {
        var a = Snapshot("dev", [Table("orders")],
        [
            Column("orders", "id", pk: true),
            Column("orders", "note", length: "100"),
            Column("orders", "status", nullable: "Y"),
            Column("orders", "removed"),
        ]);
        var b = Snapshot("prod", [Table("orders")],
        [
            Column("orders", "id", pk: false),
            Column("orders", "note", length: "200"),
            Column("orders", "status", nullable: "N", defaultValue: "'NEW'"),
            Column("orders", "added"),
        ]);

        var table = Assert.Single(SchemaComparer.Compare(a, b).ChangedTables);

        Assert.Equal(["removed"], table.ColumnsOnlyInA.Select(c => c.ColumnName));
        Assert.Equal(["added"], table.ColumnsOnlyInB.Select(c => c.ColumnName));
        var changes = table.ColumnChanges.ToDictionary(c => c.ColumnName, c => c.Changes);
        Assert.Equal(new FieldChange("Khoá chính", "Có", "Không"), Assert.Single(changes["id"]));
        Assert.Equal(new FieldChange("Độ dài", "100", "200"), Assert.Single(changes["note"]));
        Assert.Equal(
            [new FieldChange("Null", "Y", "N"), new FieldChange("Mặc định", "", "'NEW'")],
            changes["status"]);
        Assert.False(table.OnlyDescriptionChanges);
    }

    [Fact]
    public void Thu_tu_cot_khac_khong_tinh_la_thay_doi()
    {
        var a = Snapshot("dev", [Table("t")], [Column("t", "a") with { OrdinalPosition = 1 }, Column("t", "b") with { OrdinalPosition = 2 }]);
        var b = Snapshot("prod", [Table("t")], [Column("t", "b") with { OrdinalPosition = 1 }, Column("t", "a") with { OrdinalPosition = 2 }]);

        Assert.True(SchemaComparer.Compare(a, b).IsIdentical);
    }

    [Fact]
    public void Chi_khac_mo_ta_thi_OnlyDescriptionChanges()
    {
        var a = Snapshot("dev", [Table("t", "Đơn hàng")], [Column("t", "a", description: "mã")]);
        var b = Snapshot("prod", [Table("t", "Đơn hàng (cũ)")], [Column("t", "a", description: "")]);

        var table = Assert.Single(SchemaComparer.Compare(a, b).ChangedTables);

        Assert.True(table.OnlyDescriptionChanges);
        Assert.Equal(SchemaComparer.DescriptionField, Assert.Single(table.TableChanges).Field);
    }

    [Fact]
    public void Index_va_rang_buoc_so_theo_noi_dung_khong_theo_ten()
    {
        // Tên do hệ thống sinh khác nhau ở mỗi môi trường nhưng định nghĩa giống → không báo khác.
        var a = Snapshot("dev", [Table("t")], [],
            indexes: [new() { TableName = "t", IndexName = "SYS_C001", Columns = "ID", IsPrimaryKey = true, IsUnique = true }],
            constraints: [new() { TableName = "t", ConstraintName = "SYS_C002", ConstraintType = "CHECK", Definition = "PRICE > 0" }]);
        var b = Snapshot("prod", [Table("t")], [],
            indexes: [new() { TableName = "t", IndexName = "SYS_C987", Columns = "ID", IsPrimaryKey = true, IsUnique = true }],
            constraints: [new() { TableName = "t", ConstraintName = "SYS_C988", ConstraintType = "CHECK", Definition = "PRICE > 0" }]);

        Assert.True(SchemaComparer.Compare(a, b).IsIdentical);
    }

    [Fact]
    public void Index_khoa_ngoai_rang_buoc_them_bot()
    {
        var a = Snapshot("dev", [Table("t")], [],
            foreignKeys: [new() { TableName = "t", LocalColumns = "CUST_ID", ReferencedTable = "customers", ReferencedColumns = "ID" }],
            indexes: [new() { TableName = "t", IndexName = "ix_a", Columns = "NAME" }],
            constraints: [new() { TableName = "t", ConstraintName = "uq", ConstraintType = "UNIQUE", Definition = "CODE" }]);
        var b = Snapshot("prod", [Table("t")], [],
            indexes: [new() { TableName = "t", IndexName = "ix_a", Columns = "NAME", IsUnique = true }]);

        var table = Assert.Single(SchemaComparer.Compare(a, b).ChangedTables);

        Assert.Equal(["(CUST_ID) → customers(ID)"], table.ForeignKeysOnlyInA);
        Assert.Empty(table.ForeignKeysOnlyInB);
        Assert.Equal(["Thường (NAME)"], table.IndexesOnlyInA);
        Assert.Equal(["UNIQUE (NAME)"], table.IndexesOnlyInB);
        Assert.Equal(["UNIQUE CODE"], table.ConstraintsOnlyInA);
    }

    [Fact]
    public void File_ban_cu_khong_co_index_thi_bo_qua_so_index()
    {
        var a = Snapshot("old", [Table("t")], [], extended: false);
        var b = Snapshot("new", [Table("t")], [],
            indexes: [new() { TableName = "t", IndexName = "ix", Columns = "A" }]);

        var diff = SchemaComparer.Compare(a, b);

        Assert.False(diff.IndexesCompared);
        Assert.True(diff.IsIdentical);
    }

    [Fact]
    public void Trang_HTML_co_tom_tat_va_ma_hoa_ky_tu()
    {
        var a = Snapshot("dev", [Table("orders"), Table("<script>")], [Column("orders", "id", type: "INT", length: "")]);
        var b = Snapshot("prod", [Table("orders")], [Column("orders", "id", type: "BIGINT", length: "")]);

        var html = SchemaCompareReport.Build(SchemaComparer.Compare(a, b), new DateTime(2026, 10, 21, 9, 0, 0));

        Assert.Contains("<b>1</b> bảng chỉ có ở A", html);
        Assert.Contains("<b>1</b> bảng có thay đổi", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<td class=\"code\"><script>", html);
        Assert.Contains("BIGINT", html);
        Assert.Contains("2026-10-21 09:00:00", html);
    }

    [Fact]
    public void Trang_HTML_khi_giong_nhau()
    {
        var a = Snapshot("dev", [Table("t")], []);

        var html = SchemaCompareReport.Build(SchemaComparer.Compare(a, a with { Name = "prod" }), DateTime.Now);

        Assert.Contains("Hai schema giống nhau", html);
        Assert.DoesNotContain("id=\"changed\"", html);
    }
}
