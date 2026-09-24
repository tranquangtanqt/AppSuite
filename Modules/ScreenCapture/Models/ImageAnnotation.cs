using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Ảnh dán từ clipboard (Ctrl+V) - là 1 shape như các shape khác nên di chuyển / co giãn qua
/// handle góc / đổi lớp / Flatten / Undo được. Vẽ ảnh co giãn vừa khít Bounds.</summary>
public sealed class ImageAnnotation : AnnotationShape
{
    public ImageAnnotation(SKBitmap image)
    {
        Image = image;
    }

    public SKBitmap Image { get; }
    public override string DisplayName => "Ảnh dán";

    /// <summary>Tỉ lệ rộng/cao gốc của ảnh - dùng khi giữ Shift lúc co giãn để không bị méo.</summary>
    public float AspectRatio => (float)Image.Width / Image.Height;

    public override void Render(SKCanvas canvas)
    {
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.DrawBitmap(Image, NormalizedBounds, paint);
    }
}
