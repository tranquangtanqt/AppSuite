using SkiaSharp;

namespace ScreenCapture.Models;

public sealed class RectangleAnnotation : AnnotationShape
{
    public override string DisplayName => "Hình chữ nhật";
    public float StrokeWidth { get; set; } = 3f;

    public override void Render(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        canvas.DrawRect(Bounds, paint);
    }
}
