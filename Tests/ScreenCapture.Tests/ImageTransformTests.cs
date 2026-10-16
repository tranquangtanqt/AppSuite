using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Tests;

public class ImageTransformTests
{
    [Fact]
    public void Rotate_right_swaps_size_and_moves_pixel()
    {
        using var source = TestSupport.Solid(4, 2, SKColors.White, (0, 0, SKColors.Red)); // chấm đỏ góc trên-trái
        var transform = ImageTransform.Create(ImageTransformKind.RotateRight, 4, 2);
        using var rotated = transform.Apply(source);
        Assert.Equal((2, 4), (rotated.Width, rotated.Height));
        Assert.Equal(SKColors.Red, rotated.GetPixel(1, 0)); // góc trên-trái → góc trên-phải
        Assert.Equal(new SKPoint(2, 0), transform.MapPoint(new SKPoint(0, 0)));
    }

    [Theory]
    [InlineData(ImageTransformKind.FlipHorizontal, 3, 0)]
    [InlineData(ImageTransformKind.FlipVertical, 0, 1)]
    [InlineData(ImageTransformKind.Rotate180, 3, 1)]
    public void Flip_and_180_keep_size(ImageTransformKind kind, int x, int y)
    {
        using var source = TestSupport.Solid(4, 2, SKColors.White, (0, 0, SKColors.Red));
        using var result = ImageTransform.Create(kind, 4, 2).Apply(source);
        Assert.Equal((4, 2), (result.Width, result.Height));
        Assert.Equal(SKColors.Red, result.GetPixel(x, y));
    }

    [Fact]
    public void Resize_scales_shape_bounds()
    {
        var transform = ImageTransform.CreateResize(100, 100, 200, 50);
        var shape = new RectangleAnnotation { Bounds = new SKRect(10, 10, 20, 20) };
        var moved = transform.Transform(shape)!;
        Assert.Equal(new SKRect(20, 5, 40, 10), moved.Bounds);
        Assert.Equal(new SKRect(10, 10, 20, 20), shape.Bounds); // shape cũ giữ nguyên cho Undo
        Assert.Throws<ArgumentException>(() => ImageTransform.Create(ImageTransformKind.Resize, 1, 1));
    }

    public static TheoryData<ImageTransformKind[]> Sequences() => new()
    {
        new[] { ImageTransformKind.RotateRight },
        new[] { ImageTransformKind.RotateLeft, ImageTransformKind.RotateLeft },
        new[] { ImageTransformKind.FlipHorizontal },
        new[] { ImageTransformKind.FlipVertical, ImageTransformKind.RotateRight },
        new[] { ImageTransformKind.RotateRight, ImageTransformKind.FlipHorizontal, ImageTransformKind.RotateRight, ImageTransformKind.FlipVertical },
        new[] { ImageTransformKind.Rotate180, ImageTransformKind.FlipHorizontal, ImageTransformKind.RotateLeft },
    };

    /// <summary>Về hướng ban đầu: áp các bước UndoSteps lên ảnh đã xoay / lật thì ra đúng ảnh gốc (tối đa 2 bước).</summary>
    [Theory]
    [MemberData(nameof(Sequences))]
    public void Undo_steps_bring_image_back_to_original(ImageTransformKind[] steps)
    {
        using var original = TestSupport.Solid(5, 3, SKColors.White, (1, 0, SKColors.Blue));
        var bitmap = original.Copy();
        var orientation = ImageOrientation.Original;
        foreach (var step in steps)
        {
            bitmap = ImageTransform.Create(step, bitmap.Width, bitmap.Height).Apply(bitmap);
            orientation = orientation.Then(step);
        }
        var undo = orientation.UndoSteps();
        Assert.InRange(undo.Count, 0, 2);
        foreach (var step in undo)
        {
            bitmap = ImageTransform.Create(step, bitmap.Width, bitmap.Height).Apply(bitmap);
            orientation = orientation.Then(step);
        }
        Assert.True(orientation.IsOriginal);
        Assert.Equal((5, 3), (bitmap.Width, bitmap.Height));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(1, 0));
    }
}
