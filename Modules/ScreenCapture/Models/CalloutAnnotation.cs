using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Khung chú thích / bong bóng lời nói (như PicPick): khung bo góc có chữ bên trong (tự xuống dòng theo bề rộng
/// khung) và 1 "đuôi" nhọn chỉ vào chỗ cần chú thích. Viền + chữ màu <see cref="AnnotationShape.Color"/>, nền
/// <see cref="AnnotationShape.FillColor"/>.
///
/// Đầu đuôi lưu theo độ lệch so với tâm khung (<see cref="TailOffset"/>) → di chuyển khung (chỉ đổi Bounds) thì đuôi đi
/// theo, mọi lệnh dời / Cắt ảnh dùng được chung như các shape khác.</summary>
public sealed class CalloutAnnotation : AnnotationShape, ITextShape
{
    public override string DisplayName => "Khung chú thích";
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = TextAnnotation.DefaultFontFamily;
    public float FontSize { get; set; } = 16f;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public float CornerRadius { get; set; } = 10f;
    public SKPoint TailOffset { get; set; }
    public bool IsEditingText { get; set; }

    public TextFont Font => new(FontFamily, FontSize, Bold, Italic);

    public SKPoint Center => new(NormalizedBounds.MidX, NormalizedBounds.MidY);

    /// <summary>Đầu nhọn của đuôi (toạ độ ảnh).</summary>
    public SKPoint TailTip
    {
        get => Center + TailOffset;
        set => TailOffset = value - Center;
    }

    /// <summary>Lề giữa viền khung và chữ.</summary>
    public float Padding => Math.Max(6f, FontSize * 0.5f) + StrokeWidth / 2;

    /// <summary>Bề rộng dành cho chữ (chữ dài hơn tự xuống dòng).</summary>
    public float TextWidth => Math.Max(1f, NormalizedBounds.Width - 2 * Padding);

    /// <summary>Đuôi mặc định: chỉ xuống dưới, lệch trái - như khi vừa vẽ khung trong PicPick.</summary>
    public void ResetTail()
    {
        var r = NormalizedBounds;
        TailOffset = new SKPoint(-r.Width * 0.3f, r.Height / 2 + Math.Clamp(r.Height * 0.6f, 20f, 60f));
    }

    /// <summary>Kéo cao khung xuống dưới cho đủ chỗ chữ (giữ nguyên đầu đuôi).</summary>
    public void GrowToFitText()
    {
        var r = NormalizedBounds;
        float needed = TextLayout.Create(Text, Font, TextWidth).Height + 2 * Padding;
        if (needed > r.Height)
        {
            var tip = TailTip;
            Bounds = new SKRect(r.Left, r.Top, r.Right, r.Top + needed);
            TailTip = tip;
        }
    }

    public override void CopyPropertiesFrom(AnnotationShape source)
    {
        base.CopyPropertiesFrom(source);
        if (source is CalloutAnnotation callout)
        {
            Text = callout.Text;
            FontFamily = callout.FontFamily;
            FontSize = callout.FontSize;
            Bold = callout.Bold;
            Italic = callout.Italic;
            CornerRadius = callout.CornerRadius;
            TailOffset = callout.TailOffset;
        }
    }

    /// <summary>Trúng khi bấm vào trong khung / đuôi (khung có nền nên bấm đâu bên trong cũng chọn được) hoặc sát viền.</summary>
    public override bool HitTest(SKPoint point, float tolerance)
    {
        if (SKRect.Inflate(NormalizedBounds, tolerance + StrokeWidth / 2, tolerance + StrokeWidth / 2).Contains(point)
            || SKPoint.Distance(point, TailTip) <= tolerance + StrokeWidth) // mũi đuôi rất mảnh, bấm gần là trúng
        {
            return true;
        }
        using var outline = BuildOutline();
        return outline.Contains(point.X, point.Y);
    }

    /// <summary>Khung bo góc gộp với đuôi tam giác thành 1 đường viền liền. Đáy đuôi nằm lùi vào trong khung để phép gộp
    /// không để lại khe; đầu đuôi nằm trong khung thì không có đuôi.</summary>
    private SKPath BuildOutline()
    {
        var r = NormalizedBounds;
        float radius = Math.Min(CornerRadius, Math.Min(r.Width, r.Height) / 2);
        var body = new SKPath();
        body.AddRoundRect(r, radius, radius);

        var tip = TailTip;
        if (r.Width < 2 || r.Height < 2 || r.Contains(tip))
        {
            return body;
        }

        var c = Center;
        float dx = tip.X - c.X, dy = tip.Y - c.Y;
        float half = Math.Clamp(Math.Min(r.Width, r.Height) * 0.15f, 6f, 30f);
        float inset = Math.Min(r.Width, r.Height) / 4;
        SKPoint a, b;
        if (Math.Abs(dy) * r.Width >= Math.Abs(dx) * r.Height)
        {
            // Đuôi đi ra cạnh trên / dưới: đáy đuôi trượt theo phía đầu đuôi nhưng không lấn vào góc bo.
            float y = dy > 0 ? r.Bottom - inset : r.Top + inset;
            float x = ClampOrMid((c.X + tip.X) / 2, r.Left + radius + half, r.Right - radius - half);
            a = new SKPoint(x - half, y);
            b = new SKPoint(x + half, y);
        }
        else
        {
            float x = dx > 0 ? r.Right - inset : r.Left + inset;
            float y = ClampOrMid((c.Y + tip.Y) / 2, r.Top + radius + half, r.Bottom - radius - half);
            a = new SKPoint(x, y - half);
            b = new SKPoint(x, y + half);
        }

        using var tail = new SKPath();
        tail.MoveTo(a);
        tail.LineTo(tip);
        tail.LineTo(b);
        tail.Close();
        var merged = body.Op(tail, SKPathOp.Union);
        if (merged is null)
        {
            return body;
        }
        body.Dispose();
        return merged;
    }

    private static float ClampOrMid(float value, float min, float max) => min <= max ? Math.Clamp(value, min, max) : (min + max) / 2;

    public override void Render(SKCanvas canvas)
    {
        using var outline = BuildOutline();
        if (FillColor is { } fill)
        {
            using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(outline, fillPaint);
        }
        if (StrokeWidth > 0)
        {
            using var dash = CreateDashEffect(roundCap: false);
            using var strokePaint = new SKPaint
            {
                Color = Color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = StrokeWidth,
                StrokeJoin = SKStrokeJoin.Round,
                PathEffect = dash,
                IsAntialias = true,
            };
            canvas.DrawPath(outline, strokePaint);
        }

        if (IsEditingText || Text.Length == 0)
        {
            return;
        }
        var r = NormalizedBounds;
        var layout = TextLayout.Create(Text, Font, TextWidth);
        using var textPaint = new SKPaint { Color = Color, IsAntialias = true };
        canvas.Save();
        canvas.ClipRect(SKRect.Inflate(r, -StrokeWidth / 2, -StrokeWidth / 2));
        layout.Draw(canvas, r.Left + Padding, r.Top + Padding, textPaint);
        canvas.Restore();
    }
}
