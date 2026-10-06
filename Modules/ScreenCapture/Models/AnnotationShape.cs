using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Kiểu nét của hình / đường / bút.</summary>
public enum LineDash
{
    Solid,
    Dash,
    Dot,
}

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

    /// <summary>Màu tô nền (Chữ nhật / Elip / Khung chú thích), null = không tô. Shape khác bỏ qua.</summary>
    public SKColor? FillColor { get; set; }

    /// <summary>Kiểu nét (Chữ nhật / Elip / Đường / Mũi tên / Bút / Khung chú thích). Shape khác bỏ qua.</summary>
    public LineDash Dash { get; set; } = LineDash.Solid;

    /// <summary>Độ đục của cả shape (0.1 - 1). Áp ở <see cref="Draw"/> bằng 1 lớp vẽ riêng, nên chỗ nét đè lên nền
    /// của chính shape không bị đậm hơn.</summary>
    public float Opacity { get; set; } = 1f;

    public abstract string DisplayName { get; }
    public abstract void Render(SKCanvas canvas);

    /// <summary>Vẽ lên trên <paramref name="baseImage"/> (ảnh nền của tab, cùng hệ toạ độ). Chỉ shape cần
    /// đọc pixel ảnh nền (<see cref="RedactAnnotation"/>) override; mọi chỗ vẽ shape lên ảnh gọi bản này.</summary>
    public virtual void Render(SKCanvas canvas, SKBitmap baseImage) => Render(canvas);

    /// <summary>Vẽ shape kèm <see cref="Opacity"/> - mọi chỗ vẽ shape (màn hình, lưu, copy, Flatten) gọi hàm này.</summary>
    public void Draw(SKCanvas canvas, SKBitmap baseImage)
    {
        if (Opacity >= 1f)
        {
            Render(canvas, baseImage);
            return;
        }
        using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(Opacity, 0f, 1f) * 255)) };
        canvas.SaveLayer(layer);
        Render(canvas, baseImage);
        canvas.Restore();
    }

    /// <summary>Hiệu ứng nét đứt / chấm theo <see cref="Dash"/>, độ dài gạch tỉ lệ theo độ dày nét. Nét đầu tròn
    /// (<paramref name="roundCap"/>) tự dài thêm nửa nét mỗi đầu → chấm dùng gạch gần 0.</summary>
    protected SKPathEffect? CreateDashEffect(bool roundCap)
    {
        float w = Math.Max(1f, StrokeWidth);
        return Dash switch
        {
            LineDash.Dash => roundCap ? SKPathEffect.CreateDash([w * 2.5f, w * 2.5f], 0) : SKPathEffect.CreateDash([w * 3, w * 2], 0),
            LineDash.Dot => roundCap ? SKPathEffect.CreateDash([0.01f, w * 2], 0) : SKPathEffect.CreateDash([w, w], 0),
            _ => null,
        };
    }

    /// <summary>Chép mọi thuộc tính (màu, nét, chữ...) trừ <see cref="Bounds"/> từ shape cùng loại. Dùng để lưu / khôi phục
    /// trạng thái cho Undo (<see cref="Snapshot"/>) và tạo bản sao khi xoay / lật ảnh.</summary>
    public virtual void CopyPropertiesFrom(AnnotationShape source)
    {
        Color = source.Color;
        StrokeWidth = source.StrokeWidth;
        FillColor = source.FillColor;
        Dash = source.Dash;
        Opacity = source.Opacity;
    }

    /// <summary>Bản chụp trạng thái hiện tại (khung + thuộc tính) để Undo - chỉ đọc lại qua <see cref="RestoreFrom"/>,
    /// không vẽ / thêm vào ảnh.</summary>
    public AnnotationShape Snapshot() => (AnnotationShape)MemberwiseClone();

    public void RestoreFrom(AnnotationShape snapshot)
    {
        Bounds = snapshot.Bounds;
        CopyPropertiesFrom(snapshot);
    }
}
