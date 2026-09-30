using System.Text;
using System.Text.RegularExpressions;
using Mcf.CrudDiagram.HtmlGenerator.Services;
using OfficeOpenXml;
using static Mcf.CrudDiagram.Tests.CrudSheets;

namespace Mcf.CrudDiagram.Tests;

/// <summary>Đọc 1 sheet CRUD図: khối header, dòng tiêu đề bảng (tìm theo chữ, không cố định vị trí), các dòng dữ liệu.</summary>
public class ParserTests
{
    private static readonly string[]?[] SampleRows =
    [
        Block("Ins01", "登録"),
        Obj("MAM_BP", c: "○"),
        Obj("TSB_ORDER", r: "○", u: "○", kind: "T", remark: "受注"),
        null,
        Block("Upd02", "更新"),
        Obj("MAM_ITEM", d: "○"),
    ];

    [Fact]
    public void Reads_header_block_fields()
    {
        using var package = new ExcelPackage();
        var sheet = Add(package, "MSBBB0020", "受注登録", SampleRows);

        var model = CrudSheetParser.Parse(sheet, new StringBuilder());

        Assert.Equal("受注登録", model.ScreenName);
        Assert.Equal("MSB", model.ModuleId);
        Assert.Equal("販売管理", model.ModuleName);
        Assert.Equal("BB", model.SubModuleId);
        Assert.Equal("受注", model.SubModuleName);
        // 文書番号 / Version / Rev.: bên phải là nhãn khác → lấy ô ngay bên dưới.
        Assert.Equal("M7-DV-0221", model.DocNumber);
        Assert.Equal("1.0", model.Version);
        Assert.Equal("02", model.Revision);
    }

    [Fact]
    public void Reads_every_row_verbatim_including_spacers()
    {
        using var package = new ExcelPackage();
        var sheet = Add(package, "MSBBB0020", "受注登録", SampleRows);

        var model = CrudSheetParser.Parse(sheet, new StringBuilder());

        Assert.True(model.DataHeaderFound);
        Assert.Equal(SampleRows.Length, model.Rows.Count);
        Assert.Equal(2, model.BlockCount);
        var row = model.Rows[2];
        Assert.Equal(("TSB_ORDER", "", "○", "○", "", "T", "受注"), (row.UsedObject, row.Create, row.Read, row.Update, row.Delete, row.Kind, row.Remark));
        Assert.Equal("", model.Rows[3].Id + model.Rows[3].UsedObject);
    }

    [Fact]
    public void Data_header_is_found_by_text_even_when_shifted()
    {
        using var package = new ExcelPackage();
        var sheet = Add(package, "X", "X", SampleRows, headerRow: 8, firstCol: 2);

        var model = CrudSheetParser.Parse(sheet, new StringBuilder());

        Assert.True(model.DataHeaderFound);
        Assert.Equal("MAM_BP", model.Rows[1].UsedObject);
        Assert.Equal("○", model.Rows[1].Create);
    }

    [Fact]
    public void Missing_logic_name_falls_back_to_sheet_name()
    {
        using var package = new ExcelPackage();
        var sheet = Add(package, "MSBBB0030", "", SampleRows);

        Assert.Equal("MSBBB0030", CrudSheetParser.Parse(sheet, new StringBuilder()).ScreenName);
    }

    [Fact]
    public void Search_text_collects_header_values_ids_and_objects()
    {
        using var package = new ExcelPackage();
        var sheet = Add(package, "MSBBB0020", "受注登録", SampleRows);
        var search = new StringBuilder();

        CrudSheetParser.Parse(sheet, search);

        foreach (var word in new[] { "受注登録", "MSB", "M7-DV-0221", "Ins01", "登録", "MAM_BP", "TSB_ORDER", "MAM_ITEM" })
        {
            Assert.Contains(word, search.ToString());
        }
    }

    [Fact]
    public void Sheet_without_data_header_is_flagged()
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Other");
        sheet.Cells[1, 1].Value = "ロジック名";
        sheet.Cells[1, 2].Value = "何か";
        sheet.Cells[10, 3].Value = "自由記述";

        var model = CrudSheetParser.Parse(sheet, new StringBuilder());

