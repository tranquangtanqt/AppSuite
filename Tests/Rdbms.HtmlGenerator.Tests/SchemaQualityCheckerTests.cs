using Rdbms.HtmlGenerator.Models;
using Rdbms.HtmlGenerator.Services;

namespace Rdbms.HtmlGenerator.Tests;

public class SchemaQualityCheckerTests
{
    private static DbTableRecord Table(string name, string kind = "BASE TABLE", string description = "mô tả", long? rows = 10) =>
        new() { TableName = name, Kind = kind, Description = description, EstimatedRows = rows, SourceFile = "db", SourceSheet = "public" };

    private static DbColumnRecord Column(string table, string name, string type = "integer", bool pk = false, string description = "mô tả", int pos = 1) =>
        new() { TableName = table, ColumnName = name, DataType = type, Level = pk ? 0 : 1, Description = description, OrdinalPosition = pos, Nullable = pk ? "N" : "Y" };

    private static DbIndexRecord Index(string table, string name, string columns, bool unique = false, bool pk = false, string note = "") =>
        new() { TableName = table, IndexName = name, Columns = columns, IsUnique = unique, IsPrimaryKey = pk, Note = note };

    private static DbForeignKeyRecord ForeignKey(string table, string columns, string referencedTable, string referencedColumns) =>
        new() { TableName = table, LocalColumns = columns, ReferencedTable = referencedTable, ReferencedColumns = referencedColumns };

    private static SchemaSnapshot Snapshot(List<DbTableRecord> tables, List<DbColumnRecord> columns,
        List<DbForeignKeyRecord>? foreignKeys = null, List<DbIndexRecord>? indexes = null, bool extended = true) =>
        new("shop", tables, columns, foreignKeys ?? [], indexes ?? [], [], extended);

    private static List<QualityFinding> Of(QualityReport report, QualityRule rule) => report.Findings.Where(f => f.Rule == rule).ToList();

    /// <summary>Schema "sạch": customers / orders có khoá chính, khoá ngoại cùng kiểu và có index, đủ mô tả, đủ thống kê.</summary>
    private static (List<DbTableRecord>, List<DbColumnRecord>, List<DbForeignKeyRecord>, List<DbIndexRecord>) Clean() => (
        [Table("customers"), Table("orders")],
        [Column("customers", "id", pk: true), Column("customers", "name", "varchar(50)", pos: 2),
         Column("orders", "id", pk: true), Column("orders", "customer_id", pos: 2)],
        [ForeignKey("orders", "customer_id", "customers", "id")],
        [Index("customers", "customers_pkey", "id", unique: true, pk: true), Index("orders", "orders_pkey", "id", unique: true, pk: true),
         Index("orders", "ix_orders_customer_id", "customer_id")]);

    [Fact]
    public void Schema_sach_thi_khong_co_phat_hien()
    {
        var (tables, columns, fks, indexes) = Clean();

        var report = SchemaQualityChecker.Check(Snapshot(tables, columns, fks, indexes));

        Assert.Empty(report.Findings);
        Assert.Equal(2, report.TableCount);
        Assert.Equal(0, report.ViewCount);
    }

    [Fact]
    public void Bang_khong_co_khoa_chinh_view_thi_khong_bao()
    {
        var report = SchemaQualityChecker.Check(Snapshot(
            [Table("log_import"), Table("v_orders", kind: "VIEW", rows: null)],
            [Column("log_import", "msg", "text"), Column("v_orders", "id")]));

        var finding = Assert.Single(Of(report, SchemaQualityChecker.NoPrimaryKey));
        Assert.Equal("log_import", finding.TableName);
        Assert.Equal(QualitySeverity.Error, finding.Rule.Severity);
        Assert.Empty(Of(report, SchemaQualityChecker.NoStatistics)); // view không có thống kê là bình thường
    }

    [Fact]
    public void Khoa_ngoai_khac_kieu()
    {
        var (tables, columns, fks, indexes) = Clean();
        columns[3] = Column("orders", "customer_id", "bigint", pos: 2);

        var finding = Assert.Single(Of(SchemaQualityChecker.Check(Snapshot(tables, columns, fks, indexes)), SchemaQualityChecker.ForeignKeyTypeMismatch));

        Assert.Contains("customer_id bigint ≠ id integer", finding.Message);
    }

    [Fact]
    public void Khoa_ngoai_khong_co_index_kem_SQL_goi_y()
    {
        var (tables, columns, fks, indexes) = Clean();
        indexes.RemoveAt(2);

        var finding = Assert.Single(Of(SchemaQualityChecker.Check(Snapshot(tables, columns, fks, indexes)), SchemaQualityChecker.ForeignKeyWithoutIndex));

        Assert.Equal("CREATE INDEX ix_orders_customer_id ON orders (customer_id);", finding.Sql);
    }

    [Fact]
    public void Khoa_ngoai_nhieu_cot_can_index_bat_dau_dung_thu_tu()
    {
        var tables = new List<DbTableRecord> { Table("lines") };
        var columns = new List<DbColumnRecord> { Column("lines", "id", pk: true), Column("lines", "a"), Column("lines", "b") };
        var fks = new List<DbForeignKeyRecord> { ForeignKey("lines", "a,b", "other", "x,y") };

        // (b, a) không dùng được cho khoá ngoại (a, b); (a, b, id) thì được; khoá chính cũng tính.
        Assert.Single(Of(SchemaQualityChecker.Check(Snapshot(tables, columns, fks, [Index("lines", "ix_ba", "b, a")])), SchemaQualityChecker.ForeignKeyWithoutIndex));
        Assert.Empty(Of(SchemaQualityChecker.Check(Snapshot(tables, columns, fks, [Index("lines", "ix_abid", "A, B DESC, id")])), SchemaQualityChecker.ForeignKeyWithoutIndex));
        Assert.Empty(Of(SchemaQualityChecker.Check(Snapshot(tables, columns, fks, [Index("lines", "pk", "a, b", unique: true, pk: true)])), SchemaQualityChecker.ForeignKeyWithoutIndex));
    }

