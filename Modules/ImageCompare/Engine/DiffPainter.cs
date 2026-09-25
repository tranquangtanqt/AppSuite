using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Vẽ "ảnh khác biệt" theo toạ độ ảnh A - dùng chung cho màn hình (canvas đã zoom) và ảnh xuất PNG /
/// báo cáo (1:1) để 2 nơi luôn giống nhau: ảnh B nhạt màu (nền tham chiếu), pixel khác tô đỏ, khung + số vùng,
/// phần chỉ có ở A / chỉ có ở B tô sọc.</summary>
public sealed class DiffPainter : IDiffView
{
    public static readonly SKColor DiffColor = new(0xE5, 0x1A, 0x1A);
    public static readonly SKColor OnlyAColor = new(0x2B, 0x7B, 0xD6);
    public static readonly SKColor OnlyBColor = new(0xD8, 0x64, 0x45);

    private readonly SKBitmap _a, _b;
    private readonly DiffResult _result;
    private readonly string? _note;
    private SKBitmap? _maskOverlay;

    /// <param name="note">Ghi chú thay cho mô tả căn chỉnh mặc định (vd lui về tự căn khi căn theo dòng không được).</param>
    public DiffPainter(SKBitmap a, SKBitmap b, DiffResult result, string? note = null)
    {
        _a = a;
        _b = b;
        _result = result;
        _note = note;
    }

    public DiffResult Result => _result;
    public DiffStats Stats => _note is null ? _result.Stats : _result.Stats with { AlignNote = $"{_note} (B lệch ({_result.OffsetB.X}, {_result.OffsetB.Y}) px)" };
    public IReadOnlyList<DiffRegion> Regions => _result.Regions;

    public (SKRectI? InA, SKRectI? InB) SourceRects(DiffRegion region) =>
        (region.Bounds, new SKRectI(region.Bounds.Left - _result.OffsetB.X, region.Bounds.Top - _result.OffsetB.Y,
            region.Bounds.Right - _result.OffsetB.X, region.Bounds.Bottom - _result.OffsetB.Y));

    /// <summary>Khung bao toàn bộ (A ∪ B) theo toạ độ ảnh A.</summary>
    public SKRectI Bounds => SKRectI.Union(SKRectI.Create(_a.Width, _a.Height),
        SKRectI.Create(_result.OffsetB.X, _result.OffsetB.Y, _b.Width, _b.Height));

    /// <param name="pixelSize">Kích thước 1 pixel màn hình theo đơn vị ảnh (1 / zoom) - nét khung, cỡ chữ số
    /// vùng giữ nguyên cỡ trên màn hình dù zoom bao nhiêu. Xuất ảnh 1:1 thì = 1.</param>
    /// <param name="highlight">Vùng đang chọn (vẽ đậm hơn), 0 = không có.</param>
    public void Draw(SKCanvas canvas, float pixelSize, SKFilterQuality quality, int highlight = 0)
    {
        var rectB = SKRect.Create(_result.OffsetB.X, _result.OffsetB.Y, _b.Width, _b.Height);
        // Nền: ảnh B rửa trắng 65% để màu đỏ nổi bật mà vẫn nhận ra nội dung.
        using (var image = new SKPaint { FilterQuality = quality })
        {
            canvas.DrawBitmap(_b, rectB, image);
        }
        using (var wash = new SKPaint { Color = SKColors.White.WithAlpha(165) })
        {
            canvas.DrawRect(rectB, wash);
        }

        DrawOnlyIn(canvas, pixelSize);
        DrawIgnoreRects(canvas, _result.IgnoreRects, pixelSize);

        if (!_result.Overlap.IsEmpty && _result.DiffPixels > 0)
        {
            LazyInitializer.EnsureInitialized(ref _maskOverlay, BuildMaskOverlay); // màn hình và xuất báo cáo (nền) có thể gọi cùng lúc
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.None };
            canvas.DrawBitmap(_maskOverlay, (SKRect)_result.Overlap, paint);
        }

