using SkiaSharp;

namespace ScreenCapture.Models;

public enum RedactMode
{
    /// <summary>Ô vuông pixel (mỗi ô = màu trung bình của vùng ảnh bên dưới).</summary>
    Mosaic,
    /// <summary>Làm mờ Gauss.</summary>
    Blur,
}

/// <summary>Che thông tin nhạy cảm (mật khẩu, email, số tài khoản...) trong vùng Bounds bằng Mosaic hoặc
/// làm mờ. Là 1 shape như các shape khác (di chuyển / co giãn / xoá / Undo được) - chỉ khi Lưu / Copy /
/// Flatten ảnh mới thật sự bị che.
///
/// Khác các shape khác: phải đọc pixel ẢNH NỀN ở vùng đó nên vẽ qua <see cref="Render(SKCanvas, SKBitmap)"/>.
/// Chỉ che ảnh nền, không che các shape nằm dưới nó (nội dung cần che luôn nằm trong ảnh chụp).
/// Mức độ lấy từ <see cref="AnnotationShape.StrokeWidth"/> (thanh trượt Size 1–20) để panel chỉnh shape
/// đã chọn, Undo và lưu phiên dùng lại được đường có sẵn.</summary>
public sealed class RedactAnnotation : AnnotationShape
{
    public RedactMode Mode { get; init; }

    public override string DisplayName => Mode == RedactMode.Mosaic ? "Mosaic" : "Làm mờ";

    /// <summary>Cạnh ô mosaic (px ảnh): Size 1 → 6px, 3 (mặc định) → 10px, 20 → 44px. Ô dưới ~6px
    /// không đủ che chữ cỡ thường.</summary>
    private int BlockSize => (int)MathF.Round(4 + Math.Max(1, StrokeWidth) * 2);

    /// <summary>Sigma làm mờ: Size 1 → 4, 3 → 6, 20 → 23. Sigma nhỏ hơn vẫn có thể đọc được chữ.</summary>
    private float Sigma => 3 + Math.Max(1, StrokeWidth);

    // Ảnh đã xử lý của vùng - tính lại chỉ khi ảnh nền / vùng / chế độ / mức độ đổi, không phải mỗi
    // lần vẽ lại canvas (kéo chuột, hover...).
    private SKBitmap? _cache;
    private (SKBitmap Base, SKRectI Rect, float Strength) _cacheKey;

    /// <summary>Không có ảnh nền → tô xám đặc (vẫn che kín, không lộ nội dung).</summary>
    public override void Render(SKCanvas canvas)
    {
        using var paint = new SKPaint { Color = new SKColor(128, 128, 128), Style = SKPaintStyle.Fill };
        canvas.DrawRect(NormalizedBounds, paint);
    }

    public override void Render(SKCanvas canvas, SKBitmap baseImage)
    {
        var rect = SKRectI.Intersect(SKRectI.Round(NormalizedBounds), SKRectI.Create(baseImage.Width, baseImage.Height));
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }
        var key = (baseImage, rect, StrokeWidth);
        if (_cache is null || _cacheKey != key)
        {
            _cache?.Dispose();
            _cache = Mode == RedactMode.Mosaic ? Pixelate(baseImage, rect, BlockSize) : Blur(baseImage, rect, Sigma);
            _cacheKey = key;
        }
        // Mosaic: _cache là ảnh thu nhỏ (1 pixel = 1 ô) → phóng to không nội suy thành các ô vuông sắc cạnh.
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.None };
        canvas.DrawBitmap(_cache, SKRect.Create(_cache.Width, _cache.Height), (SKRect)rect, paint);
    }

    /// <summary>Thu nhỏ vùng về 1 pixel/ô (lọc Medium = lấy trung bình qua mipmap).</summary>
    private static SKBitmap Pixelate(SKBitmap source, SKRectI rect, int block)
    {
        int w = Math.Max(1, (rect.Width + block - 1) / block), h = Math.Max(1, (rect.Height + block - 1) / block);
        var small = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(small);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium };
        canvas.DrawBitmap(source, (SKRect)rect, SKRect.Create(w, h), paint);
        return small;
    }

    /// <summary>Làm mờ vùng, lấy thêm 1 viền pixel thật xung quanh (3 sigma) để mép vùng mờ đều như
    /// phần giữa thay vì nhạt dần vào trong suốt.</summary>
    private static SKBitmap Blur(SKBitmap source, SKRectI rect, float sigma)
    {
        int pad = (int)MathF.Ceiling(sigma * 3);
        var padded = SKRectI.Intersect(SKRectI.Inflate(rect, pad, pad), SKRectI.Create(source.Width, source.Height));
        var result = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { ImageFilter = filter };
        canvas.Translate(-rect.Left, -rect.Top);
        canvas.ClipRect(SKRect.Create(padded.Left, padded.Top, padded.Width, padded.Height));
        using var subset = new SKBitmap();
        source.ExtractSubset(subset, padded);
        canvas.DrawBitmap(subset, padded.Left, padded.Top, paint);
        return result;
    }
}
