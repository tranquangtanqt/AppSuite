using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>One drawn annotation on top of the captured bitmap. Subclasses own their own SkiaSharp
/// draw call, keeping EditorWindow/EditorViewModel shape-agnostic.</summary>
public abstract class AnnotationShape
{
    public SKRect Bounds { get; set; }
    public SKColor Color { get; set; } = new(0xE7, 0x4C, 0x3C);

    /// <summary>Không phải shape nào cũng dùng (Text/Stamp/Highlight bỏ qua) nhưng đặt ở base để
    /// panel chỉnh sửa shape đã chọn (Size slider) áp dụng được đồng nhất cho mọi loại shape.</summary>
    public float StrokeWidth { get; set; } = 3f;

    public abstract string DisplayName { get; }
    public abstract void Render(SKCanvas canvas);
}
