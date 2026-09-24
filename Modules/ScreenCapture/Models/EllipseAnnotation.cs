using SkiaSharp;

namespace ScreenCapture.Models;

public sealed class EllipseAnnotation : AnnotationShape
{
    public override string DisplayName => "Hình elip";

    /// <summary>Chỉ trúng khi bấm lên viền elip (giống <see cref="RectangleAnnotation.HitTest"/>): nằm
    /// trong elip ngoài (nới thêm t) nhưng không nằm trong elip trong (thu lại t).</summary>
    public override bool HitTest(SKPoint point, float tolerance)
    {
        float t = tolerance + StrokeWidth / 2;
        var r = NormalizedBounds;
        float rx = r.Width / 2, ry = r.Height / 2;
        float dx = point.X - r.MidX, dy = point.Y - r.MidY;

        static float Norm(float dx, float dy, float rx, float ry) => dx * dx / (rx * rx) + dy * dy / (ry * ry);

        if (Norm(dx, dy, rx + t, ry + t) > 1)
        {
            return false;
        }
        return rx - t <= 0 || ry - t <= 0 || Norm(dx, dy, rx - t, ry - t) >= 1;
    }
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
