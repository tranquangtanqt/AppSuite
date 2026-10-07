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
/// Mặc định khung tự vừa khít chữ (chỉ xuống dòng ở Enter). Kéo handle góc → khung cố định bề rộng
/// (<see cref="FixedWidth"/>, như PicPick): chữ tự xuống dòng theo bề rộng khung, cỡ chữ giữ nguyên, khung tự cao thêm
/// khi chữ không đủ chỗ (<see cref="FitBounds"/>). Giữ Ctrl khi kéo handle = đổi cỡ chữ.</summary>
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
    /// <summary>Khung do người dùng kéo: bề rộng giữ nguyên, chữ tự xuống dòng theo bề rộng đó. False = khung vừa khít chữ.</summary>
    public bool FixedWidth { get; set; }
    public bool IsEditingText { get; set; }

    public TextFont Font => new(FontFamily, FontSize, Bold, Italic);

    /// <summary>Lề quanh chữ (cũng là chỗ cho nền ô chữ và viền chữ).</summary>
    public float Padding => FontSize * 0.25f;

    private float OutlineWidth => Math.Max(1f, FontSize / 12f);

    /// <summary>Bề rộng tối thiểu của khung cố định: vừa 1 chữ.</summary>
    public float MinBoxWidth => FontSize + 2 * Padding;

    /// <summary>Bề rộng dành cho chữ (vô hạn khi khung tự vừa khít).</summary>
    private float WrapWidth => FixedWidth ? Math.Max(1f, NormalizedBounds.Width - 2 * Padding) : float.PositiveInfinity;

    private TextLayout CreateLayout() => TextLayout.Create(Text, Font, WrapWidth);

    /// <summary>Khung tự vừa khít: vừa khít chữ. Khung cố định: giữ bề rộng, cao thêm nếu chữ không đủ chỗ (không tự thấp
    /// lại - người dùng kéo khung cao hơn chữ để có nền rộng thì giữ nguyên). Luôn giữ góc trên-trái.</summary>
    public void FitBounds()
    {
        var layout = CreateLayout();
        var r = NormalizedBounds;
        float neededHeight = layout.Height + 2 * Padding;
        Bounds = FixedWidth
            ? SKRect.Create(r.Left, r.Top, Math.Max(r.Width, MinBoxWidth), Math.Max(r.Height, neededHeight))
            : SKRect.Create(r.Left, r.Top, layout.Width + 2 * Padding, neededHeight);
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
            FixedWidth = text.FixedWidth;
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

        var layout = CreateLayout();
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
