using ImageCompare.Engine;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>So từng pixel (không căn / căn tay): số vùng, khung vùng, ngưỡng màu, vùng bỏ qua, số liệu.</summary>
public class CompareTests
{
    private static readonly DiffOptions NoAlign = new() { Align = AlignMode.None };

    [Fact]
    public void Identical_images_have_no_regions()
    {
        using var a = Images.Page(600, 400);
        using var b = Images.Copy(a);

        var r = ImageComparer.Compare(a, b, NoAlign);

        Assert.True(r.IsIdentical);
        Assert.Empty(r.Regions);
        Assert.Equal(0, r.DiffPixels);
        Assert.Equal(100, r.IdenticalPercent);
        Assert.Equal(1, r.Ssim, 6);
    }

    [Fact]
    public void Three_separate_changes_give_three_regions_in_top_to_bottom_order()
    {
        using var a = Images.Page(600, 400);
        using var b = Images.Copy(a);
        SKRectI[] changes = [SKRectI.Create(400, 300, 30, 20), SKRectI.Create(50, 40, 20, 10), SKRectI.Create(250, 180, 40, 40)];
        foreach (var c in changes)
        {
            Images.Fill(b, c, SKColors.Magenta);
        }

        var r = ImageComparer.Compare(a, b, NoAlign);

        Assert.Equal(3, r.Regions.Count);
        Assert.Equal([1, 2, 3], r.Regions.Select(x => x.Number));
        var expected = changes.OrderBy(c => c.Top).ToArray();
        for (int i = 0; i < 3; i++)
        {
            // Khung ôm sát: chứa trọn chỗ sửa, không rộng quá vài px.
            var box = r.Regions[i].Bounds;
            Assert.True(box.Contains(expected[i]), $"vùng {i + 1} {box} không chứa {expected[i]}");
            Assert.InRange(box.Width - expected[i].Width, 0, 4);
            Assert.InRange(box.Height - expected[i].Height, 0, 4);
            Assert.Equal(RegionKind.Changed, r.Regions[i].Kind);
        }
        Assert.False(r.IsIdentical);
        Assert.True(r.IdenticalPercent is > 90 and < 100);
        Assert.True(r.Ssim < 1);
    }

    [Theory]
    [InlineData(8, 0)]   // chênh 10/255 ≈ 4% < ngưỡng 8% → coi như giống
    [InlineData(0, 1)]   // ngưỡng 0: khác 1 đơn vị màu cũng tính
    public void Threshold_decides_whether_a_small_color_shift_counts(double thresholdPercent, int expectedRegions)
    {
        using var a = Images.New(200, 100, new SKColor(100, 100, 100));
        using var b = Images.Copy(a);
        Images.Fill(b, SKRectI.Create(60, 30, 40, 20), new SKColor(110, 100, 100));

        var r = ImageComparer.Compare(a, b, NoAlign with { ThresholdPercent = thresholdPercent });

        Assert.Equal(expectedRegions, r.Regions.Count);
    }

    [Fact]
    public void ThresholdValue_maps_percent_to_0_255_and_clamps()
    {
        Assert.Equal(20, new DiffOptions { ThresholdPercent = 8 }.ThresholdValue);
        Assert.Equal(0, new DiffOptions { ThresholdPercent = -5 }.ThresholdValue);
        Assert.Equal(255, new DiffOptions { ThresholdPercent = 150 }.ThresholdValue);
    }

    [Fact]
    public void Changes_inside_ignore_rects_are_skipped()
    {
        using var a = Images.Page(400, 300);
        using var b = Images.Copy(a);
        Images.Fill(b, SKRectI.Create(20, 20, 30, 15), SKColors.Magenta);   // "đồng hồ" - bỏ qua
        Images.Fill(b, SKRectI.Create(200, 200, 30, 15), SKColors.Magenta); // chỗ sửa thật

        var r = ImageComparer.Compare(a, b, NoAlign with { IgnoreRects = [SKRectI.Create(10, 10, 60, 40)] });

        var region = Assert.Single(r.Regions);
        Assert.True(region.Bounds.Contains(SKRectI.Create(200, 200, 30, 15)));
    }

    [Fact]
    public void Different_sizes_count_only_in_A_and_only_in_B_pixels()
    {
        using var a = Images.Page(300, 200);
        using var b = Images.Crop(a, SKRectI.Create(0, 0, 250, 200));

        var r = ImageComparer.Compare(a, b, NoAlign);

        Assert.Empty(r.Regions);
        Assert.Equal(50 * 200, r.OnlyInA);
        Assert.Equal(0, r.OnlyInB);
        Assert.False(r.IsIdentical);
        Assert.Equal(SKRectI.Create(0, 0, 250, 200), r.Overlap);
    }

    [Fact]
    public void Manual_offset_is_used_as_given()
    {
        using var scene = Images.Page(700, 500);
        using var a = Images.Crop(scene, SKRectI.Create(40, 40, 600, 400));
        using var b = Images.Crop(scene, SKRectI.Create(30, 50, 600, 400));

        var r = ImageComparer.Compare(a, b, new DiffOptions { Align = AlignMode.Manual, ManualOffset = new SKPointI(-10, 10) });

        Assert.Equal(new SKPointI(-10, 10), r.OffsetB);
        Assert.Empty(r.Regions);
        Assert.Equal("Chỉnh tay: B lệch (-10, 10) px", r.Stats.AlignNote);
    }

    [Fact]
    public void Cancelled_token_stops_the_comparison()
    {
        using var a = Images.Page(400, 300);
        using var b = Images.Copy(a);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ImageComparer.Compare(a, b, new DiffOptions(), cts.Token));
    }

    [Fact]
    public void Normalize_converts_any_format_to_bgra_premul()
    {
        using var gray = new SKBitmap(new SKImageInfo(10, 10, SKColorType.Gray8, SKAlphaType.Opaque));
        gray.Erase(SKColors.White);

        using var n = ImageUtil.Normalize(gray);

        Assert.Equal(ImageUtil.ColorType, n.ColorType);
        Assert.Equal(ImageUtil.AlphaType, n.AlphaType);
        Assert.Equal(SKColors.White, n.GetPixel(5, 5));
        Assert.Equal(255, ImageUtil.Luma(0xFFFFFFFF), 2);
        Assert.Equal(0, ImageUtil.Luma(0xFF000000), 2);
    }
}
