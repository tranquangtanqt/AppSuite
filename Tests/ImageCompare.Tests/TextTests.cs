using ImageCompare.Engine;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>Tìm chữ trong kết quả OCR (dựng tay, không cần Tesseract).</summary>
public class TextSearchTests
{
    private static OcrLine Line(int top, params string[] words)
    {
        var list = words.Select((w, i) => new OcrWord(w, SKRectI.Create(10 + i * 100, top, 90, 20), 90)).ToList();
        var b = list[0].Bounds;
        foreach (var w in list)
        {
            b.Union(w.Bounds);
        }
        return new OcrLine(list, b);
    }

    private static readonly OcrResult Sample = new(
    [
        Line(0, "Khách", "hàng", "C"),
        Line(30, "Trạng", "thái:", "Đã", "giao"),
        Line(60, "Khách", "hàngC", "đã", "thanh", "toán"),
    ], TimeSpan.Zero);

    [Fact]
    public void Default_ignores_case_and_diacritics()
    {
        var m = TextSearch.Find(Sample, "trang thai", matchCase: false, matchDiacritics: false);

        var hit = Assert.Single(m);
        Assert.Equal("Trạng thái: Đã giao", hit.LineText);
        // Khung bao cả 2 từ "Trạng" + "thái:".
        Assert.Equal(new SKRectI(10, 30, 200, 50), hit.Bounds);
    }

    [Fact]
    public void D_with_stroke_matches_plain_d()
    {
        Assert.Equal(2, TextSearch.Find(Sample, "da", false, false).Count(m => m.LineText.Contains("Đã") || m.LineText.Contains("đã")));
    }

    [Fact]
    public void Match_diacritics_requires_the_exact_accents()
    {
        Assert.Empty(TextSearch.Find(Sample, "Da giao", false, matchDiacritics: true));
        Assert.Single(TextSearch.Find(Sample, "Đã giao", false, matchDiacritics: true));
    }

    [Fact]
    public void Match_case_is_respected()
    {
        Assert.Empty(TextSearch.Find(Sample, "khách hàng c", matchCase: true, matchDiacritics: true));
        // Khớp cả dòng 1 ("hàng" "C") lẫn dòng 3 ("hàngC") vì bỏ qua khoảng trắng.
        Assert.Equal(2, TextSearch.Find(Sample, "Khách hàng C", matchCase: true, matchDiacritics: true).Count);
        Assert.Empty(TextSearch.Find(Sample, "Thanh toán", matchCase: true, matchDiacritics: true));
    }

    [Fact]
    public void Whitespace_is_ignored_so_joined_or_split_words_still_match()
    {
        // Dòng 1: "hàng" "C" tách rời; dòng 3: OCR dính thành "hàngC".
        var m = TextSearch.Find(Sample, "khach hang c", false, false);

        Assert.Equal(2, m.Count);
        Assert.Equal([1, 2], m.Select(x => x.Number));
        Assert.Equal(new SKRectI(10, 0, 300, 20), m[0].Bounds);   // 3 từ
        Assert.Equal(new SKRectI(10, 60, 200, 80), m[1].Bounds);  // 2 từ
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_query_finds_nothing(string query)
    {
        Assert.Empty(TextSearch.Find(Sample, query, false, false));
    }

    [Fact]
    public void Full_text_joins_lines()
    {
        Assert.Equal($"Khách hàng C{Environment.NewLine}Trạng thái: Đã giao{Environment.NewLine}Khách hàngC đã thanh toán", Sample.FullText);
    }
}

/// <summary>OCR thật bằng Tesseract (tiếng Việt) trên chữ vẽ bằng font hệ thống.</summary>
public class OcrTests
{
    private static readonly string[] Lines = ["Mã đơn hàng: #1500", "Trạng thái: Đã giao hàng", "Khách hàng thanh toán"];

    [Fact]
    public void Reads_vietnamese_text_on_light_background()
    {
        using var bmp = Images.Text(Lines, SKColors.White, SKColors.Black);

        var ocr = TextRecognizer.Recognize(bmp, TestContext.Current.CancellationToken);

        Assert.Equal(3, ocr.Lines.Count);
        Assert.Single(TextSearch.Find(ocr, "Đã giao hàng", false, matchDiacritics: true));
        Assert.Single(TextSearch.Find(ocr, "#1500", false, false));
        Assert.Single(TextSearch.Find(ocr, "khach hang thanh toan", false, false));
    }

    [Fact]
    public void Reads_text_on_dark_background()
    {
        using var bmp = Images.Text(Lines, new SKColor(0x20, 0x20, 0x24), new SKColor(0xE8, 0xE8, 0xE8));

        var ocr = TextRecognizer.Recognize(bmp, TestContext.Current.CancellationToken);

        Assert.Equal(3, ocr.Lines.Count);
        Assert.Single(TextSearch.Find(ocr, "da giao hang", false, false));
        Assert.Single(TextSearch.Find(ocr, "#1500", false, false));
    }

    [Fact]
    public void Match_bounds_are_in_original_image_coordinates()
    {
        using var bmp = Images.Text(Lines, SKColors.White, SKColors.Black);

        var ocr = TextRecognizer.Recognize(bmp, TestContext.Current.CancellationToken);
        var hit = Assert.Single(TextSearch.Find(ocr, "#1500", false, false));

        // Dòng đầu vẽ ở y ≈ 15..60 (cỡ chữ 30) - khung phải nằm trong ảnh gốc, không phải ảnh đã phóng ×2.
        Assert.InRange(hit.Bounds.Top, 5, 45);
        Assert.True(hit.Bounds.Right <= bmp.Width);
    }

    [Fact]
    public void Reports_progress_up_to_1()
    {
        using var bmp = Images.Text(Lines, SKColors.White, SKColors.Black);
        var reports = new List<double>();

        TextRecognizer.Recognize(bmp, TestContext.Current.CancellationToken, new SyncProgress(reports.Add));

        Assert.Equal(1, reports.Max());
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (this)
            {
                report(value);
            }
        }
    }
}
