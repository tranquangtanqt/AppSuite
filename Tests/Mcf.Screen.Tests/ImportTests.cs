using System.Xml;
using Mcf.Screen.HtmlGenerator.Models;
using Mcf.Screen.HtmlGenerator.Services;
using SkiaSharp;
using static Mcf.Screen.Tests.Sheets;

namespace Mcf.Screen.Tests;

/// <summary>Sơ đồ 処理関連図: đọc shape / connector từ DrawingML thô, vẽ ra PNG.</summary>
public class DiagramTests
{
    private const string Drawing = """
        <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <xdr:twoCellAnchor>
            <xdr:from><xdr:col>1</xdr:col><xdr:row>5</xdr:row></xdr:from>
            <xdr:sp>
              <xdr:spPr>
                <a:xfrm><a:off x="100" y="200"/><a:ext cx="1000" cy="500"/></a:xfrm>
                <a:prstGeom prst="flowChartProcess"/>
                <a:solidFill><a:srgbClr val="FFCC00"/></a:solidFill>
              </xdr:spPr>
              <xdr:txBody><a:p><a:r><a:t>受注</a:t></a:r><a:br/><a:r><a:t>登録</a:t></a:r></a:p></xdr:txBody>
            </xdr:sp>
          </xdr:twoCellAnchor>
          <xdr:twoCellAnchor>
            <xdr:from><xdr:col>3</xdr:col><xdr:row>6</xdr:row></xdr:from>
            <xdr:cxnSp>
              <xdr:spPr>
                <a:xfrm flipH="1"><a:off x="1100" y="450"/><a:ext cx="900" cy="300"/></a:xfrm>
                <a:prstGeom prst="bentConnector3"><a:avLst><a:gd name="adj1" fmla="val 25000"/></a:avLst></a:prstGeom>
                <a:ln w="19050"><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill><a:tailEnd type="triangle"/></a:ln>
              </xdr:spPr>
            </xdr:cxnSp>
          </xdr:twoCellAnchor>
          <xdr:twoCellAnchor>
            <xdr:from><xdr:col>1</xdr:col><xdr:row>50</xdr:row></xdr:from>
            <xdr:sp><xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="10" cy="10"/></a:xfrm></xdr:spPr></xdr:sp>
          </xdr:twoCellAnchor>
        </xdr:wsDr>
        """;

    private static List<DiagramShape> Read(int fromRow0, int toRow0)
    {
        var doc = new XmlDocument();
        doc.LoadXml(Drawing);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("xdr", "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing");
        ns.AddNamespace("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        return DiagramXmlReader.ReadShapesInRowRange(doc, ns, fromRow0, toRow0);
    }

    [Fact]
    public void Reads_boxes_and_connectors_in_row_range()
    {
        var shapes = Read(0, 10);

        Assert.Equal(2, shapes.Count);
        var box = shapes[0];
        Assert.False(box.IsConnector);
        Assert.Equal(("flowChartProcess", 100d, 200d, 1000d, 500d), (box.Preset, box.X, box.Y, box.Width, box.Height));
        Assert.Equal("受注\n登録", box.Text);
        Assert.Equal(0xFFFFCC00u, box.FillColorArgb);

        var line = shapes[1];
        Assert.True(line.IsConnector);
        Assert.Equal("bentConnector3", line.Preset);
        Assert.Equal(0.25, line.BendFraction);
        Assert.True(line.FlipHorizontal);
        Assert.Equal(19050, line.LineWidthEmu);
        Assert.Equal(0xFF0000FFu, line.LineColorArgb);
        Assert.True(line.HasEndArrow);
        Assert.False(line.HasStartArrow);
        Assert.Null(line.Text);
    }

    [Fact]
    public void Shapes_outside_the_range_are_ignored()
    {
        Assert.Single(Read(6, 10));
        Assert.Empty(Read(20, 40));
    }

    [Fact]
    public void Renders_png_and_lists_box_texts()
    {
        using var temp = new TempDir();
        var png = Path.Combine(temp.Path, "d.png");

        bool ok = DiagramRenderer.TryRender(Read(0, 10), png, out var error, out var texts);

        Assert.True(ok, error);
        Assert.Equal(["受注 登録"], texts);
        using var bmp = SKBitmap.Decode(png);
        Assert.NotNull(bmp);
        Assert.True(bmp.Width > 32 && bmp.Height > 32);
    }

    [Fact]
    public void No_shapes_is_reported_as_failure()
    {
        using var temp = new TempDir();

        Assert.False(DiagramRenderer.TryRender([], Path.Combine(temp.Path, "d.png"), out var error, out _));
        Assert.NotNull(error);
    }
}

/// <summary>Đọc cả thư mục: 1 file = 1 màn hình, bỏ 表紙 / 変更来歴, 画面イメージ chèn vào 概要, trùng mã màn hình.</summary>
public class ImporterTests
{
    private const string FileName = "M7-US-1102_MSBBP1210_r01.02_画面説明書（受注登録）.xlsx";

