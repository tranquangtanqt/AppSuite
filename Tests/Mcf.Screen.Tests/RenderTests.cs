using System.Text;
using Mcf.Screen.HtmlGenerator.Services;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using static Mcf.Screen.Tests.Sheets;

namespace Mcf.Screen.Tests;

public class ScreenCodeParserTests
{
    [Fact]
    public void Splits_standard_file_name()
    {
        var p = ScreenCodeParser.Parse("M7-US-1102_MSBBP1210_r01.02_画面説明書（受注登録）");

        Assert.Equal(new ParsedFileName("M7-US-1102", "MSBBP1210", "r01.02", "受注登録"), p);
    }

    [Fact]
    public void Common_doc_with_short_code_still_matches()
    {
        Assert.Equal("M7", ScreenCodeParser.Parse("M7-US-1100_M7_r01.02_画面説明書（共通）").ScreenCode);
    }

    [Fact]
    public void Unknown_name_falls_back_to_the_whole_name()
    {
        Assert.Equal(new ParsedFileName("", "受注メモ", "", "受注メモ"), ScreenCodeParser.Parse("受注メモ"));
    }
}

/// <summary>Lưới chung (sheet không có parser riêng): bỏ khối header "mcframe 7", gộp ô, style, escape.</summary>
public class GridRenderTests
{
    private static string Render(ExcelWorksheet sheet, StringBuilder? search = null, string? screenImage = null, List<string>? log = null)
    {
        using var temp = new TempDir();
        return ExcelSheetHtmlRenderer.RenderSheet(sheet, temp.Path, "Images/X", search ?? new StringBuilder(), (log ?? []).Add, screenImage);
    }

    [Fact]
    public void Skips_the_repeated_document_header_block()
    {
        using var p = new ExcelPackage();
        var s = Add(p, "その他");
        Write(s, ContentRow, "本文");

        string html = Render(s);

        Assert.Contains("本文", html);
        Assert.DoesNotContain("mcframe 7", html);
        Assert.DoesNotContain("M7-US-1102", html);
        Assert.DoesNotContain(">その他<", html);
    }

    [Fact]
    public void Sheet_without_the_anchor_keeps_every_row()
    {
        using var p = new ExcelPackage();
        var s = Add(p, "その他", withDocHeader: false);
        Write(s, 1, "先頭行");
        Write(s, 3, "三行目");

        string html = Render(s);

        Assert.Contains("先頭行", html);
        Assert.Contains("三行目", html);
    }

    [Fact]
    public void Merged_cells_become_rowspan_colspan_and_covered_cells_are_not_drawn()
    {
        using var p = new ExcelPackage();
        var s = Add(p, "その他");
        Write(s, ContentRow, "結合", "", "右");
        s.Cells[ContentRow, 1, ContentRow + 1, 2].Merge = true;
        Write(s, ContentRow + 1, "", "", "下");

        string html = Render(s);

        Assert.Contains(" rowspan=\"2\" colspan=\"2\"", html);
        // Dòng 2 của vùng gộp chỉ còn ô "下" (2 ô bị che không vẽ).
        int second = html.IndexOf("下", StringComparison.Ordinal);
        string row2 = html[html.LastIndexOf("<tr>", second, StringComparison.Ordinal)..second];
        Assert.Equal(1, row2.Split("<td").Length - 1);
    }

