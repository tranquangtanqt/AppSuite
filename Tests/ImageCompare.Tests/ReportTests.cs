using ImageCompare.Engine;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>Báo cáo HTML (Xuất báo cáo): kết luận, số vùng, mã hoá tên file, ảnh nhúng.</summary>
public class ReportTests
{
    [Fact]
    public void Identical_report_says_so()
    {
        using var a = Images.Page(300, 200);
        using var b = Images.Copy(a);
        var options = new DiffOptions();
        using var view = ImageComparer.CreateView(a, b, options);

        string html = HtmlReport.Build("a.png", a, "b.png", b, options, view);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("Giống hệt nhau", html);
        Assert.DoesNotContain("Các vùng khác nhau", html);
    }

    [Fact]
    public void Report_lists_each_region_and_encodes_file_names()
    {
        using var a = Images.Page(400, 300);
        using var b = Images.Copy(a);
        Images.Fill(b, SKRectI.Create(50, 50, 20, 20), SKColors.Magenta);
        Images.Fill(b, SKRectI.Create(300, 200, 20, 20), SKColors.Magenta);
        var options = new DiffOptions { Align = AlignMode.None };
        using var view = ImageComparer.CreateView(a, b, options);

        string html = HtmlReport.Build("<A>&.png", a, "B.png", b, options, view);

        Assert.Contains("Có 2 chỗ khác nhau", html);
        Assert.Contains("Các vùng khác nhau", html);
        Assert.Contains("&lt;A&gt;&amp;.png", html);
        Assert.DoesNotContain("<A>&.png", html);
        Assert.Contains("data:image/png;base64,", html);
    }
}

/// <summary>Xuất kết quả So chữ (Engine/TextDiffReport): dòng tách Tab, CSV, báo cáo HTML.</summary>
public class TextDiffReportTests
{
    private static TextSegment Seg(string text, int x, int y) => new(text, TextDiff.Key(text), SKRectI.Create(x, y, 24, 12));

    private static readonly TextDiffItem[] Items =
    [
        new(1, TextDiffKind.Changed, Seg("200", 10, 10), Seg("1,200", 12, 14)),
        new(2, TextDiffKind.ColorChanged, Seg("確定", 10, 40), Seg("確定", 12, 44), "#A0A0A0 → #000000"),
        new(3, TextDiffKind.OnlyInB, null, Seg("胴サイズ", 12, 74), Other: SKRectI.Create(10, 70, 24, 12)),
    ];

    [Fact]
    public void Tsv_has_header_and_one_line_per_item_with_columns()
    {
        var lines = TextDiffReport.Tsv(Items).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("#\tLoại\tChữ A\tChữ B", lines[0]);
        Assert.Equal(["1", "Đổi chữ", "200", "1,200", "", "10, 10, 24, 12", "12, 14, 24, 12"], lines[1].Split('\t'));
        Assert.Equal("#A0A0A0 → #000000", lines[2].Split('\t')[4]);
        // Chỉ ở B: vị trí phía A là chỗ dự đoán.
        Assert.Equal("≈ 10, 70, 24, 12", lines[3].Split('\t')[5]);
    }

    [Fact]
    public void Csv_quotes_cells_with_commas_and_quotes()
    {
        string csv = TextDiffReport.Csv([new TextDiffItem(1, TextDiffKind.Changed, Seg("a\"b", 0, 0), Seg("1,200", 0, 0))]);
        Assert.Contains("\"a\"\"b\",\"1,200\"", csv);
        Assert.Contains("\"10, 10, 24, 12\"", TextDiffReport.Csv(Items));
    }

    [Fact]
    public void Html_lists_items_with_crops_and_encodes_text()
    {
        using var a = Images.Page(200, 120);
        using var b = Images.Copy(a);
        var result = new TextDiffResult([.. Items, new TextDiffItem(4, TextDiffKind.Changed, Seg("<x>", 10, 100), Seg("&y", 10, 100))], 10, 10, 7, SKPointI.Empty);
        string html = TextDiffReport.Html("<A>.png", a, "B.png", b, result, result.Items, "Windows OCR (tiếng Nhật)");
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("4 chỗ khác về chữ", html);
        Assert.Contains("Khác màu chữ", html);
        Assert.Contains("&lt;x&gt;", html);
        Assert.DoesNotContain("<x>", html);
        Assert.Contains("&lt;A&gt;.png", html);
        Assert.True(html.Split("class=\"crop\"").Length - 1 >= 8, "mỗi mục 2 ảnh cắt A | B");
    }
}