    private static void Standard(OfficeOpenXml.ExcelPackage p)
    {
        Add(p, "表紙").Cells[10, 1].Value = "表紙の文字";
        Add(p, "変更来歴").Cells[10, 1].Value = "来歴の文字";
        var overview = Add(p, "概要");
        Write(overview, 7, "", "【説明】");
        Write(overview, 8, "", "", "受注を登録する");
        Add(p, "画面イメージ").Cells[8, 2].Value = "画面のスクショ";
        Add(p, "その他").Cells[8, 1].Value = "その他の文字";
    }

    [Fact]
    public void Imports_one_record_per_file()
    {
        using var temp = new TempDir();
        var src = temp.Sub("src");
        var outDir = temp.Sub("out");
        Save(Path.Combine(src, FileName), Standard);

        var record = Assert.Single(new ScreenDocImporter().ImportDirectory(src, outDir, _ => { }));

        Assert.Equal(("MSBBP1210", "受注登録", "M7-US-1102", "r01.02", "MSBBP1210.html", 2),
            (record.ScreenCode, record.ScreenName, record.DocNumber, record.Revision, record.HtmlFileName, record.SheetCount));
        Assert.Contains("受注を登録する", record.SearchText);
        Assert.Contains("画面のスクショ", record.SearchText);
        Assert.DoesNotContain("表紙の文字", record.SearchText);
        Assert.DoesNotContain("来歴の文字", record.SearchText);
    }

    [Fact]
    public void Page_has_nav_per_sheet_and_the_screen_image_inside_overview()
    {
        using var temp = new TempDir();
        var src = temp.Sub("src");
        var outDir = temp.Sub("out");
        Save(Path.Combine(src, FileName), Standard);

        new ScreenDocImporter().ImportDirectory(src, outDir, _ => { });
        string html = File.ReadAllText(Path.Combine(outDir, "MSBBP1210.html"));

        Assert.Contains("<title>MSBBP1210 - 受注登録</title>", html);
        Assert.Contains("<a href=\"#sheet-1\">概要</a><a href=\"#sheet-2\">その他</a>", html);
        Assert.DoesNotContain(">表紙<", html);
        Assert.DoesNotContain("<h2>画面イメージ</h2>", html);
        string overview = html[html.IndexOf("id=\"sheet-1\"", StringComparison.Ordinal)..html.IndexOf("id=\"sheet-2\"", StringComparison.Ordinal)];
        Assert.Contains("<h3>画面イメージ</h3>", overview);
        Assert.Contains("画面のスクショ", overview);
    }

    [Fact]
    public void Screen_image_without_overview_gets_its_own_section()
    {
        using var temp = new TempDir();
        var src = temp.Sub("src");
        var outDir = temp.Sub("out");
        Save(Path.Combine(src, FileName), p =>
        {
            Add(p, "画面イメージ").Cells[8, 2].Value = "画面のスクショ";
            Add(p, "その他").Cells[8, 1].Value = "x";
        });

        var record = Assert.Single(new ScreenDocImporter().ImportDirectory(src, outDir, _ => { }));
        string html = File.ReadAllText(Path.Combine(outDir, record.HtmlFileName));

        Assert.Equal(2, record.SheetCount);
        Assert.Contains("<a href=\"#sheet-2\">画面イメージ</a>", html);
        Assert.Contains("画面のスクショ", html);
    }

    [Fact]
    public void Same_screen_code_twice_gets_numbered_file_and_bad_files_are_logged()
    {
        using var temp = new TempDir();
        var src = temp.Sub("src");
        var sub = Directory.CreateDirectory(Path.Combine(src, "old")).FullName;
        var outDir = temp.Sub("out");
        Save(Path.Combine(src, FileName), Standard);
        Save(Path.Combine(sub, "M7-US-1102_MSBBP1210_r01.01_画面説明書（受注登録）.xlsx"), Standard);
        File.WriteAllText(Path.Combine(src, "~$lock.xlsx"), "lock");
        File.WriteAllText(Path.Combine(src, "broken.xlsx"), "not a zip");
        var log = new List<string>();

        var records = new ScreenDocImporter().ImportDirectory(src, outDir, log.Add);

        Assert.Equal(["MSBBP1210.html", "MSBBP1210_2.html"], records.Select(r => r.HtmlFileName).Order(StringComparer.Ordinal));
        Assert.Contains("Tim thay 3 file .xlsx.", log);
        Assert.Contains(log, l => l.Contains("LOI 'broken.xlsx'"));
    }

    [Fact]
    public async Task Database_round_trip_and_index_page()
    {
        using var temp = new TempDir();
        var src = temp.Sub("src");
        var outDir = temp.Sub("out");
        Save(Path.Combine(src, FileName), Standard);
        var records = new ScreenDocImporter().ImportDirectory(src, outDir, _ => { });
        var db = new McfScreenHtmlGeneratorDatabase();

        await db.ReplaceAllAsync(records);
        await db.ReplaceAllAsync(records);

        ScreenRecord read = Assert.Single(db.GetAllScreens());
        Assert.Equal(("MSBBP1210", "受注登録", "MSBBP1210.html", 2), (read.ScreenCode, read.ScreenName, read.HtmlFileName, read.SheetCount));

        string index = new HtmlIndexGenerator().Generate(db, outDir);
        Assert.Equal("01_画面説明書.html", Path.GetFileName(index));
        Assert.Contains("受注登録", File.ReadAllText(index));
    }
}
