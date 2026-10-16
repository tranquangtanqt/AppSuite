using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Tests;

public class ImageEffectsTests
{
    [Fact]
    public void Grayscale_makes_channels_equal_and_invert_flips()
    {
        using var source = TestSupport.Solid(3, 3, new SKColor(200, 40, 90));
        using var gray = ImageEffects.Grayscale(source);
        var g = gray.GetPixel(1, 1);
        Assert.InRange(Math.Abs(g.Red - g.Green), 0, 1);
        Assert.InRange(Math.Abs(g.Green - g.Blue), 0, 1);

        using var inverted = ImageEffects.Invert(source);
        var i = inverted.GetPixel(1, 1);
        Assert.Equal((55, 215, 165), (i.Red, i.Green, i.Blue));
    }

    [Fact]
    public void Border_grows_image_and_paints_edge()
    {
        using var source = TestSupport.Solid(10, 10, SKColors.White);
        var result = ImageEffects.Border(source, 2, SKColors.Black);
        Assert.Equal((14, 14), (result.Bitmap.Width, result.Bitmap.Height));
        Assert.Equal(SKColors.Black, result.Bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.White, result.Bitmap.GetPixel(7, 7));
    }

    [Fact]
    public void Shadow_adds_transparent_margin()
    {
        using var source = TestSupport.Solid(20, 20, SKColors.White);
        Assert.False(ImageEffects.HasTransparency(source));
        var result = ImageEffects.Shadow(source, 8, 0.5f);
        Assert.True(result.Bitmap.Width > 20 && result.Bitmap.Height > 20);
        Assert.True(ImageEffects.HasTransparency(result.Bitmap));
    }
}
