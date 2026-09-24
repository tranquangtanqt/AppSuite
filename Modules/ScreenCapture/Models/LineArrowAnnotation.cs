using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>A straight line, optionally with an arrowhead at the end point (Bounds.Right/Bottom).
/// Start is Bounds.Left/Top, end is Bounds.Right/Bottom - reuses Bounds instead of a separate
/// Start/End pair so move/resize commands can treat every shape uniformly. Bounds KHÔNG được chuẩn
/// hoá (có thể Left &gt; Right...), nếu không mũi tên luôn bị lật về hướng xuống-phải.</summary>
public sealed class LineArrowAnnotation : AnnotationShape
{
    public override string DisplayName => "Mũi tên";
    public bool IsArrow { get; set; } = true;

    public SKPoint Start => new(Bounds.Left, Bounds.Top);
    public SKPoint End => new(Bounds.Right, Bounds.Bottom);
    public float Length => SKPoint.Distance(Start, End);

    public static SKRect FromPoints(SKPoint start, SKPoint end) => new(start.X, start.Y, end.X, end.Y);

    /// <summary>Hit-test theo khoảng cách tới đoạn thẳng thay vì cả khung bao - với đường chéo, khung
    /// bao chiếm cả vùng trống lớn và che mất các shape nằm dưới.</summary>
    public override bool HitTest(SKPoint point, float tolerance) =>
        DistanceToSegment(point) <= tolerance + StrokeWidth / 2;

    private float DistanceToSegment(SKPoint p)
    {
        var d = End - Start;
        float lengthSquared = d.X * d.X + d.Y * d.Y;
        if (lengthSquared == 0)
        {
            return SKPoint.Distance(p, Start);
        }
        float t = Math.Clamp(((p.X - Start.X) * d.X + (p.Y - Start.Y) * d.Y) / lengthSquared, 0f, 1f);
        return SKPoint.Distance(p, new SKPoint(Start.X + t * d.X, Start.Y + t * d.Y));
    }

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
