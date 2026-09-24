using SkiaSharp;

namespace ScreenCapture.Models;

public enum StampKind
{
    /// <summary>Số tự tăng dần mỗi lần đặt stamp mới (1, 2, 3...), không phải số cố định - đúng hành
    /// vi "Number Stamps" của PicPick: bảng chọn chỉ chọn MÀU, số do <see cref="NumberValue"/> quyết
    /// định lúc đặt lên canvas (xem EditorWindow._numberStampCounter).</summary>
    Number,
    Check, Cross, Star,
    ArrowUp, ArrowDown, ArrowLeft, ArrowRight,
    ArrowUpLeft, ArrowUpRight, ArrowDownLeft, ArrowDownRight,
    Bookmark, Pin, Flag, Tag,
    Info, Warning, NoEntry, Heart, Plus, Minus,
}

/// <summary>A stamp placed at a fixed size (see EditorWindow) at the click point. Number stamps draw
/// a filled circle + digit directly with Skia (no font glyph needed); General stamps draw a Segoe
/// Fluent Icons glyph - same font WinUI's FontIcon uses, so it matches the toolbar/flyout preview
/// pixel-for-pixel. Diagonal arrows reuse the Up-arrow glyph rotated, since Segoe Fluent Icons has no
/// dedicated diagonal-arrow glyphs.</summary>
public sealed class StampAnnotation : AnnotationShape
{
    public StampKind Kind { get; init; }
    public override string DisplayName => "Stamp";

    /// <summary>Chỉ dùng khi Kind == Number - giá trị số hiển thị, gán từ bộ đếm tự tăng lúc đặt
    /// stamp (xem EditorWindow._numberStampCounter), không phải cố định theo Kind. Sửa được sau khi
    /// đặt qua ô "Current" ở tab ribbon contextual "Number Stamp" (giống PicPick).</summary>
    public int NumberValue { get; set; }

    /// <summary>Màu viền hình tròn (PicPick gọi là "Outline", tách biệt với "Fill" = <see cref="AnnotationShape.Color"/>).
    /// Mặc định trắng đục, đủ dày để thấy rõ trên cả nền sáng lẫn tối.</summary>
    public SKColor OutlineColor { get; set; } = SKColors.White;

    private static readonly Dictionary<StampKind, string> Glyph = new()
    {
        [StampKind.Check] = "",
        [StampKind.Cross] = "",
        [StampKind.Star] = "",
        [StampKind.ArrowUp] = "",
        [StampKind.ArrowDown] = "",
        [StampKind.ArrowLeft] = "",
        [StampKind.ArrowRight] = "",
        [StampKind.ArrowUpLeft] = "",
        [StampKind.ArrowUpRight] = "",
        [StampKind.ArrowDownLeft] = "",
        [StampKind.ArrowDownRight] = "",
        [StampKind.Bookmark] = "",
        [StampKind.Pin] = "",
        [StampKind.Flag] = "",
        [StampKind.Tag] = "",
        [StampKind.Info] = "",
        [StampKind.Warning] = "",
        [StampKind.NoEntry] = "",
        [StampKind.Heart] = "",
        [StampKind.Plus] = "",
        [StampKind.Minus] = "",
    };

    private static readonly Dictionary<StampKind, float> GlyphRotationDegrees = new()
    {
        [StampKind.ArrowUpRight] = 45,
        [StampKind.ArrowDownRight] = 135,
        [StampKind.ArrowDownLeft] = 225,
        [StampKind.ArrowUpLeft] = 315,
    };

    // Typeface tra cứu tốn kém - cache tĩnh, không tạo mới mỗi lần Render() (được gọi mỗi lần
    // Canvas.Invalidate, có thể vài chục lần/giây khi đang kéo shape khác).
    private static readonly SKTypeface NumberTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
    private static readonly SKTypeface IconTypeface =
        SKTypeface.FromFamilyName("Segoe Fluent Icons") ?? SKTypeface.FromFamilyName("Segoe MDL2 Assets");

    public override void Render(SKCanvas canvas)
    {
        if (Kind == StampKind.Number)
        {
            RenderNumber(canvas, NumberValue);
        }
        else if (Glyph.TryGetValue(Kind, out var glyph))
        {
            RenderGlyph(canvas, glyph);
        }
    }

    private void RenderNumber(SKCanvas canvas, int number)
    {
        float radius = Math.Min(Bounds.Width, Bounds.Height) / 2f;
        var center = new SKPoint(Bounds.MidX, Bounds.MidY);

        // Đổ bóng nhẹ + viền trắng cho giống style "flat + shadow" của PicPick thay vì hình
        // tròn phẳng lì như trước.
        using var circlePaint = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateDropShadow(0, radius * 0.12f, radius * 0.18f, radius * 0.18f,
                new SKColor(0, 0, 0, 90)),
        };
        canvas.DrawCircle(center, radius, circlePaint);

        using var rimPaint = new SKPaint
        {
            Color = OutlineColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.5f, radius * 0.12f),
            IsAntialias = true,
        };
        canvas.DrawCircle(center, radius - rimPaint.StrokeWidth / 2, rimPaint);

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = radius * 1.05f,
            TextAlign = SKTextAlign.Center,
            Typeface = NumberTypeface,
        };
        var text = number.ToString();
        float textY = center.Y - (textPaint.FontMetrics.Ascent + textPaint.FontMetrics.Descent) / 2f;
        canvas.DrawText(text, center.X, textY, textPaint);
    }

    private void RenderGlyph(SKCanvas canvas, string glyph)
    {
        float size = Math.Min(Bounds.Width, Bounds.Height);
        var center = new SKPoint(Bounds.MidX, Bounds.MidY);

        using var paint = new SKPaint
        {
            Color = Color,
            IsAntialias = true,
            TextSize = size,
            TextAlign = SKTextAlign.Center,
            Typeface = IconTypeface,
        };

        canvas.Save();
        if (GlyphRotationDegrees.TryGetValue(Kind, out var degrees))
        {
            canvas.RotateDegrees(degrees, center.X, center.Y);
        }
        float textY = center.Y - (paint.FontMetrics.Ascent + paint.FontMetrics.Descent) / 2f;
        canvas.DrawText(glyph, center.X, textY, paint);
        canvas.Restore();
    }
}
