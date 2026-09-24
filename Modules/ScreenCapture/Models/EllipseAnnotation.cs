using SkiaSharp;

namespace ScreenCapture.Models;

public sealed class EllipseAnnotation : AnnotationShape
{
    public override string DisplayName => "Hình elip";
    public override void Render(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        canvas.DrawOval(Bounds, paint);
    }
}