        Assert.False(model.DataHeaderFound);
        Assert.Empty(model.Rows);
    }
}

/// <summary>HTML 1 logic: 1 bảng liên tục, bỏ dòng trống, neo cho từng khối ID, link sang khối của logic khác.</summary>
public class RendererTests
{
    private static string Render(IEnumerable<string[]?> rows, IReadOnlyDictionary<string, string>? index = null)
    {
        using var package = new ExcelPackage();
        var sheet = Add(package, "MSBBB0020", "受注登録", rows);
        var model = CrudSheetParser.Parse(sheet, new StringBuilder());
        return CrudHtmlRenderer.Render(model, sheet, index ?? new Dictionary<string, string>());
    }

    private static int BodyRowCount(string html) =>
        Regex.Matches(html[html.IndexOf("<tbody>", StringComparison.Ordinal)..], "<tr").Count;

    [Fact]
    public void Blank_rows_are_not_drawn()
    {
        string html = Render([Block("A1", "x"), Obj("T1", c: "○"), null, null, Block("A2", "y"), null, Obj("T2")]);

        Assert.Equal(4, BodyRowCount(html));
        Assert.DoesNotContain("<tr><td></td><td></td><td></td>", html);
    }

    [Fact]
    public void Block_rows_get_an_anchor()
    {
        string html = Render([Block("Ins01", "登録"), Obj("T1")]);

        Assert.Contains("<tr class=\"crud-block-row\" id=\"blk-Ins01\">", html);
    }

    [Fact]
    public void Reference_to_a_known_logic_becomes_a_link()
    {
        var index = new Dictionary<string, string> { ["MSBBL6020"] = "MSBBL6020.html" };

        string html = Render([Block("A1", "x"), Obj("MSBBL6020.Slo_Chk03", r: "○")], index);

        Assert.Contains("<a href=\"MSBBL6020.html#blk-Slo_Chk03\">MSBBL6020.Slo_Chk03</a>", html);
    }

    [Theory]
    [InlineData("MAUDL5010.pushEndLog")] // tiền tố không phải logic trong bộ tài liệu
    [InlineData("MAM_BP")]               // tên bảng thường
    [InlineData(".Slo")]
    [InlineData("MSBBL6020.")]
    public void Other_objects_stay_plain_text(string usedObject)
    {
        var index = new Dictionary<string, string> { ["MSBBL6020"] = "MSBBL6020.html" };

        string html = Render([Block("A1", "x"), Obj(usedObject)], index);

        Assert.DoesNotContain("<a ", html);
        Assert.Contains($"<td>{usedObject}</td>", html);
    }

    [Fact]
    public void Cell_text_is_html_escaped()
    {
        string html = Render([Block("A1", "a<b>&\"c\""), Obj("T1")]);

        Assert.Contains("a&lt;b&gt;&amp;&quot;c&quot;", html);
    }

    [Fact]
    public void Unexpected_layout_falls_back_to_raw_grid()
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Other");
        sheet.Cells[2, 2].Value = "自由記述";
        var model = CrudSheetParser.Parse(sheet, new StringBuilder());

        string html = CrudHtmlRenderer.Render(model, sheet, new Dictionary<string, string>());

        Assert.Contains("class=\"sheet-grid\"", html);
        Assert.Contains("自由記述", html);
    }
}

/// <summary>Đọc cả thư mục: bỏ sheet hành chính / file khoá, trùng tên sheet, link chéo giữa các file, lỗi 1 file không dừng cả lô.</summary>
public class ImporterTests
{
    private static void SaveWorkbook(string path, Action<ExcelPackage> build)
    {
        using var package = new ExcelPackage();
        build(package);
        package.SaveAs(new FileInfo(path));
    }

