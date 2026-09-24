using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Semi-transparent filled marker (giống bút dạ quang) - không viền, chỉ tô đè 1 lớp màu
/// mờ lên vùng Bounds.</summary>
public sealed class HighlightAnnotation : AnnotationShape
{
    public override string DisplayName => "Highlight";

    public HighlightAnnotation()
    {
        Color = new SKColor(255, 235, 59, 90); // vàng, alpha thấp - mặc định nếu không set Color2
    }

    public override void Render(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        canvas.DrawRect(Bounds, paint);
    }
}
