using Mcf.DbDef.HtmlGenerator.Services;
using OfficeOpenXml;

namespace Mcf.DbDef.Tests;

/// <summary>Thư mục tạm riêng cho mỗi test, tự xoá khi xong.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Mcf.DbDef.Tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Dựng workbook DBDef theo bố cục tài liệu mcframe: sheet index "テーブル・ビュー一覧" trỏ tới các sheet
/// bảng; mỗi sheet có 1 hoặc nhiều khối "テーブル名称" (tên bảng ở dòng dưới, cột kế bên) → phần mô tả 【...】 →
/// dòng tiêu đề cột (レベル / 項目名 / ...) → các cột, FOREIGN, tham chiếu nhóm cột dùng chung "$...$".</summary>
internal static class Workbooks
{
    public static void Write(ExcelWorksheet s, int row, params string[] cells)
    {
        for (int c = 0; c < cells.Length; c++)
        {
            if (cells[c].Length > 0)
            {
                s.Cells[row, c + 1].Value = cells[c];
            }
        }
    }

    private static readonly string[] ColumnHeader = ["レベル", "項目名", "メタ", "型", "桁", "Null", "Def.", "日本語", "説明", "フルネーム", "値の制限"];

    public static void Index(ExcelPackage p, params (string Sheet, string Table, string Japanese, string Kind, string Note)[] entries)
    {
        var s = p.Workbook.Worksheets.Add("テーブル・ビュー一覧");
        Write(s, 1, "テーブル・ビュー一覧");
        Write(s, 3, "シート名", "テーブル・ビュー名", "日本語名", "種別", "備考");
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            Write(s, 4 + i, e.Sheet, e.Table, e.Japanese, e.Kind, e.Note);
        }
    }

    /// <summary>Sheet "受注": 2 khối bảng. TSB_ORDER đủ mọi phần (mô tả, cột, nhóm dùng chung, INDEX, FOREIGN).</summary>
    public static void OrderSheet(ExcelPackage p)
    {
        var s = p.Workbook.Worksheets.Add("受注");
        Write(s, 1, "テーブル名称", "", "別名", "日本語名");
        Write(s, 2, "", "TSB_ORDER", "ORD", "");               // 日本語名 trống → lấy từ index
        Write(s, 3, "", "【説明】");
        Write(s, 4, "", "受注の見出し");
        Write(s, 5, "", "2行目");
        Write(s, 6, "", "【管理タイプ】");
        Write(s, 7, "", "トランザクション");
        Write(s, 8, "", "【運用後の変更に注意が必要な項目】");
        Write(s, 9, "", "項目名", "", "変更不可理由");       // "項目名" không kèm "レベル" → không phải tiêu đề cột
        Write(s, 10, "", "ORDER_NO", "", "キー");
        Write(s, 11, "", "CUST_CD");
        Write(s, 13, "", "【改廃】");
        Write(s, 14, "", "2024 新規");
        Write(s, 16, ColumnHeader);
        Write(s, 17, "1", "ORDER_NO", "KEY", "VARCHAR2", "20", "N", "", "受注番号", "", "", "");
        Write(s, 18, "1", "CUST_CD", "", "VARCHAR2", "10", "Y", "' '", "テーブル名", "得意先", "", "");  // "テーブル名" ở cột khác cột A: không mở khối mới
        Write(s, 20, "88", "$EXCTRL_COLS$");
        Write(s, 21, "INDEX", "PK_ORDER");
        Write(s, 22, "FOREIGN", "", "CUST_CD", "", "", "MAM_CUST", "", "CUST_CD");
        Write(s, 23, "FOREIGN", "", "ITEM_CD", "", "MAM_ITEM");  // không có cột tham chiếu

        Write(s, 25, "テーブル名", "", "別名", "日本語名");
        Write(s, 26, "", "TSB_ORDER_DTL", "", "受注明細");
        Write(s, 27, ColumnHeader);
        Write(s, 28, "1", "ORDER_NO");
        Write(s, 29, "1", "LINE_NO");

        Write(s, 31, "テーブル名称");
        Write(s, 32, "", "$LOCAL_GROUP$");                    // nhóm dùng chung, không phải bảng thật
        Write(s, 33, ColumnHeader);
        Write(s, 34, "1", "X");
    }

    /// <summary>Sheet định nghĩa nhóm cột dùng chung "$EXCTRL_COLS$" (排他制御用カラム).</summary>
    public static void ControlSheet(ExcelPackage p)
    {
        var s = p.Workbook.Worksheets.Add("制御用");
        Write(s, 1, "テーブル名称");
        Write(s, 2, "", "$EXCTRL_COLS$");
        Write(s, 3, ColumnHeader);
        Write(s, 4, "1", "UPD_DATE", "", "DATE", "", "N", "", "更新日時");
        Write(s, 5, "1", "UPD_USER", "", "VARCHAR2", "20", "N", "", "更新者");
    }

    public static string Save(string dir, string fileName, Action<ExcelPackage> build)
    {
        using var p = new ExcelPackage();
        build(p);
        var path = System.IO.Path.Combine(dir, fileName);
        p.SaveAs(new FileInfo(path));
        return path;
    }

    public static void Standard(ExcelPackage p)
    {
        Index(p,
            ("受注", "TSB_ORDER", "受注", "TABLE", "見出し"),
            ("受注", "TSB_ORDER_DTL", "受注明細(index)", "TABLE", ""),
            ("受注", "TSB_GHOST", "幽霊", "VIEW", ""),
            ("存在しない", "TSB_NONE", "", "", ""));
        OrderSheet(p);
        ControlSheet(p);
    }
}

