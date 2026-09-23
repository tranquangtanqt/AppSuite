using SkiaSharp;

namespace ScreenCapture.Models;

public sealed class TextAnnotation : AnnotationShape
{
    public override string DisplayName => "Text";
    public string Text { get; set; } = string.Empty;
    public float FontSize { get; set; } = 20f;

    public override void Render(SKCanvas canvas)
    {
        using var font = new SKFont(SKTypeface.Default, FontSize);
        using var paint = new SKPaint { Color = Color, IsAntialias = true };
        // Baseline sits FontSize below the top of Bounds so the text visually starts at Bounds.Top.
        canvas.DrawText(Text, Bounds.Left, Bounds.Top + FontSize, font, paint);
    }
}
