using ImageCompare.Engine;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>Bảng nhiều dòng giống nhau (chỉ khác chữ số): B mỗi dòng cao hơn A vài px. Mọi cách ghép "dòng i của A ↔ dòng j
/// của B" đều khớp gần ngang nhau về hình → phải chọn cách ghép có độ lệch nhỏ (dòng i ↔ dòng i), không nhảy sang dòng kề.</summary>
public class RepeatedRowsTests(ITestOutputHelper output)
{
    private const int RowsA = 50, RowsB = 54, Top = 20;

    private static SKBitmap Form(int rowStep, bool changed)
    {
        var bmp = Images.New(800, 640, SKColors.White);
        using var canvas = new SKCanvas(bmp);
        using var typeface = SKTypeface.FromFamilyName("Arial");
        using var text = new SKPaint { Color = SKColors.Black, IsAntialias = false, TextSize = 19, Typeface = typeface };
        using var value = new SKPaint { Color = SKColors.Navy, IsAntialias = false, TextSize = 19, Typeface = typeface };
        using var box = new SKPaint { Color = SKColors.Gray, IsStroke = true, StrokeWidth = 1 };
        for (int i = 0; i < 10; i++)
        {
            int y = Top + i * rowStep;
            canvas.DrawText($"Item {i}", 20, y + 22, text);
            canvas.DrawRect(200.5f, y + 0.5f, 320, 30, box);
            canvas.DrawText(changed && i == 5 ? "value 5 CHANGED" : $"value {i}", 206, y + 22, value);
        }
        return bmp;
    }

    /// <summary>Dòng i: A ở Top + 50i, B ở Top + 54i → độ lệch đúng (quy ước B(x − dx, y − dy) ↔ A) là dy = −4i.</summary>
    private static int TrueDy(int row) => -(RowsB - RowsA) * row;

    /// <summary>"skia" = vẽ bằng SkiaSharp; "gdi" = ảnh vẽ sẵn bằng System.Drawing (Arial 14pt, như ảnh thử GUI trong Sandbox -
    /// TestData\repeated-rows-*.png), cùng bố cục.</summary>
    private static (SKBitmap A, SKBitmap B) Load(string source) => source == "gdi"
        ? (LoadPng("repeated-rows-A.png"),
           LoadPng("repeated-rows-B.png"))
        : (Form(RowsA, changed: false), Form(RowsB, changed: true));

    private static SKBitmap LoadPng(string name)
    {
        using var decoded = SKBitmap.Decode(Path.Combine(AppContext.BaseDirectory, "TestData", name));
        return ImageUtil.Normalize(decoded);
    }

    [Theory]
    [InlineData("skia")]
    [InlineData("gdi")]
    public void Translate_picks_a_small_shift_not_a_whole_row_away(string source)
    {
        var (a, b) = Load(source);
        using var _a = a;
        using var _b = b;

        var offset = Aligner.FindOffset(a, b);

        output.WriteLine($"Tự căn: {offset}");
        // Dòng 0..9 lệch 0..−36 px; bất kỳ độ lệch nào trong khoảng đó đều "đúng dòng" ở đâu đó, còn ±50 trở lên là ghép nhầm.
        Assert.InRange(offset.Y, TrueDy(9), 0);
    }

    [Theory]
    [InlineData("skia", 161, 331, 3, 6)]   // dòng 3..6 (có dòng 5 khác thật)
    [InlineData("skia", 15, 160, 0, 2)]    // dòng 0..2
    [InlineData("skia", 311, 520, 6, 9)]   // dòng 6..9 (cuối bảng)
    [InlineData("gdi", 161, 331, 3, 6)]
    [InlineData("gdi", 15, 160, 0, 2)]
    [InlineData("gdi", 311, 520, 6, 9)]
    public void Focus_on_repeated_rows_matches_the_same_rows_of_B(string source, int top, int bottom, int firstRow, int lastRow)
    {
        var (a, b) = Load(source);
        using var _a = a;
        using var _b = b;
        var focus = new SKRectI(10, top, 540, bottom);

        var r = ImageComparer.Compare(a, b, new DiffOptions { Align = AlignMode.Focus, FocusRect = focus });

        output.WriteLine(r.Stats.AlignNote);
        foreach (var c in r.FocusMatch!.Cells)
        {
            output.WriteLine($"  ô {c.Area} → {c.OffsetB}");
        }
        foreach (var region in r.Regions)
        {
            output.WriteLine($"  khác: {region.Bounds}");
        }
        // Mọi ô lệch đúng dòng của nó (± vài px): không ô nào ghép sang dòng kề (lệch thêm ~50 px).
        Assert.All(r.FocusMatch.Cells, c => Assert.InRange(c.OffsetB.Y, TrueDy(lastRow) - 4, TrueDy(firstRow) + 4));
        // Chỗ khác chỉ có "CHANGED" ở dòng 5 (A: y 270..300, x ≥ 270), không có chữ số nào bị tô.
        bool hasRow5 = firstRow <= 5 && 5 <= lastRow;
        Assert.All(r.Regions, region => Assert.True(region.Bounds.Left >= 260 && region.Bounds.Top >= 265 && region.Bounds.Bottom <= 305,
            $"vùng khác lạ {region.Bounds}"));
        Assert.Equal(hasRow5, r.Regions.Count > 0);
    }
}