public class ImporterTests
{
    private static (List<HtmlGenerator.Models.DbTableRecord> Tables, List<HtmlGenerator.Models.DbColumnRecord> Columns,
        List<HtmlGenerator.Models.DbForeignKeyRecord> ForeignKeys, List<string> Log) ImportStandard()
    {
        using var temp = new TempDir();
        Workbooks.Save(temp.Path, "DBDef_受注.xlsm", Workbooks.Standard);
        var log = new List<string>();
        var (t, c, f) = new ExcelDbDefImporter().ImportDirectory(temp.Path, log.Add);
        return (t, c, f, log);
    }

    [Fact]
    public void Reads_tables_listed_in_the_index_sheet()
    {
        var (tables, _, _, _) = ImportStandard();

        Assert.Equal(["TSB_ORDER", "TSB_ORDER_DTL"], tables.Select(t => t.TableName));
        var order = tables[0];
        Assert.Equal("ORD", order.Alias);
        Assert.Equal("受注", order.JapaneseName);        // từ index vì khối để trống
        Assert.Equal("TABLE", order.Kind);
        Assert.Equal("見出し", order.Note);
        Assert.Equal("DBDef_受注.xlsm", order.SourceFile);
        Assert.Equal("受注", order.SourceSheet);
        Assert.Equal("受注明細", tables[1].JapaneseName); // khối có tên riêng → ưu tiên hơn index
    }

    [Fact]
    public void Reads_preamble_sections()
    {
        var order = ImportStandard().Tables[0];

        Assert.Equal("受注の見出し\n2行目", order.Description);
        Assert.Equal("トランザクション", order.ManagementType);
        Assert.Equal("ORDER_NO: キー\nCUST_CD", order.CautionItems);
        Assert.Equal("2024 新規", order.RevisionHistory);
    }

    [Fact]
    public void Reads_columns_and_splices_shared_column_groups()
    {
        var columns = ImportStandard().Columns.Where(c => c.TableName == "TSB_ORDER").ToList();

        Assert.Equal(["ORDER_NO", "CUST_CD", "UPD_DATE", "UPD_USER"], columns.Select(c => c.ColumnName));
        Assert.Equal([0, 1, 2, 3], columns.Select(c => c.OrdinalPosition));
        var orderNo = columns[0];
        Assert.Equal((1, "KEY", "VARCHAR2", "20", "N", "受注番号", false),
            (orderNo.Level, orderNo.Meta, orderNo.DataType, orderNo.Length, orderNo.Nullable, orderNo.JapaneseName, orderNo.IsCommon));
        Assert.Equal("' '", columns[1].DefaultValue);
        Assert.All(columns.Skip(2), c =>
        {
            Assert.True(c.IsCommon);
            Assert.Equal("$EXCTRL_COLS$", c.GroupName);
        });
        Assert.Equal("更新日時", columns[2].JapaneseName);
    }

    [Fact]
    public void Text_テーブル名_outside_the_first_column_does_not_start_a_new_block()
    {
        var columns = ImportStandard().Columns;

        Assert.Equal(["ORDER_NO", "LINE_NO"], columns.Where(c => c.TableName == "TSB_ORDER_DTL").Select(c => c.ColumnName));
    }

    [Fact]
    public void Reads_foreign_keys_by_content_not_fixed_columns()
    {
        var fks = ImportStandard().ForeignKeys;

        Assert.Equal(2, fks.Count);
        Assert.Equal(("TSB_ORDER", 0, "CUST_CD", "MAM_CUST", "CUST_CD"),
            (fks[0].TableName, fks[0].OrdinalPosition, fks[0].LocalColumns, fks[0].ReferencedTable, fks[0].ReferencedColumns));
        Assert.Equal(("ITEM_CD", "MAM_ITEM", ""), (fks[1].LocalColumns, fks[1].ReferencedTable, fks[1].ReferencedColumns));
    }

    [Fact]
    public void Group_placeholder_blocks_are_not_tables()
    {
        Assert.DoesNotContain(ImportStandard().Tables, t => t.TableName.StartsWith('$'));
    }