        DrawRegionBoxes(canvas, _result.Regions, pixelSize, highlight, Bounds.Top);
    }

    public static SKColor ColorOf(RegionKind kind) => kind switch
    {
        RegionKind.OnlyInA => OnlyAColor,
        RegionKind.OnlyInB => OnlyBColor,
        _ => DiffColor,
    };

    /// <summary>Khung + nhãn số cho từng vùng (màu theo loại vùng), vùng đang chọn viền vàng đậm.</summary>
    public static void DrawRegionBoxes(SKCanvas canvas, IEnumerable<DiffRegion> regions, float pixelSize, int highlight, int boundsTop)
    {
        using var box = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 2 * pixelSize, IsAntialias = true };
        using var boxStrong = new SKPaint { Color = new SKColor(0xFF, 0xC1, 0x07), Style = SKPaintStyle.Stroke, StrokeWidth = 3 * pixelSize, IsAntialias = true };
        using var label = new SKPaint { IsAntialias = true };
        using var labelText = new SKPaint
        {
            Color = SKColors.White, IsAntialias = true, TextSize = 12 * pixelSize,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        };
        float pad = 4 * pixelSize;
        foreach (var region in regions)
        {
            box.Color = label.Color = ColorOf(region.Kind);
            var r = SKRect.Inflate(region.Bounds, pad, pad);
            canvas.DrawRect(r, region.Number == highlight ? boxStrong : box);
            string text = region.Number.ToString();
            float tw = labelText.MeasureText(text);
            var tag = SKRect.Create(r.Left, r.Top - 16 * pixelSize, tw + 8 * pixelSize, 16 * pixelSize);
            if (tag.Top < boundsTop)
            {
                tag.Offset(0, 16 * pixelSize); // sát mép trên ảnh → nhãn vào trong khung
            }
            canvas.DrawRect(tag, label);
            canvas.DrawText(text, tag.Left + 4 * pixelSize, tag.Bottom - 4 * pixelSize, labelText);
        }
    }

    /// <summary>Vùng bỏ qua: nền xám + viền nét đứt - trong đó không so pixel.</summary>
    public static void DrawIgnoreRects(SKCanvas canvas, IEnumerable<SKRectI> rects, float pixelSize)
    {
        using var fill = new SKPaint { Color = new SKColor(0x60, 0x60, 0x60, 70) };
        using var border = new SKPaint
        {
            Color = new SKColor(0x40, 0x40, 0x40), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * pixelSize, IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([6 * pixelSize, 4 * pixelSize], 0),
        };
        foreach (var r in rects)
        {
            canvas.DrawRect(r, fill);
            canvas.DrawRect(r, border);
        }
    }

    /// <summary>Phần chỉ có ở 1 ảnh (khác kích thước / bị dịch): sọc chéo màu của ảnh đó.</summary>
    private void DrawOnlyIn(SKCanvas canvas, float pixelSize)
    {
        var overlap = (SKRect)_result.Overlap;
        void Hatch(SKRect area, SKColor color)
        {
            canvas.Save();
            canvas.ClipRect(area);
            if (!overlap.IsEmpty)
            {
                canvas.ClipRect(overlap, SKClipOperation.Difference);
            }
            using var tint = new SKPaint { Color = color.WithAlpha(40) };
            canvas.DrawRect(area, tint);
            using var stripe = new SKPaint { Color = color.WithAlpha(110), StrokeWidth = 1.5f * pixelSize, IsAntialias = true };
            float step = 10 * pixelSize;
            for (float d = area.Left - area.Height; d < area.Right; d += step)
            {
                canvas.DrawLine(d, area.Bottom, d + area.Height, area.Top, stripe);
            }
            canvas.Restore();
        }
        if (_result.OnlyInA > 0)
        {
            Hatch(SKRect.Create(_a.Width, _a.Height), OnlyAColor);
        }
        if (_result.OnlyInB > 0)
        {
            Hatch(SKRect.Create(_result.OffsetB.X, _result.OffsetB.Y, _b.Width, _b.Height), OnlyBColor);
        }
    }

    private SKBitmap BuildMaskOverlay() => MaskToOverlay(_result.Mask, _result.Overlap.Width, _result.Overlap.Height);

    /// <summary>Mặt nạ khác biệt (1 byte / pixel) → ảnh trong suốt, pixel khác tô đỏ.</summary>
    public static unsafe SKBitmap MaskToOverlay(byte[] mask, int width, int height)
    {
        var bmp = new SKBitmap(new SKImageInfo(width, height, ImageUtil.ColorType, ImageUtil.AlphaType));
        uint* p = (uint*)bmp.GetPixels();
        int stride = bmp.RowBytes / 4;
        // Đỏ đậm alpha 220, premultiplied (B, G, R nhân alpha).
        const uint a = 220;
        uint red = (a << 24) | ((DiffColor.Red * a / 255) << 16) | ((DiffColor.Green * a / 255) << 8) | (DiffColor.Blue * a / 255);
        for (int y = 0; y < height; y++)
        {
            uint* row = p + (long)y * stride;
            int m = y * width;
            for (int x = 0; x < width; x++)
            {
                row[x] = mask[m + x] != 0 ? red : 0;
            }
        }
        return bmp;
    }

    /// <summary>Ảnh khác biệt 1:1 (khung A ∪ B) để Copy / Lưu PNG / nhúng báo cáo.</summary>
    public SKBitmap Render()
    {
        var bounds = Bounds;
        var bmp = new SKBitmap(new SKImageInfo(bounds.Width, bounds.Height, ImageUtil.ColorType, ImageUtil.AlphaType));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        canvas.Translate(-bounds.Left, -bounds.Top);
        Draw(canvas, 1, SKFilterQuality.None);
        return bmp;
    }

    public void Dispose() => _maskOverlay?.Dispose();
}
