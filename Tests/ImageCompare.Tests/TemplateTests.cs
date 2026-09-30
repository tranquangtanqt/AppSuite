using ImageCompare.Engine;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>Tìm ảnh con (1 nút / icon cắt ra) trong ảnh lớn.</summary>
public class TemplateTests
{
    [Fact]
    public void Finds_a_cut_out_piece_at_its_exact_position()
    {
        using var image = Images.Page(800, 600, seed: 7);
        var at = SKRectI.Create(123, 77, 90, 40);
        using var piece = Images.Crop(image, at);

        var r = TemplateMatcher.Find(image, piece, 0.9);

        Assert.False(r.Swapped);
        var best = r.Matches[0];
        Assert.Equal(1, best.Number);
        Assert.Equal(at, best.Bounds);
        Assert.True(best.Score > 0.99, $"score {best.Score}");
    }

    [Fact]
    public void Smaller_image_as_A_is_swapped_automatically()
    {
        using var image = Images.Page(800, 600, seed: 7);
        var at = SKRectI.Create(400, 300, 80, 50);
        using var piece = Images.Crop(image, at);

        var r = TemplateMatcher.Find(piece, image, 0.9);

        Assert.True(r.Swapped);
        Assert.Equal(at, r.Matches[0].Bounds);
    }

    [Fact]
    public void Finds_every_copy_of_a_repeated_icon()
    {
        using var image = Images.New(600, 400, SKColors.White);
        using var icon = Images.Noise(24, 24, seed: 11);
        SKPointI[] places = [new(30, 40), new(300, 60), new(500, 330)];
        using (var canvas = new SKCanvas(image))
        {
            foreach (var p in places)
            {
                canvas.DrawBitmap(icon, p.X, p.Y);
            }
        }

        var r = TemplateMatcher.Find(image, icon, 0.9);

        Assert.Equal(places.Length, r.Matches.Count);
        Assert.Equal(places.OrderBy(p => p.X).ThenBy(p => p.Y),
            r.Matches.Select(m => new SKPointI(m.Bounds.Left, m.Bounds.Top)).OrderBy(p => p.X).ThenBy(p => p.Y));
    }

    [Fact]
    public void Piece_that_is_not_in_the_image_is_not_found()
    {
        using var image = Images.Page(600, 400, seed: 1);
        using var piece = Images.Noise(60, 40, seed: 99);

        var r = TemplateMatcher.Find(image, piece, 0.9);

        Assert.Empty(r.Matches);
    }

    [Fact]
    public void Tiny_template_returns_nothing_instead_of_throwing()
    {
        using var image = Images.Page(200, 200);
        using var piece = Images.New(1, 1, SKColors.Black);

        Assert.Empty(TemplateMatcher.Find(image, piece, 0.9).Matches);
    }
}
