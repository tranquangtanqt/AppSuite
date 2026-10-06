using SkiaSharp;

namespace ScreenCapture.Models;

public sealed class RectangleAnnotation : AnnotationShape
{
    public override string DisplayName => "Hình chữ nhật";

    /// <summary>Bán kính bo góc (0 = góc vuông), tự giới hạn theo nửa cạnh ngắn.</summary>
    public float CornerRadius { get; set; }

    public override void CopyPropertiesFrom(AnnotationShape source)
    {
        base.CopyPropertiesFrom(source);
        if (source is RectangleAnnotation rectangle)
        {
            CornerRadius = rectangle.CornerRadius;
        }
    }

    /// <summary>Hình rỗng ruột chỉ trúng khi bấm lên viền - bấm vào giữa khung vẫn vẽ được shape mới bên trong thay vì bị
    /// chọn khung. Hình có tô nền thì bấm đâu bên trong cũng trúng.</summary>
    public override bool HitTest(SKPoint point, float tolerance)
    {
        float t = tolerance + StrokeWidth / 2;
        var r = NormalizedBounds;
        var outer = SKRect.Inflate(r, t, t);
        if (FillColor is not null)
        {
            return outer.Contains(point);
        }
        var inner = SKRect.Inflate(r, -t, -t);
        return outer.Contains(point) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(point));
    }

    public override void Render(SKCanvas canvas)
    {
        var r = NormalizedBounds;
        float radius = Math.Min(CornerRadius, Math.Min(r.Width, r.Height) / 2);
        if (FillColor is { } fill)
        {
            using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(r, radius, radius, fillPaint);
        }
        using var dash = CreateDashEffect(roundCap: false);
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth,
            Style = SKPaintStyle.Stroke,
            PathEffect = dash,
            IsAntialias = true,
        };
        canvas.DrawRoundRect(r, radius, radius, paint);
    }
}