    [Fact]
    public void Imports_one_record_per_logic_sheet_and_writes_html()
    {
        using var temp = new TempDir();
        var src = temp.Sub("src");
        var outDir = temp.Sub("out");
        SaveWorkbook(Path.Combine(src, "a.xlsx"), p =>
        {
            p.Workbook.Worksheets.Add("表紙").Cells[1, 1].Value = "mcframe 7";
            p.Workbook.Worksheets.Add("変更来歴").Cells[1, 1].Value = "Rev";
            Add(p, "MSBBB0020", "受注登録", [Block("Ins01", "登録"), Obj("MAM_BP", c: "○"), Obj("MSBBB0030.Upd01", r: "○")]);
            Add(p, "MSBBB0030", "受注変更", [Block("Upd01", "更新"), Obj("MAM_BP", u: "○")]);
        });
        var log = new List<string>();

        var records = new CrudDocImporter().ImportDirectory(src, outDir, log.Add);

        Assert.Equal(["MSBBB0020", "MSBBB0030"], records.Select(r => r.ScreenCode));
        var first = records[0];
        Assert.Equal("受注登録", first.ScreenName);
        Assert.Equal("MSBBB0020.html", first.HtmlFileName);
        Assert.Equal(1, first.BlockCount);
        Assert.Contains("MAM_BP", first.SearchText);

        string html = File.ReadAllText(Path.Combine(outDir, "MSBBB0020.html"));
        Assert.Contains("<title>MSBBB0020 - 受注登録</title>", html);
        Assert.Contains("<a href=\"MSBBB0030.html#blk-Upd01\">", html);
        Assert.Contains(log, l => l.StartsWith("Xong: 2 sheet"));
    }

    [Fact]
    public void Links_work_across_workbooks()
    {
        using var temp = new TempDir();
        var src = temp.Sub("src");
        var outDir = temp.Sub("out");
        SaveWorkbook(Path.Combine(src, "1.xlsx"), p => Add(p, "MSBBL6020", "x", [Block("A", "a"), Obj("MSBBL7000.Chk01")]));
        SaveWorkbook(Path.Combine(src, "2.xlsx"), p => Add(p, "MSBBL7000", "y", [Block("Chk01", "b"), Obj("T")]));

        new CrudDocImporter().ImportDirectory(src, outDir, _ => { });

        // File 1 được render trước khi đọc file 2 nhưng vẫn biết tên HTML của MSBBL7000 (lượt đọc trước).
        Assert.Contains("<a href=\"MSBBL7000.html#blk-Chk01\">", File.ReadAllText(Path.Combine(outDir, "MSBBL6020.html")));
    }

    [Fact]
    public void Duplicate_sheet_names_get_a_numbered_html_file()
    {
        using var temp = new TempDir();
        var src = temp.Sub("src");
        var outDir = temp.Sub("out");
        SaveWorkbook(Path.Combine(src, "1.xlsx"), p => Add(p, "MSBBL6020", "x", [Block("A", "a")]));
        SaveWorkbook(Path.Combine(src, "2.xlsx"), p => Add(p, "MSBBL6020", "y", [Block("B", "b")]));

        var records = new CrudDocImporter().ImportDirectory(src, outDir, _ => { });

        Assert.Equal(["MSBBL6020.html", "MSBBL6020_2.html"], records.Select(r => r.HtmlFileName));
        Assert.True(File.Exists(Path.Combine(outDir, "MSBBL6020_2.html")));
    }

    [Fact]
    public void Lock_files_are_ignored_and_a_broken_file_does_not_stop_the_batch()
    {
        using var temp = new TempDir();
        var src = temp.Sub("src");
        var outDir = temp.Sub("out");
        File.WriteAllText(Path.Combine(src, "~$a.xlsx"), "lock");
        File.WriteAllText(Path.Combine(src, "broken.xlsx"), "not a zip");
        SaveWorkbook(Path.Combine(src, "ok.xlsx"), p => Add(p, "MSBBB0020", "x", [Block("A", "a")]));
        var log = new List<string>();

        var records = new CrudDocImporter().ImportDirectory(src, outDir, log.Add);

        Assert.Single(records);
        Assert.Contains("Tim thay 2 file .xlsx.", log);
        Assert.Contains(log, l => l.StartsWith("LOI mo file 'broken.xlsx'"));
    }

    [Fact]
    public void Missing_folder_is_logged_not_thrown()
    {
        var log = new List<string>();

        var records = new CrudDocImporter().ImportDirectory(@"Z:\khong\ton\tai", Path.GetTempPath(), log.Add);

        Assert.Empty(records);
        Assert.StartsWith("Khong tim thay thu muc", Assert.Single(log));
    }
}
