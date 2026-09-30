using ImageCompare.Engine;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>Tự căn dịch chuyển (ảnh chụp lệch vài px / khác lề) và căn theo dòng (trang dài thêm / bớt 1 đoạn).</summary>
public class AlignTests
{
    [Theory]
    [InlineData(7, -3)]
    [InlineData(-5, -12)]
    [InlineData(0, 150)]   // dịch dọc xa (dò tối đa 25% chiều cao = 175 px)
    [InlineData(0, 0)]
    public void Translate_finds_the_shift_and_leaves_no_regions(int dx, int dy)
    {
        using var scene = Images.Page(1000, 1100);
        var originA = new SKPointI(200, 200);
        using var a = Images.Crop(scene, SKRectI.Create(originA.X, originA.Y, 600, 700));
        using var b = Images.Crop(scene, SKRectI.Create(originA.X + dx, originA.Y + dy, 600, 700));

        var r = ImageComparer.Compare(a, b, new DiffOptions { Align = AlignMode.Translate });

        Assert.Equal(new SKPointI(dx, dy), r.OffsetB);
        Assert.Empty(r.Regions);
        Assert.Equal($"Tự căn: B lệch ({dx}, {dy}) px", r.Stats.AlignNote);
    }

    [Fact]
    public void Translate_handles_B_wider_than_A()
    {
        using var scene = Images.Page(1000, 800);
        using var a = Images.Crop(scene, SKRectI.Create(100, 100, 600, 500));
        using var b = Images.Crop(scene, SKRectI.Create(93, 103, 700, 520));

        var r = ImageComparer.Compare(a, b, new DiffOptions());

        Assert.Equal(new SKPointI(-7, 3), r.OffsetB);
        Assert.Empty(r.Regions);
        Assert.True(r.OnlyInB > 0);
    }

    [Fact]
    public void Translate_still_reports_real_changes_after_aligning()
    {
        using var scene = Images.Page(900, 700);
        using var a = Images.Crop(scene, SKRectI.Create(100, 100, 600, 400));
        using var b = Images.Crop(scene, SKRectI.Create(105, 112, 600, 400));
        Images.Fill(b, SKRectI.Create(300, 200, 40, 20), SKColors.Magenta);

        var r = ImageComparer.Compare(a, b, new DiffOptions());

        Assert.Equal(new SKPointI(5, 12), r.OffsetB);
        var region = Assert.Single(r.Regions);
        // Toạ độ vùng theo ảnh A: chỗ sửa ở B (300, 200) nằm ở A (305, 212).
        Assert.True(region.Bounds.Contains(SKRectI.Create(305, 212, 40, 20)), region.Bounds.ToString());
    }

    [Fact]
    public void Rows_detects_a_band_inserted_in_the_middle_of_a_long_page()
    {
        using var a = Images.Page(500, 1500, seed: 3);
        using var band = Images.Noise(500, 240, seed: 9);
        using var b = Images.InsertRows(a, 600, band);

        using var view = ImageComparer.CreateView(a, b, new DiffOptions { Align = AlignMode.Rows });

        Assert.IsType<RowDiffView>(view);
        Assert.Equal("Căn theo dòng: B thêm 240 dòng, bỏ 0 dòng so với A", view.Stats.AlignNote);
        var region = Assert.Single(view.Regions);
        Assert.Equal(RegionKind.OnlyInB, region.Kind);
        Assert.Equal(240, region.Bounds.Height);
        Assert.Equal(600, region.Bounds.Top);
        Assert.Equal(240L * 500, view.Stats.OnlyInB);
        Assert.Equal(0, view.Stats.DiffPixels);
        Assert.True(double.IsNaN(view.Stats.Ssim));

        var (inA, inB) = view.SourceRects(region);
        Assert.Null(inA);
        Assert.Equal(SKRectI.Create(0, 600, 500, 240), inB);
    }

    [Fact]
    public void Rows_detects_a_removed_band()
    {
        using var b = Images.Page(500, 1500, seed: 4);
        using var band = Images.Noise(500, 120, seed: 5);
        using var a = Images.InsertRows(b, 300, band);

        using var view = ImageComparer.CreateView(a, b, new DiffOptions { Align = AlignMode.Rows });

        Assert.Equal("Căn theo dòng: B thêm 0 dòng, bỏ 120 dòng so với A", view.Stats.AlignNote);
        Assert.Equal(RegionKind.OnlyInA, Assert.Single(view.Regions).Kind);
    }

    [Fact]
    public void Rows_on_identical_pages_reports_nothing()
    {
        using var a = Images.Page(400, 800);
        using var b = Images.Copy(a);

        using var view = ImageComparer.CreateView(a, b, new DiffOptions { Align = AlignMode.Rows });

        Assert.True(view.Stats.IsIdentical);
        Assert.Equal("Căn theo dòng: không có đoạn thêm / bớt", view.Stats.AlignNote);
    }

    [Fact]
    public void Rows_falls_back_to_translate_when_images_are_too_different()
    {
        // Mỗi dòng nhiễu là duy nhất → Myers cần 1700 + 1700 > MaxEdits bước.
        using var a = Images.Noise(300, 1700, seed: 1);
        using var b = Images.Noise(300, 1700, seed: 2);

        using var view = ImageComparer.CreateView(a, b, new DiffOptions { Align = AlignMode.Rows });

        Assert.IsType<DiffPainter>(view);
        Assert.StartsWith("2 ảnh khác nhau quá nhiều để căn theo dòng", view.Stats.AlignNote);
        Assert.Equal(AlignMode.Translate, view.Stats.Align);
    }

    [Fact]
    public void DiffPainter_render_covers_A_union_B()
    {
        using var scene = Images.Page(900, 700);
        using var a = Images.Crop(scene, SKRectI.Create(100, 100, 600, 400));
        using var b = Images.Crop(scene, SKRectI.Create(110, 90, 600, 400));

        using var view = ImageComparer.CreateView(a, b, new DiffOptions());
        using var png = view.Render();

        Assert.Equal(SKRectI.Create(0, -10, 610, 410), view.Bounds);
        Assert.Equal(610, png.Width);
        Assert.Equal(410, png.Height);
    }
}
