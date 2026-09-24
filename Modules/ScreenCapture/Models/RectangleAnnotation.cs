using SkiaSharp;

namespace ScreenCapture.Models;

public sealed class RectangleAnnotation : AnnotationShape
{
    public override string DisplayName => "Hình chữ nhật";

    /// <summary>Chỉ trúng khi bấm lên viền (hình rỗng ruột) - bấm vào giữa khung vẫn vẽ được shape mới
    /// bên trong thay vì bị chọn khung.</summary>
    public override bool HitTest(SKPoint point, float tolerance)
    {
        float t = tolerance + StrokeWidth / 2;
        var r = NormalizedBounds;
        var outer = SKRect.Inflate(r, t, t);
        var inner = SKRect.Inflate(r, -t, -t);
        return outer.Contains(point) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(point));
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
        canvas.DrawRect(Bounds, paint);
    }
}
