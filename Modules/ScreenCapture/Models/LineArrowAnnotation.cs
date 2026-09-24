using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>A straight line, optionally with an arrowhead at the end point (Bounds.Right/Bottom).
/// Start is Bounds.Left/Top, end is Bounds.Right/Bottom - reuses Bounds instead of a separate
/// Start/End pair so move/resize commands can treat every shape uniformly.</summary>
public sealed class LineArrowAnnotation : AnnotationShape
{
    public override string DisplayName => "Mũi tên";
    public bool IsArrow { get; set; } = true;

    public override void Render(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
        };

        var start = new SKPoint(Bounds.Left, Bounds.Top);
        var end = new SKPoint(Bounds.Right, Bounds.Bottom);
        canvas.DrawLine(start, end, paint);

        if (!IsArrow)
        {
            return;
        }

        const float headLength = 14f;
        const float headAngle = 0.5f; // radians
        float angle = MathF.Atan2(end.Y - start.Y, end.X - start.X);
        var left = new SKPoint(
            end.X - headLength * MathF.Cos(angle - headAngle),
            end.Y - headLength * MathF.Sin(angle - headAngle));
        var right = new SKPoint(
            end.X - headLength * MathF.Cos(angle + headAngle),
            end.Y - headLength * MathF.Sin(angle + headAngle));

        using var fillPaint = new SKPaint { Color = Color, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(end);
        path.LineTo(left);
        path.LineTo(right);
        path.Close();
        canvas.DrawPath(path, fillPaint);
    }
}
