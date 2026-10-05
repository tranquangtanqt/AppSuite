using ImageCompare.Engine;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>Soi 1 vùng (AlignMode.Focus): 2 ảnh lệch bố cục dần - không có 1 độ dịch nào khớp cả trang, nhưng khoanh 1 vùng
/// thì vùng đó tự tìm chỗ khớp riêng ở B và chỉ báo chỗ khác thật.</summary>
public class FocusTests
{
    /// <summary>Trang gồm các dòng khối màu (cùng nội dung ở A và B); dòng thứ i của B thấp hơn A thêm i × <paramref name="drift"/>
    /// px - như B dùng font / trình duyệt có dòng cao hơn một chút.</summary>
    private static SKBitmap DriftPage(int drift, int rows = 50, int seed = 3)
    {
        const int width = 700, rowStep = 24;
        var bmp = Images.New(width, 20 + rows * (rowStep + drift) + 20, SKColors.White);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { IsAntialias = false };
        var rnd = new Random(seed);
        for (int i = 0; i < rows; i++)
        {
            int y = 20 + i * (rowStep + drift);
            int x = 10 + rnd.Next(30);
            while (x < width - 60)
            {
                int w = 10 + rnd.Next(60);
                paint.Color = new SKColor((byte)rnd.Next(160), (byte)rnd.Next(160), (byte)rnd.Next(160));
                canvas.DrawRect(x, y, w, 8 + rnd.Next(8), paint);
                x += w + 6 + rnd.Next(20);
            }
        }
        return bmp;
    }

    private static DiffOptions Focus(SKRectI rect) => new() { Align = AlignMode.Focus, FocusRect = rect };

    [Fact]
    public void Whole_page_translate_reports_drifting_layout_as_many_changes()
    {
        using var a = DriftPage(0);
        using var b = DriftPage(2);

        var r = ImageComparer.Compare(a, b, new DiffOptions { Align = AlignMode.Translate });

        Assert.True(r.IdenticalPercent < 95, $"{r.IdenticalPercent}% giống - ảnh thử phải lệch thật");
    }

    [Theory]
    [InlineData(5)]   // gần đầu trang: B lệch ~10 px
    [InlineData(40)]  // gần cuối trang: B lệch ~80 px
    public void Focused_region_finds_its_own_offset_and_is_identical(int row)
    {
        using var a = DriftPage(0);
        using var b = DriftPage(2);
        var focus = SKRectI.Create(0, 20 + row * 24 - 4, 700, 3 * 24); // 3 dòng

        var r = ImageComparer.Compare(a, b, Focus(focus));

        Assert.Equal(AlignMode.Focus, r.Align);
        Assert.Equal(focus, r.Focus);
        // Mỗi dòng căn riêng: dòng row + k lệch 2·(row + k) px; độ lệch chung = dòng giữa.
        Assert.Equal(new SKPointI(0, -2 * (row + 1)), r.OffsetB);
        Assert.Equal([-2 * row, -2 * (row + 1), -2 * (row + 2)], r.FocusMatch!.Cells.Select(c => c.OffsetB.Y).Distinct().Order().Reverse());
        Assert.All(r.FocusMatch.Cells, c => Assert.Equal(0, c.OffsetB.X));
        Assert.Empty(r.Regions);
        Assert.Equal(100, r.IdenticalPercent);
        Assert.Equal(0, r.OnlyInA);
        Assert.Equal(0, r.OnlyInB);
    }

    [Fact]
    public void Real_change_inside_focus_is_the_only_region_and_outside_changes_are_ignored()
    {
        using var a = DriftPage(0);
        using var b = DriftPage(2);
        const int row = 30;
        var focus = SKRectI.Create(0, 20 + row * 24 - 4, 700, 3 * 24);
        // Đổi 1 khối trong vùng soi (toạ độ B = toạ độ A + độ lệch của dòng đó) và 1 khối ngoài vùng.
        Images.Fill(b, SKRectI.Create(300, 20 + row * 26 + 26, 40, 10), SKColors.Magenta);
        Images.Fill(b, SKRectI.Create(300, 20 + 5 * 26, 40, 10), SKColors.Magenta);

        var r = ImageComparer.Compare(a, b, Focus(focus));

        var region = Assert.Single(r.Regions);
        Assert.True(focus.Contains(region.Bounds), $"{region.Bounds} phải nằm trong {focus}");
        Assert.InRange(region.Bounds.Left, 296, 304);
    }

    [Fact]
    public void Repeated_content_prefers_the_nearest_match()
    {
        // Sọc lặp lại đều mỗi 20 px: dịch ±20 px cũng khớp y hệt → phải chọn chỗ gần vị trí cũ (B dịch 3 px).
        using var a = Images.New(400, 600, SKColors.White);
        for (int y = 0; y < 600; y += 20)
        {
            Images.Fill(a, SKRectI.Create(20, y, 360, 6), SKColors.Black);
        }
        using var b = Images.New(400, 600, SKColors.White);
        for (int y = 3; y < 600; y += 20)
        {
            Images.Fill(b, SKRectI.Create(20, y, 360, 6), SKColors.Black);
        }

        var r = ImageComparer.Compare(a, b, Focus(SKRectI.Create(10, 200, 380, 100)));

        Assert.Equal(new SKPointI(0, -3), r.OffsetB);
        Assert.Empty(r.Regions);
    }

    [Fact]
    public void Without_a_focus_rect_it_compares_like_auto_align()
    {
        using var a = Images.Page(600, 400);
        using var b = Images.Copy(a);

        var r = ImageComparer.Compare(a, b, new DiffOptions { Align = AlignMode.Focus });

        Assert.Equal(AlignMode.Translate, r.Align);
        Assert.Null(r.Focus);
        Assert.True(r.IsIdentical);
    }

    [Fact]
    public void Focus_outside_image_a_falls_back_to_auto_align()
    {
        using var a = Images.Page(600, 400);
        using var b = Images.Copy(a);

        var r = ImageComparer.Compare(a, b, Focus(SKRectI.Create(1000, 1000, 50, 50)));

        Assert.Null(r.Focus);
        Assert.Equal(AlignMode.Translate, r.Align);
    }

    [Fact]
    public void Painter_renders_focus_view_and_note_describes_it()
    {
        using var a = DriftPage(0);
        using var b = DriftPage(2);
        var focus = SKRectI.Create(0, 100, 700, 80);

        using var view = ImageComparer.CreateView(a, b, Focus(focus));
        using var png = view.Render();

        Assert.StartsWith("Soi vùng 700 × 80 tại (0, 100)", view.Stats.AlignNote);
        Assert.Equal(view.Bounds.Width, png.Width);
        // Ngoài vùng soi bị phủ tối, trong vùng giữ nền sáng.
        var inside = png.GetPixel(5 - view.Bounds.Left, 140 - view.Bounds.Top);
        var outside = png.GetPixel(5 - view.Bounds.Left, 20 - view.Bounds.Top);
        Assert.True(inside.Red > 200 && outside.Red < 150, $"trong {inside}, ngoài {outside}");
    }
}
