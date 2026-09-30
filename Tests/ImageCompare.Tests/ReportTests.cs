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