    [Fact]
    public void Index_bi_bao_trum_hoac_trung()
    {
        var tables = new List<DbTableRecord> { Table("t") };
        var columns = new List<DbColumnRecord> { Column("t", "id", pk: true), Column("t", "a"), Column("t", "b") };
        var indexes = new List<DbIndexRecord>
        {
            Index("t", "t_pkey", "id", unique: true, pk: true),
            Index("t", "ix_a", "a"),        // bị ix_a_b bao trùm
            Index("t", "ix_a_b", "a, b"),
            Index("t", "ix_b1", "b"),       // trùng ix_b2 → chỉ báo 1 trong 2
            Index("t", "ix_b2", "b"),
            Index("t", "ux_a", "a", unique: true),               // UNIQUE: giữ ràng buộc, không báo
            Index("t", "ix_a_part", "a", note: "WHERE a > 0"),   // partial: phạm vi khác, không báo
            Index("t", "ix_lower", "lower(a)"),                  // biểu thức: không so
        };

        var findings = Of(SchemaQualityChecker.Check(Snapshot(tables, columns, indexes: indexes)), SchemaQualityChecker.RedundantIndex);

        Assert.Equal(["ix_a", "ix_b1"], findings.Select(f => f.Sql!["DROP INDEX ".Length..^1]).Order());
        Assert.Contains("ix_a (a) bị bao trùm bởi ix_a_b (a, b)", findings.Single(f => f.Sql == "DROP INDEX ix_a;").Message);
    }

    [Fact]
    public void Index_thuong_trung_cot_khoa_chinh_bi_bao()
    {
        var indexes = new List<DbIndexRecord> { Index("t", "t_pkey", "id", unique: true, pk: true), Index("t", "ix_id", "id") };

        var finding = Assert.Single(Of(SchemaQualityChecker.Check(Snapshot([Table("t")], [Column("t", "id", pk: true)], indexes: indexes)),
            SchemaQualityChecker.RedundantIndex));

        Assert.Equal("DROP INDEX ix_id;", finding.Sql);
    }

    [Fact]
    public void Chua_co_thong_ke_va_thieu_mo_ta()
    {
        var report = SchemaQualityChecker.Check(Snapshot(
            [Table("t", description: "", rows: null)],
            [Column("t", "id", pk: true), Column("t", "a", description: ""), Column("t", "b", description: "  ")]));

        Assert.Single(Of(report, SchemaQualityChecker.NoStatistics));
        Assert.Single(Of(report, SchemaQualityChecker.NoTableDescription));
        Assert.Contains("2 / 3 cột thiếu mô tả - a, b", Assert.Single(Of(report, SchemaQualityChecker.NoColumnDescription)).Message);
        Assert.Equal(1, report.Count(QualitySeverity.Warning));
        Assert.Equal(2, report.Count(QualitySeverity.Info));
    }

    [Fact]
    public void Db_ban_cu_bo_qua_quy_tac_can_index_va_thong_ke()
    {
        var (tables, columns, fks, _) = Clean();
        tables[0] = Table("customers", rows: null);

        var report = SchemaQualityChecker.Check(Snapshot(tables, columns, fks, indexes: [], extended: false));

        Assert.False(report.IndexesChecked);
        Assert.Empty(report.Findings);
        Assert.Contains("bỏ qua", SchemaQualityReport.Build(report, DateTime.Now, DateTime.Now, null));
    }

    [Fact]
    public void Ten_khac_quy_uoc_da_so()
    {
        var report = SchemaQualityChecker.Check(Snapshot(
            [Table("order_items"), Table("customer_notes"), Table("productList"), Table("ORDER_LOG"), Table("Order Detail"), Table("users")],
            [Column("order_items", "id", pk: true), Column("order_items", "item_name"), Column("order_items", "order_id"), Column("order_items", "unitPrice"),
             Column("order_items", "Created_At")]));

        var naming = Of(report, SchemaQualityChecker.NamingStyle);
        Assert.Contains(naming, f => f.TableName == "productList" && f.Message.Contains("camelCase"));
        Assert.Contains(naming, f => f.TableName == "Order Detail" && f.Message.Contains("ký tự đặc biệt"));
        Assert.DoesNotContain(naming, f => f.TableName is "ORDER_LOG" or "users"); // chữ HOA + _ / 1 từ đều hợp snake_case
        var columnsFinding = Assert.Single(naming, f => f.TableName == "order_items");
        Assert.Contains("cột unitPrice dạng camelCase", columnsFinding.Message);
        Assert.Contains("cột Created_At lẫn chữ hoa / thường", columnsFinding.Message);
    }

    [Fact]
    public void Trang_ket_qua_link_tu_dien_va_escape()
    {
        var report = SchemaQualityChecker.Check(Snapshot([Table("a<b>", description: "")], [Column("a<b>", "x")]));

        var html = SchemaQualityReport.Build(report, DateTime.Now, DateTime.Now, "shop (prod).html");

        Assert.Contains("href=\"shop%20%28prod%29.html#table=a%3Cb%3E\"", html);
        Assert.DoesNotContain("<b>a<b></b>", html);
        Assert.Contains("✓ không có", html); // quy tắc không vi phạm
        Assert.Contains("Nên sửa", html);
    }
}
