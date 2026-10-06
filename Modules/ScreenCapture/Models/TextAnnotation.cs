using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Shape có chữ gõ được ngay trên ảnh (Text, Khung chú thích) - dùng chung ô chọn phông / cỡ / đậm / nghiêng.</summary>
public interface ITextShape
{
    string Text { get; set; }
    string FontFamily { get; set; }
    float FontSize { get; set; }
    bool Bold { get; set; }
    bool Italic { get; set; }

    /// <summary>Đang gõ / sửa chữ trong ô nhập trên ảnh → không vẽ phần chữ (ô nhập đã hiện chữ đó).</summary>
    bool IsEditingText { get; set; }
}

/// <summary>Chữ trên ảnh: nhiều dòng (Enter), chọn phông / cỡ / đậm / nghiêng, nền ô chữ (<see cref="BackgroundColor"/>)
/// và viền quanh nét chữ (<see cref="OutlineColor"/>) cho dễ đọc trên ảnh nhiều màu. Màu chữ = <see cref="AnnotationShape.Color"/>.
///
/// <see cref="AnnotationShape.Bounds"/> luôn vừa khít chữ + lề (<see cref="FitBounds"/>) - kéo handle góc đổi cỡ chữ chứ
/// không kéo giãn khung.</summary>
public sealed class TextAnnotation : AnnotationShape, ITextShape
{
    public const string DefaultFontFamily = "Segoe UI";

    public override string DisplayName => "Text";
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = DefaultFontFamily;
    public float FontSize { get; set; } = 20f;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    /// <summary>Màu nền ô chữ, null = trong suốt.</summary>
    public SKColor? BackgroundColor { get; set; }
    /// <summary>Màu viền quanh nét chữ, null = không viền.</summary>
    public SKColor? OutlineColor { get; set; }
    public bool IsEditingText { get; set; }

    public TextFont Font => new(FontFamily, FontSize, Bold, Italic);

    /// <summary>Lề quanh chữ (cũng là chỗ cho nền ô chữ và viền chữ).</summary>
    public float Padding => FontSize * 0.25f;

    private float OutlineWidth => Math.Max(1f, FontSize / 12f);

    /// <summary>Khung vừa khít chữ, giữ nguyên góc trên-trái.</summary>
    public void FitBounds()
    {
        var layout = TextLayout.Create(Text, Font);
        var r = NormalizedBounds;
        Bounds = SKRect.Create(r.Left, r.Top, layout.Width + 2 * Padding, layout.Height + 2 * Padding);
    }

    public override void CopyPropertiesFrom(AnnotationShape source)
    {
        base.CopyPropertiesFrom(source);
        if (source is TextAnnotation text)
        {
            Text = text.Text;
            FontFamily = text.FontFamily;
            FontSize = text.FontSize;
            Bold = text.Bold;
            Italic = text.Italic;
            BackgroundColor = text.BackgroundColor;
            OutlineColor = text.OutlineColor;
        }
    }

    public override void Render(SKCanvas canvas)
    {
        if (IsEditingText)
        {
            return;
        }
        var r = NormalizedBounds;
        if (BackgroundColor is { } background)
        {
            using var backgroundPaint = new SKPaint { Color = background, Style = SKPaintStyle.Fill, IsAntialias = true };
            float radius = FontSize * 0.15f;
            canvas.DrawRoundRect(r, radius, radius, backgroundPaint);
        }

        var layout = TextLayout.Create(Text, Font);
        if (OutlineColor is { } outline)
        {
            using var outlinePaint = new SKPaint
            {
                Color = outline,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = OutlineWidth * 2,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            layout.Draw(canvas, r.Left + Padding, r.Top + Padding, outlinePaint);
        }
        using var paint = new SKPaint { Color = Color, IsAntialias = true };
        layout.Draw(canvas, r.Left + Padding, r.Top + Padding, paint);
    }
}
