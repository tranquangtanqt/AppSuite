using OfficeOpenXml;
using Rdbms.HtmlGenerator.Models;
using Rdbms.HtmlGenerator.Services;

namespace Rdbms.HtmlGenerator.Tests;

/// <summary>Excel テーブル定義書: ghi ra file tạm rồi đọc lại bằng EPPlus.</summary>
public sealed class TableDefinitionWorkbookTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"RdbmsTableDef_{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static SchemaSnapshot Shop(bool extended = true) => new("shop",
        [
            new() { TableName = "v_orders", Kind = "VIEW", Description = "Đơn kèm tên khách", SourceFile = "127.0.0.1:5432/shop", SourceSheet = "public" },
            new() { TableName = "orders", Kind = "BASE TABLE", Description = "注文", EstimatedRows = 12345, SourceFile = "127.0.0.1:5432/shop", SourceSheet = "public" },
            new() { TableName = "customers", Kind = "BASE TABLE", Description = "顧客", EstimatedRows = 10, SourceFile = "127.0.0.1:5432/shop", SourceSheet = "public" },
        ],
        [
            new() { TableName = "orders", ColumnName = "customer_id", DataType = "integer", Nullable = "N", Level = 1, OrdinalPosition = 2, Description = "顧客ID" },
            new() { TableName = "orders", ColumnName = "id", DataType = "integer", Nullable = "N", Level = 0, OrdinalPosition = 1 },
            new() { TableName = "orders", ColumnName = "amount", DataType = "numeric(12,2)", Nullable = "Y", Level = 1, OrdinalPosition = 3, DefaultValue = "0" },
            new() { TableName = "customers", ColumnName = "id", DataType = "integer", Nullable = "N", Level = 0, OrdinalPosition = 1 },
            new() { TableName = "customers", ColumnName = "name", DataType = "character varying(50)", Nullable = "N", Level = 1, OrdinalPosition = 2 },
            new() { TableName = "v_orders", ColumnName = "id", DataType = "integer", Nullable = "Y", Level = 1, OrdinalPosition = 1 },
        ],
        [new() { TableName = "orders", LocalColumns = "customer_id", ReferencedTable = "customers", ReferencedColumns = "id" }],
        [new() { TableName = "orders", IndexName = "orders_pkey", Columns = "id", IsUnique = true, IsPrimaryKey = true, IndexType = "btree" }],
        [new() { TableName = "orders", ConstraintName = "ck_amount", ConstraintType = "CHECK", Definition = "CHECK (amount >= 0)" }],
        extended);

    private static int FindRow(ExcelWorksheet sheet, int column, string text)
    {
        for (var r = 1; r <= sheet.Dimension.End.Row; r++)
        {
            if (sheet.Cells[r, column].Text == text)
            {
                return r;
            }
        }
        return -1;
    }

    [Fact]
    public void Sheet_danh_sach_va_moi_bang_1_sheet()
    {
        var count = TableDefinitionWorkbook.Write(Shop(), "shop", new DateTime(2026, 10, 22, 10, 0, 0), _path);

        Assert.Equal(3, count);
        using var package = new ExcelPackage(new FileInfo(_path));
        var names = package.Workbook.Worksheets.Select(s => s.Name).ToList();
        Assert.Equal(["テーブル一覧", "customers", "orders", "v_orders"], names); // bảng trước, view sau

        var index = package.Workbook.Worksheets["テーブル一覧"];
        Assert.Equal("データベース：127.0.0.1:5432/shop　　出力日時：2026/10/22 10:00", index.Cells[2, 1].Text);
        Assert.Equal("テーブル：2　　ビュー：1", index.Cells[3, 1].Text);
        Assert.Equal("テーブル名", index.Cells[5, 3].Text);
        Assert.Equal("orders", index.Cells[7, 3].Text);
        Assert.Equal("'orders'!A1", index.Cells[7, 3].Hyperlink is ExcelHyperLink h ? h.ReferenceAddress : null);
        Assert.Equal("ビュー", index.Cells[8, 4].Text);
        Assert.Equal(12345L, Convert.ToInt64(index.Cells[7, 6].Value));
        Assert.Equal(3, Convert.ToInt32(index.Cells[7, 7].Value));
    }

    [Fact]
    public void Sheet_bang_co_cot_index_khoa_ngoai_rang_buoc()
    {
        TableDefinitionWorkbook.Write(Shop(), "shop", DateTime.Now, _path);

        using var package = new ExcelPackage(new FileInfo(_path));
        var sheet = package.Workbook.Worksheets["orders"];
        Assert.Equal("orders", sheet.Cells[FindRow(sheet, 1, "テーブル名"), 3].Text);
        Assert.Equal("12,345", sheet.Cells[FindRow(sheet, 1, "件数（概算）"), 3].Text);

        var header = FindRow(sheet, 1, "■ カラム定義") + 1;
        Assert.Equal("カラム名", sheet.Cells[header, 2].Text);
        // Theo OrdinalPosition: id (PK), customer_id, amount (kiểu + 桁数 tách riêng).
        Assert.Equal(["id", "customer_id", "amount"], [sheet.Cells[header + 1, 2].Text, sheet.Cells[header + 2, 2].Text, sheet.Cells[header + 3, 2].Text]);
        Assert.Equal("○", sheet.Cells[header + 1, 6].Text);
        Assert.Equal("○", sheet.Cells[header + 2, 5].Text); // NOT NULL
        Assert.Equal("numeric", sheet.Cells[header + 3, 3].Text);
        Assert.Equal("12,2", sheet.Cells[header + 3, 4].Text);
        Assert.Equal("0", sheet.Cells[header + 3, 7].Text);

        Assert.Equal("orders_pkey", sheet.Cells[FindRow(sheet, 1, "■ インデックス") + 2, 2].Text);
        var fk = FindRow(sheet, 1, "■ 外部キー") + 2;
        Assert.Equal("customers", sheet.Cells[fk, 3].Text);
        Assert.Equal("'customers'!A1", sheet.Cells[fk, 3].Hyperlink is ExcelHyperLink h ? h.ReferenceAddress : null);
        Assert.Equal("CHECK (amount >= 0)", sheet.Cells[FindRow(sheet, 1, "■ 制約（UNIQUE / CHECK）") + 2, 4].Text);

        var view = package.Workbook.Worksheets["v_orders"];
        Assert.Equal("（なし）", view.Cells[FindRow(view, 1, "■ 外部キー") + 2, 1].Text);
        Assert.Equal("－", view.Cells[FindRow(view, 1, "件数（概算）"), 3].Text);
    }

    [Fact]
    public void Db_ban_cu_khong_co_muc_index_va_rang_buoc()
    {
        TableDefinitionWorkbook.Write(Shop(extended: false), "shop", DateTime.Now, _path);

        using var package = new ExcelPackage(new FileInfo(_path));
        var sheet = package.Workbook.Worksheets["orders"];
        Assert.Equal(-1, FindRow(sheet, 1, "■ インデックス"));
        Assert.True(FindRow(sheet, 1, "■ 外部キー") > 0);
    }

    [Theory]
    [InlineData("character varying(100)", "character varying", "100")]
    [InlineData("numeric(12, 2)", "numeric", "12,2")]
    [InlineData("timestamp(3) without time zone", "timestamp without time zone", "3")]
    [InlineData("decimal(10,2) unsigned", "decimal unsigned", "10,2")]
    [InlineData("nvarchar(max)", "nvarchar", "max")]
    [InlineData("integer", "integer", "")]
    public void Tach_kieu_va_do_dai(string dataType, string type, string length)
    {
        var (t, l) = TableDefinitionWorkbook.SplitType(new DbColumnRecord { TableName = "t", ColumnName = "c", DataType = dataType });

        Assert.Equal(type, t);
        Assert.Equal(length, l);
    }

    [Fact]
    public void Ten_sheet_hop_le_va_khong_trung()
    {
        var names = TableDefinitionWorkbook.AssignSheetNames(
            ["sales.orders", "a/b:c", "Orders", "orders", "テーブル一覧", new string('x', 40), new string('x', 35)]);

        Assert.Equal("sales.orders", names["sales.orders"]);
        Assert.Equal("a_b_c", names["a/b:c"]);
        Assert.Equal("Orders", names["Orders"]);
        Assert.Equal("Orders", names["orders"]); // không phân biệt hoa thường (như mọi chỗ khác) → cùng 1 sheet
        Assert.Equal("テーブル一覧~2", names["テーブル一覧"]);
        Assert.Equal(new string('x', 31), names[new string('x', 40)]);
        Assert.Equal(new string('x', 29) + "~2", names[new string('x', 35)]);
        Assert.All(names.Values, n => Assert.True(n.Length <= 31));
    }
}