    [Fact]
    public void Logs_index_entries_that_have_no_block_or_sheet()
    {
        var log = ImportStandard().Log;

        Assert.Contains(log, l => l.Contains("Khong tim thay khoi du lieu cho bang 'TSB_GHOST'"));
        Assert.Contains(log, l => l.Contains("Sheet '存在しない' duoc tham chieu tu index nhung khong ton tai."));
        // Quét mọi sheet: cả $EXCTRL_COLS$ (制御用) lẫn $LOCAL_GROUP$ (受注).
        Assert.Contains(log, l => l.Contains("Da nap 2 nhom cot dung chung"));
    }

    [Fact]
    public void Duplicate_table_across_files_keeps_the_first()
    {
        using var temp = new TempDir();
        Workbooks.Save(temp.Path, "A.xlsm", Workbooks.Standard);
        Workbooks.Save(temp.Path, "B.xlsm", p =>
        {
            Workbooks.Index(p, ("受注", "TSB_ORDER", "", "", ""));
            var s = p.Workbook.Worksheets.Add("受注");
            Workbooks.Write(s, 1, "テーブル名称");
            Workbooks.Write(s, 2, "", "TSB_ORDER");
            Workbooks.Write(s, 3, "レベル", "項目名");
            Workbooks.Write(s, 4, "1", "OTHER_COL");
        });
        var log = new List<string>();

        var (tables, columns, _) = new ExcelDbDefImporter().ImportDirectory(temp.Path, log.Add);

        Assert.Equal("A.xlsm", Assert.Single(tables, t => t.TableName == "TSB_ORDER").SourceFile);
        Assert.DoesNotContain(columns, c => c.ColumnName == "OTHER_COL");
        Assert.Contains("[B.xlsm] Bo qua bang trung ten 'TSB_ORDER' (da co tu file khac)", log);
    }

    [Fact]
    public void File_without_index_sheet_or_broken_file_is_logged()
    {
        using var temp = new TempDir();
        Workbooks.Save(temp.Path, "A.xlsm", p => p.Workbook.Worksheets.Add("何か").Cells[1, 1].Value = "x");
        File.WriteAllText(Path.Combine(temp.Path, "B.xlsm"), "not a zip");
        var log = new List<string>();

        var (tables, _, _) = new ExcelDbDefImporter().ImportDirectory(temp.Path, log.Add);

        Assert.Empty(tables);
        Assert.Contains("[A.xlsm] Khong tim thay sheet 'テーブル・ビュー一覧'.", log);
        Assert.Contains(log, l => l.StartsWith("[B.xlsm] LOI:"));
    }

    [Fact]
    public void Empty_folder_is_logged()
    {
        using var temp = new TempDir();
        var log = new List<string>();

        var (tables, _, _) = new ExcelDbDefImporter().ImportDirectory(temp.Path, log.Add);

        Assert.Empty(tables);
        Assert.StartsWith("Khong tim thay file .xlsm nao", Assert.Single(log));
    }
}

public class DatabaseTests
{
    [Fact]
    public async Task Round_trip_through_sqlite_and_html_report()
    {
        using var temp = new TempDir();
        Workbooks.Save(temp.Path, "DBDef.xlsm", Workbooks.Standard);
        var (tables, columns, fks) = new ExcelDbDefImporter().ImportDirectory(temp.Path, _ => { });
        var db = new McfDbDefHtmlGeneratorDatabase();

        await db.ReplaceAllAsync(tables, columns, fks);
        await db.ReplaceAllAsync(tables, columns, fks); // ghi lại lần 2: thay thế, không trùng khoá

        Assert.Equal(tables.Select(t => t.TableName).Order(), db.GetAllTables().Select(t => t.TableName).Order());
        var readColumns = db.GetAllColumns().Where(c => c.TableName == "TSB_ORDER").OrderBy(c => c.OrdinalPosition).ToList();
        Assert.Equal(["ORDER_NO", "CUST_CD", "UPD_DATE", "UPD_USER"], readColumns.Select(c => c.ColumnName));
        Assert.True(readColumns[2].IsCommon);
        Assert.Equal("$EXCTRL_COLS$", readColumns[2].GroupName);
        Assert.Equal(1, readColumns[0].Level);
        Assert.Equal(2, db.GetAllForeignKeys().Count);
        var order = db.GetAllTables().Single(t => t.TableName == "TSB_ORDER");
        Assert.Equal("ORDER_NO: キー\nCUST_CD", order.CautionItems);

        string htmlPath = new HtmlReportGenerator().Generate(db);
        string html = File.ReadAllText(htmlPath);
        Assert.Contains("\"name\":\"TSB_ORDER\"", html);
        Assert.Contains("\"japaneseName\":\"受注明細\"", html);
    }
}
