using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Kiểu đầu của đường thẳng / mũi tên.</summary>
public enum ArrowHead
{
    None,
    /// <summary>Tam giác đặc.</summary>
    Arrow,
    /// <summary>Chữ V (2 nét, không tô).</summary>
    OpenArrow,
    /// <summary>Chấm tròn đặc.</summary>
    Circle,
}

/// <summary>A straight line with an optional head at each end (<see cref="StartHead"/> / <see cref="EndHead"/>).
/// Start is Bounds.Left/Top, end is Bounds.Right/Bottom - reuses Bounds instead of a separate
/// Start/End pair so move/resize commands can treat every shape uniformly. Bounds KHÔNG được chuẩn
/// hoá (có thể Left &gt; Right...), nếu không mũi tên luôn bị lật về hướng xuống-phải.</summary>
public sealed class LineArrowAnnotation : AnnotationShape
{
    public override string DisplayName => StartHead == ArrowHead.None && EndHead == ArrowHead.None ? "Đường thẳng" : "Mũi tên";
    public ArrowHead StartHead { get; set; } = ArrowHead.None;
    public ArrowHead EndHead { get; set; } = ArrowHead.Arrow;

    public SKPoint Start => new(Bounds.Left, Bounds.Top);
    public SKPoint End => new(Bounds.Right, Bounds.Bottom);
    public float Length => SKPoint.Distance(Start, End);

    public static SKRect FromPoints(SKPoint start, SKPoint end) => new(start.X, start.Y, end.X, end.Y);

    /// <summary>Đầu mũi tên to theo độ dày nét (nét mảnh vẫn giữ tối thiểu 14px như trước).</summary>
    private float HeadLength => Math.Max(14f, StrokeWidth * 4f);
    private const float HeadAngle = 0.5f; // radians

    public override void CopyPropertiesFrom(AnnotationShape source)
    {
        base.CopyPropertiesFrom(source);
        if (source is LineArrowAnnotation line)
        {
            StartHead = line.StartHead;
            EndHead = line.EndHead;
        }
    }

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
        var start = Start;
        var end = End;
        float length = Length;
        using var dash = CreateDashEffect(roundCap: true);
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            PathEffect = dash,
        };
        if (length < 0.5f)
        {
            canvas.DrawLine(start, end, paint);
            return;
        }

        // Đầu tam giác: rút đường lại tới giữa đầu mũi tên, nét dày không chọc ra khỏi mũi nhọn.
        var direction = new SKPoint((end.X - start.X) / length, (end.Y - start.Y) / length);
        float pullBack = Math.Min(HeadLength * 0.6f, length / 2);
        var lineStart = StartHead == ArrowHead.Arrow ? start + Scale(direction, pullBack) : start;
        var lineEnd = EndHead == ArrowHead.Arrow ? end - Scale(direction, pullBack) : end;
        canvas.DrawLine(lineStart, lineEnd, paint);

        DrawHead(canvas, start, new SKPoint(-direction.X, -direction.Y), StartHead);
        DrawHead(canvas, end, direction, EndHead);
    }

    private static SKPoint Scale(SKPoint p, float factor) => new(p.X * factor, p.Y * factor);

    /// <summary>Vẽ 1 đầu tại <paramref name="tip"/>, <paramref name="direction"/> = hướng (đã chuẩn hoá) đi ra phía đầu.</summary>
    private void DrawHead(SKCanvas canvas, SKPoint tip, SKPoint direction, ArrowHead head)
    {
        if (head == ArrowHead.None)
        {
            return;
        }
        if (head == ArrowHead.Circle)
        {
            using var dotPaint = new SKPaint { Color = Color, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawCircle(tip, Math.Max(4f, StrokeWidth * 1.6f), dotPaint);
            return;
        }

        float angle = MathF.Atan2(direction.Y, direction.X);
        var left = new SKPoint(
            tip.X - HeadLength * MathF.Cos(angle - HeadAngle),
            tip.Y - HeadLength * MathF.Sin(angle - HeadAngle));
        var right = new SKPoint(
            tip.X - HeadLength * MathF.Cos(angle + HeadAngle),
            tip.Y - HeadLength * MathF.Sin(angle + HeadAngle));

        if (head == ArrowHead.OpenArrow)
        {
            using var strokePaint = new SKPaint
            {
                Color = Color,
                StrokeWidth = StrokeWidth,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            using var open = new SKPath();
            open.MoveTo(left);
            open.LineTo(tip);
            open.LineTo(right);
            canvas.DrawPath(open, strokePaint);
            return;
        }

        using var fillPaint = new SKPaint { Color = Color, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(tip);
        path.LineTo(left);
        path.LineTo(right);
        path.Close();
        canvas.DrawPath(path, fillPaint);
    }
}
