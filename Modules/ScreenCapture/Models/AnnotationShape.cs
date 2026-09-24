using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>One drawn annotation on top of the captured bitmap. Subclasses own their own SkiaSharp
/// draw call, keeping EditorWindow/EditorViewModel shape-agnostic.</summary>
public abstract class AnnotationShape
{
    /// <summary>Với đa số shape luôn là rect chuẩn hoá (Left&lt;=Right, Top&lt;=Bottom). Riêng
    /// <see cref="LineArrowAnnotation"/> lưu điểm đầu/cuối nên có thể "ngược" - code cần khung thật
    /// (vẽ vùng chọn, cắt ảnh...) dùng <see cref="NormalizedBounds"/>.</summary>
    public SKRect Bounds { get; set; }
    public SKRect NormalizedBounds => Bounds.Standardized;

    /// <summary>Click tại <paramref name="point"/> có trúng shape không (dùng cho công cụ Di chuyển).</summary>
    public virtual bool HitTest(SKPoint point, float tolerance)
    {
        var r = NormalizedBounds;
        return point.X >= r.Left - tolerance && point.X <= r.Right + tolerance
            && point.Y >= r.Top - tolerance && point.Y <= r.Bottom + tolerance;
    }

    public SKColor Color { get; set; } = new(0xE7, 0x4C, 0x3C);

    /// <summary>Không phải shape nào cũng dùng (Text/Stamp/Highlight bỏ qua) nhưng đặt ở base để
    /// panel chỉnh sửa shape đã chọn (Size slider) áp dụng được đồng nhất cho mọi loại shape.</summary>
    public float StrokeWidth { get; set; } = 3f;

    public abstract string DisplayName { get; }
    public abstract void Render(SKCanvas canvas);
}
