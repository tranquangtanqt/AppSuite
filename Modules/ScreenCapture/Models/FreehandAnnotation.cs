using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Nét bút vẽ tự do (khoanh tròn, gạch chân tay...). Các điểm lưu theo toạ độ lúc vẽ cùng khung bao
/// lúc đó (<see cref="PointsBounds"/>); khi vẽ, điểm được ánh xạ từ khung đó sang <see cref="AnnotationShape.Bounds"/>
/// hiện tại → di chuyển / co giãn qua handle / Cắt ảnh (dịch Bounds) dùng lại được đường chung, không cần
/// command riêng sửa từng điểm.</summary>
public sealed class FreehandAnnotation : AnnotationShape
{
    private readonly List<SKPoint> _points = [];

    public override string DisplayName => "Bút";

    public IReadOnlyList<SKPoint> Points => _points;

    /// <summary>Khung bao của <see cref="Points"/> (toạ độ lúc vẽ).</summary>
    public SKRect PointsBounds { get; private set; }

    /// <summary>Thêm điểm khi đang vẽ - Bounds bám theo khung bao của nét.</summary>
    public void AddPoint(SKPoint point)
    {
        if (_points.Count > 0 && SKPoint.Distance(_points[^1], point) < 1.5f)
        {
            return; // bỏ điểm quá sát, nét nhẹ hơn mà không khác gì khi nhìn
        }
        _points.Add(point);
        PointsBounds = _points.Count == 1
            ? new SKRect(point.X, point.Y, point.X, point.Y)
            : SKRect.Union(PointsBounds, new SKRect(point.X, point.Y, point.X, point.Y));
        Bounds = PointsBounds;
    }

    /// <summary>Nạp lại nét đã lưu (phiên làm việc): điểm theo toạ độ của Bounds hiện tại.</summary>
    public void SetPoints(IEnumerable<SKPoint> points)
    {
        var bounds = Bounds;
        _points.Clear();
        foreach (var p in points)
        {
            AddPoint(p);
        }
        Bounds = bounds;
    }

    /// <summary>Các điểm đã ánh xạ vào Bounds hiện tại (đúng vị trí đang thấy trên ảnh).</summary>
    public IEnumerable<SKPoint> CurrentPoints()
    {
        var from = PointsBounds;
        var to = NormalizedBounds;
        // Nét thẳng tuyệt đối (ngang/dọc) có khung rộng/cao 0 → giữ nguyên chiều đó, chỉ dịch.
        float sx = from.Width > 0 ? to.Width / from.Width : 1, sy = from.Height > 0 ? to.Height / from.Height : 1;
        foreach (var p in _points)
        {
            yield return new SKPoint(to.Left + (p.X - from.Left) * sx, to.Top + (p.Y - from.Top) * sy);
        }
    }

    public override bool HitTest(SKPoint point, float tolerance)
    {
        float limit = tolerance + StrokeWidth / 2;
        SKPoint? previous = null;
        foreach (var p in CurrentPoints())
        {
            if (previous is { } a && DistanceToSegment(point, a, p) <= limit)
            {
                return true;
            }
            if (previous is null && SKPoint.Distance(point, p) <= limit)
            {
                return true;
            }
            previous = p;
        }
        return false;
    }

    public override void Render(SKCanvas canvas)
    {
        var points = CurrentPoints().ToList();
        if (points.Count == 0)
        {
            return;
        }
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };
        if (points.Count == 1)
        {
            canvas.DrawPoint(points[0], paint); // 1 cú click = 1 chấm tròn
            return;
        }
        // Đi qua trung điểm các đoạn bằng đường cong bậc 2 → nét mềm, không gãy góc theo từng sự kiện chuột.
        using var path = new SKPath();
        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count - 1; i++)
        {
            var mid = new SKPoint((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
            path.QuadTo(points[i], mid);
        }
        path.LineTo(points[^1]);
        canvas.DrawPath(path, paint);
    }

    private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        var ab = b - a;
        float lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
        float t = lengthSquared == 0 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSquared, 0, 1);
        return SKPoint.Distance(p, new SKPoint(a.X + ab.X * t, a.Y + ab.Y * t));
    }
}
