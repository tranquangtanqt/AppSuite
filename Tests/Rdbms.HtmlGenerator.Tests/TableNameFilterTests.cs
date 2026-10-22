using Rdbms.HtmlGenerator.Models;
using Rdbms.HtmlGenerator.Services;

namespace Rdbms.HtmlGenerator.Tests;

public class TableNameFilterTests
{
    private static DbTableRecord Table(string name, string schema = "public") =>
        new() { TableName = name, SourceFile = "db", SourceSheet = schema };

    private static SchemaImportResult Import(params DbTableRecord[] tables) => new(
        tables.ToList(),
        tables.Select(t => new DbColumnRecord { TableName = t.TableName, ColumnName = "id", Schema = t.SourceSheet }).ToList(),
        tables.Select(t => new DbForeignKeyRecord { TableName = t.TableName, LocalColumns = "id", ReferencedTable = "orders", Schema = t.SourceSheet }).ToList(),
        tables.Select(t => new DbIndexRecord { TableName = t.TableName, IndexName = "ix_" + t.TableName, Columns = "id", Schema = t.SourceSheet }).ToList(),
        tables.Select(t => new DbConstraintRecord { TableName = t.TableName, ConstraintName = "ck_" + t.TableName, ConstraintType = "CHECK", Schema = t.SourceSheet }).ToList());

    private static List<string> Kept(SchemaImportResult result) => result.Tables.Select(t => t.TableName).ToList();

    [Fact]
    public void Tach_mau_theo_phay_cham_phay_xuong_dong()
    {
        Assert.Equal(["tmp_%", "%_bk", "audit.%"], TableNameFilter.ParsePatterns(" tmp_% , %_bk;\r\n\naudit.% , TMP_%"));
        Assert.Empty(TableNameFilter.ParsePatterns("   "));
        Assert.Empty(TableNameFilter.ParsePatterns(null));
    }

    [Fact]
    public void Khong_co_mau_thi_giu_nguyen()
    {
        var import = Import(Table("tmp_x"), Table("orders"));

        var (result, excluded) = TableNameFilter.Apply(import, []);

        Assert.Same(import, result);
        Assert.Empty(excluded);
    }

    [Fact]
    public void Bo_bang_tam_va_bang_sao_luu()
    {
        var import = Import(Table("orders"), Table("tmp_orders"), Table("TMP_LOG"), Table("orders_bk"), Table("customers_BK"), Table("bk_list"));

        var (result, excluded) = TableNameFilter.Apply(import, TableNameFilter.ParsePatterns("tmp_%, %_bk"));

        Assert.Equal(["orders", "bk_list"], Kept(result));
        Assert.Equal(["tmp_orders", "TMP_LOG", "orders_bk", "customers_BK"], excluded);
        // Cột / khoá ngoại / index / ràng buộc của bảng bị bỏ cũng bỏ theo.
        Assert.Equal(["orders", "bk_list"], result.Columns.Select(c => c.TableName));
        Assert.Equal(["orders", "bk_list"], result.ForeignKeys.Select(f => f.TableName));
        Assert.Equal(["orders", "bk_list"], result.Indexes.Select(i => i.TableName));
        Assert.Equal(["orders", "bk_list"], result.Constraints.Select(c => c.TableName));
    }

    [Fact]
    public void Dau_gach_duoi_la_chu_that_khong_phai_1_ky_tu()
    {
        var (result, _) = TableNameFilter.Apply(Import(Table("tmp_a"), Table("tmpl_a"), Table("tmpXa")), ["tmp_%"]);

        Assert.Equal(["tmpl_a", "tmpXa"], Kept(result)); // LIKE của SQL sẽ bỏ cả tmpXa
    }

    [Fact]
    public void Sao_va_dau_hoi()
    {
        var (result, _) = TableNameFilter.Apply(Import(Table("log1"), Table("log12"), Table("t_old"), Table("old")), ["log?", "*old"]);

        Assert.Equal(["log12"], Kept(result));
    }

    [Fact]
    public void Mau_co_dau_cham_so_voi_schema_bang()
    {
        var import = Import(Table("orders", "public"), Table("orders", "audit"), Table("logs", "audit"));

        var (result, excluded) = TableNameFilter.Apply(import, ["audit.%"]);

        var kept = Assert.Single(result.Tables);
        Assert.Equal(("public", "orders"), (kept.SourceSheet, kept.TableName));
        Assert.Equal(["orders", "logs"], excluded);
        // Chỉ cột của audit.orders bị bỏ, public.orders giữ.
        Assert.Equal(["public"], result.Columns.Select(c => c.Schema));
    }

    [Fact]
    public void Khong_khop_mau_nao_thi_giu_nguyen()
    {
        var import = Import(Table("orders"));

        var (result, excluded) = TableNameFilter.Apply(import, ["tmp_%"]);

        Assert.Same(import, result);
        Assert.Empty(excluded);
    }
}