    [Fact]
    public void Cell_style_text_escape_and_line_breaks()
    {
        using var p = new ExcelPackage();
        var s = Add(p, "その他");
        Write(s, ContentRow, "a<b>&c", "1行\n2行");
        s.Cells[ContentRow, 1].Style.Font.Bold = true;
        s.Cells[ContentRow, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        s.Cells[ContentRow, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Yellow);
        s.Cells[ContentRow, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        string html = Render(s);

        Assert.Contains("a&lt;b&gt;&amp;c", html);
        Assert.Contains("1行<br>2行", html);
        Assert.Contains("background-color:#FFFF00", html);
        Assert.Contains("font-weight:600", html);
        Assert.Contains("text-align:center", html);
    }

    [Fact]
    public void Content_goes_into_the_search_text()
    {
        using var p = new ExcelPackage();
        var s = Add(p, "その他");
        Write(s, ContentRow, "得意先コード", "受注番号");
        var search = new StringBuilder();

        Render(s, search);

        Assert.Contains("得意先コード", search.ToString());
        Assert.Contains("受注番号", search.ToString());
    }
}

/// <summary>概要: các mục 【...】 → đoạn văn / bảng theo tiêu đề cột; mục không khớp → lưới; chèn 画面イメージ sau 【説明】.</summary>
public class OverviewTests
{
    private static ExcelWorksheet Overview(ExcelPackage p)
    {
        var s = Add(p, "概要");
        Write(s, 7, "", "【説明】");
        Write(s, 8, "", "", "受注を登録する画面");
        Write(s, 9, "", "", "", "・明細も登録する");
        Write(s, 10, "", "【オペレーション一覧】");
        Write(s, 11, "", "ID", "処理名", "処理概要");
        Write(s, 12, "", "OP01", "検索", "条件で検索", "", "", "", "【補足】"); // 【...】 xa lề trái: không phải mục mới
        Write(s, 14, "", "OP02", "登録");
        Write(s, 15, "", "【目的】");
        Write(s, 16, "", "", "自由な文章");                                    // không có hàng tiêu đề 目的（レベル１）...
        return s;
    }

    private static string Render(ExcelWorksheet s, string? screenImage = null, List<string>? log = null, StringBuilder? search = null)
    {
        using var temp = new TempDir();
        return ExcelSheetHtmlRenderer.RenderSheet(s, temp.Path, "Images/X", search ?? new StringBuilder(), (log ?? []).Add, screenImage);
    }

    [Fact]
    public void Renders_sections_as_headings()
    {
        using var p = new ExcelPackage();

        string html = Render(Overview(p));

        Assert.Contains("<div class=\"semantic-doc\">", html);
        Assert.Contains("<h3>【説明】</h3>", html);
        Assert.Contains("<h3>【オペレーション一覧】</h3>", html);
        Assert.Contains("<h3>【目的】</h3>", html);
        Assert.DoesNotContain("<h3>【補足】</h3>", html);
    }

    [Fact]
    public void Free_text_is_indented_by_column()
    {
        using var p = new ExcelPackage();

        string html = Render(Overview(p));

        Assert.Contains("<p class=\"ov-line\" style=\"padding-left:20px\">受注を登録する画面</p>", html);
        Assert.Contains("<p class=\"ov-line\" style=\"padding-left:30px\">・明細も登録する</p>", html);
    }

    [Fact]
    public void Table_section_uses_its_header_row_and_skips_blank_rows()
    {
        using var p = new ExcelPackage();

        string html = Render(Overview(p));

        Assert.Contains("<th>ID</th><th>処理名</th><th>処理概要</th>", html);
        Assert.Contains("<tr><td>OP01</td><td>検索</td><td>条件で検索</td></tr>", html);
        Assert.Contains("<tr><td>OP02</td><td>登録</td><td></td></tr>", html);
        Assert.DoesNotContain("<tr><td></td><td></td><td></td></tr>", html);
    }

    [Fact]
    public void Section_without_expected_header_falls_back_to_grid()
    {
        using var p = new ExcelPackage();

        string html = Render(Overview(p));

        string purpose = html[html.IndexOf("<h3>【目的】</h3>", StringComparison.Ordinal)..];
        Assert.Contains("class=\"sheet-grid\"", purpose);
        Assert.Contains("自由な文章", purpose);
    }

    [Fact]
    public void Screen_image_is_spliced_right_after_the_description()
    {
        using var p = new ExcelPackage();

        string html = Render(Overview(p), screenImage: "<p>SCREENSHOT</p>");

        int desc = html.IndexOf("<h3>【説明】</h3>", StringComparison.Ordinal);
        int image = html.IndexOf("<h3>画面イメージ</h3><p>SCREENSHOT</p>", StringComparison.Ordinal);
        int ops = html.IndexOf("<h3>【オペレーション一覧】</h3>", StringComparison.Ordinal);
        Assert.True(desc < image && image < ops, $"{desc} < {image} < {ops}");
    }

    [Fact]
    public void Overview_without_known_sections_falls_back_but_keeps_the_screen_image()
    {
        using var p = new ExcelPackage();
        var s = Add(p, "概要");
        Write(s, 7, "", "自由なレイアウト");

        string html = Render(s, screenImage: "<p>SCREENSHOT</p>");

        Assert.Contains("class=\"sheet-grid\"", html);
        Assert.EndsWith("<div class=\"ov-section\"><h3>画面イメージ</h3><p>SCREENSHOT</p></div>", html);
    }

    [Fact]
    public void Diagram_section_without_shapes_is_logged_and_shown_as_grid()
    {
        using var p = new ExcelPackage();
        var s = Add(p, "概要");
        Write(s, 7, "", "【説明】");
        Write(s, 8, "", "", "説明文");
        Write(s, 9, "", "【処理関連図】");
        Write(s, 10, "", "", "図の代わりの文字");
        var log = new List<string>();

        string html = Render(s, log: log);

        Assert.Contains("図の代わりの文字", html);
        Assert.Contains(log, l => l.StartsWith("[概要] Ve so do '【処理関連図】'"));
    }
}

/// <summary>項目説明: 1 bảng gọn (必須 / 項目名 / 説明 + các cột cờ có trong sheet), mỗi nhóm 1 dòng dải.</summary>
public class ItemExplanationTests
{
    private static string Render(ExcelWorksheet s, StringBuilder? search = null)
    {
        using var temp = new TempDir();
        return ExcelSheetHtmlRenderer.RenderSheet(s, temp.Path, "Images/X", search ?? new StringBuilder(), _ => { });
    }

    private static ExcelWorksheet Items(ExcelPackage p)
    {
        var s = Add(p, "項目説明");
        Write(s, 7, "検索条件", "", "説明", "", "型", "入");
        Write(s, 8, "○", "得意先", "得意先コード", "", "X", "○");
        Write(s, 10, "明細", "", "", "説明", "型", "", "O");
        Write(s, 11, "", "数量", "", "数量\n小数可", "9", "", "○");
        Write(s, 12, "", "単価", "説明が左にずれた"); // giá trị lệch 1 cột so với tiêu đề 説明
        return s;
    }

    [Fact]
    public void Header_has_only_the_flag_columns_used_by_any_group()
    {
        using var p = new ExcelPackage();

        string html = Render(Items(p));

        Assert.Contains("<thead><tr><th>必須</th><th>項目名</th><th>説明</th><th>型</th><th>入</th><th>O</th></tr></thead>", html);
    }

    [Fact]
    public void Each_group_gets_a_band_row_and_blank_rows_are_skipped()
    {
        using var p = new ExcelPackage();

        string html = Render(Items(p));

        Assert.Contains("<tr class=\"item-group-row\"><td colspan=\"6\">検索条件</td></tr>", html);
        Assert.Contains("<tr class=\"item-group-row\"><td colspan=\"6\">明細</td></tr>", html);
        Assert.Contains("<tr><td>○</td><td>得意先</td><td>得意先コード</td><td>X</td><td>○</td><td></td></tr>", html);
        Assert.Contains("<tr><td></td><td>数量</td><td>数量<br>小数可</td><td>9</td><td></td><td>○</td></tr>", html);
        Assert.Contains("<td>説明が左にずれた</td>", html);
        Assert.Equal(5, html.Split("<tr").Length - 1 - 1); // 2 dải + 3 dòng (trừ dòng tiêu đề)
    }

    [Fact]
    public void Names_and_descriptions_go_into_search_text()
    {
        using var p = new ExcelPackage();
        var search = new StringBuilder();

        Render(Items(p), search);

        Assert.Contains("得意先コード", search.ToString());
        Assert.Contains("単価", search.ToString());
    }
}

/// <summary>画面遷移: mỗi khối ID → chip thông tin + chi tiết 遷移処理 / 復帰処理 + bảng 引継項目.</summary>
public class ScreenDiagramTests
{
    private static string Render(ExcelWorksheet s)
    {
        using var temp = new TempDir();
        return ExcelSheetHtmlRenderer.RenderSheet(s, temp.Path, "Images/X", new StringBuilder(), _ => { });
    }

    [Fact]
    public void Renders_block_details_and_carried_items()
    {
        using var p = new ExcelPackage();
        var s = Add(p, "画面遷移");
        Write(s, 7, "ID", "処理名", "ダブルクリック対象", "ダブルクリック処理条件");
        Write(s, 8, "TR01", "明細表示", "一覧", "選択時");
        Write(s, 9, "", "遷移処理");
        Write(s, 10, "", "遷移先画面ＩＤ", "MSBBP1220");
        Write(s, 11, "", "遷移先画面名", "受注明細");
        Write(s, 12, "", "遷移条件", "行選択");
        Write(s, 13, "", "引継項目", "遷移元画面項目", "遷移先画面項目");
        Write(s, 14, "", "", "受注番号", "受注No");
        Write(s, 15, "", "遷移後のアクション", "検索実行");
        Write(s, 16, "", "復帰処理");
        Write(s, 17, "", "引継項目", "遷移元", "遷移先");
        Write(s, 18, "", "", "数量", "数量2");

        string html = Render(s);

        Assert.Contains("<span class=\"chip\">ID: TR01</span><span class=\"chip\">処理名: 明細表示</span>", html);
        Assert.Contains("<span class=\"chip\">条件: 選択時</span>", html);
        Assert.Contains("<tr><th>遷移先画面ＩＤ</th><td>MSBBP1220</td></tr>", html);
        Assert.Contains("<tr><th>遷移後のアクション</th><td>検索実行</td></tr>", html);
        Assert.Contains("<tr><td>受注番号</td><td>受注No</td></tr>", html);
        string back = html[html.IndexOf("<strong>復帰処理</strong>", StringComparison.Ordinal)..];
        Assert.Contains("<tr><td>数量</td><td>数量2</td></tr>", back);
        Assert.DoesNotContain("受注No", back);
    }

    [Fact]
    public void Block_without_detail_labels_falls_back_to_grid()
    {
        using var p = new ExcelPackage();
        var s = Add(p, "画面遷移");
        Write(s, 7, "ID", "処理名", "ダブルクリック対象", "ダブルクリック処理条件");
        Write(s, 8, "TR01", "明細表示");
        Write(s, 9, "", "自由メモ");

        string html = Render(s);

        Assert.Contains("<span class=\"chip\">ID: TR01</span>", html);
        Assert.Contains("class=\"sheet-grid\"", html);
        Assert.Contains("自由メモ", html);
    }
}
