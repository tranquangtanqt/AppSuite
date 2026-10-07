using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Cạnh được xé của hiệu ứng Mép rách.</summary>
[Flags]
public enum TornSides
{
    None = 0,
    Top = 1,
    Right = 2,
    Bottom = 4,
    Left = 8,
}

public enum WatermarkPosition
{
    Center,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    /// <summary>Lặp kín cả ảnh theo đường chéo.</summary>
    Tile,
}

/// <summary>Thiết lập Watermark: chữ (<see cref="Text"/>) hoặc ảnh (<see cref="Image"/> khác null thì dùng ảnh).</summary>
public sealed record WatermarkSettings(
    string Text, string FontFamily, float FontSize, bool Bold, SKColor Color,
    SKBitmap? Image, float ImageScale, float Opacity, WatermarkPosition Position);

/// <summary>Kết quả 1 hiệu ứng: ảnh mới + độ dời của nội dung cũ trong ảnh mới (viền / bóng nới ảnh ra → các hình đã vẽ
/// dời theo để vẫn nằm đúng chỗ trên ảnh).</summary>
public sealed record EffectResult(SKBitmap Bitmap, SKPointI Offset);

/// <summary>Hiệu ứng ảnh (Đợt 2 so với PicPick): viền, đổ bóng, mép rách, độ sáng / tương phản, làm xám, sepia, đảo màu,
/// làm nét, watermark. Mọi hàm tạo ảnh MỚI (ảnh cũ giữ nguyên cho Undo), cùng định dạng pixel với ảnh chụp.
/// <c>scale</c>: tỉ lệ ảnh đưa vào so với ảnh thật - hộp thoại xem trước dùng ảnh thu nhỏ, mọi độ dài (px) nhân theo để
/// ảnh xem trước giống hệt kết quả.</summary>
public static class ImageEffects
{
    private static SKBitmap NewBitmap(int width, int height) =>
        new(new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKColorType.Bgra8888, SKAlphaType.Premul));

    // ---- Viền, đổ bóng, mép rách (đổi cỡ / hình dạng ảnh) ----

    /// <summary>Viền màu <paramref name="color"/> dày <paramref name="width"/> px bao quanh ảnh (ảnh to thêm 2 × width).
    /// Phần trong suốt của ảnh vẫn trong suốt (viền chỉ ở ngoài).</summary>
    public static EffectResult Border(SKBitmap source, float width, SKColor color, float scale = 1)
    {
        int b = Math.Max(1, (int)Math.Round(width * scale));
        var result = NewBitmap(source.Width + 2 * b, source.Height + 2 * b);
        using var canvas = new SKCanvas(result);
        canvas.Clear(color);
        using (var clear = new SKPaint { BlendMode = SKBlendMode.Clear })
        {
            canvas.DrawRect(SKRect.Create(b, b, source.Width, source.Height), clear);
        }
        canvas.DrawBitmap(source, b, b);
        return new EffectResult(result, new SKPointI(b, b));
    }

    /// <summary>Bóng đổ mờ về phía dưới-phải theo đúng hình dạng ảnh (kể cả mép rách). Ảnh nới thêm lề trong suốt cho
    /// bóng - lưu PNG giữ được nền trong suốt, JPG / BMP thì nền trắng.</summary>
    /// <param name="size">Độ lan của bóng (px).</param>
    /// <param name="opacity">Độ đậm của bóng 0-1.</param>
    public static EffectResult Shadow(SKBitmap source, float size, float opacity, float scale = 1)
    {
        float s = Math.Max(1f, size * scale);
        float sigma = s / 2;
        float d = s / 3; // bóng lệch xuống dưới-phải
        int near = (int)Math.Ceiling(Math.Max(0, s * 1.5f - d));
        int far = (int)Math.Ceiling(s * 1.5f + d);
        var result = NewBitmap(source.Width + near + far, source.Height + near + far);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        var shadowColor = SKColors.Black.WithAlpha((byte)Math.Round(Math.Clamp(opacity, 0f, 1f) * 255));
        using var filter = SKImageFilter.CreateDropShadow(d, d, sigma, sigma, shadowColor);
        using var paint = new SKPaint { ImageFilter = filter };
        canvas.DrawBitmap(source, near, near, paint);
        return new EffectResult(result, new SKPointI(near, near));
    }

    /// <summary>Xé răng cưa ngẫu nhiên các cạnh <paramref name="sides"/> (sâu tối đa <paramref name="depth"/> px), phần
    /// bị xé thành trong suốt; cỡ ảnh giữ nguyên. Răng cưa sinh từ số ngẫu nhiên cố định → xem trước và kết quả giống
    /// nhau, làm lại cũng ra đúng hình đó.</summary>
    public static EffectResult TornEdge(SKBitmap source, TornSides sides, float depth, float scale = 1)
    {
        float w = source.Width, h = source.Height;
        float dep = Math.Clamp(depth * scale, 1f, Math.Min(w, h) / 3);
        var random = new Random(1234);
        using var path = new SKPath();
        // Góc trên-trái lùi vào trong nếu cạnh kề bị xé, không thì còn sót 1 góc nhọn nguyên vẹn.
        path.MoveTo(sides.HasFlag(TornSides.Left) ? dep / 2 : 0, sides.HasFlag(TornSides.Top) ? dep / 2 : 0);

        // Đi vòng theo chiều kim đồng hồ: trên → phải → dưới → trái. Mỗi răng rộng ~1-2 lần độ sâu.
        void Edge(bool torn, SKPoint from, SKPoint to, SKPoint inward)
        {
            if (!torn)
            {
                path.LineTo(to);
                return;
            }
            float length = SKPoint.Distance(from, to);
            var dir = new SKPoint((to.X - from.X) / length, (to.Y - from.Y) / length);
            float t = 0;
            while (t < length)
            {
                t = Math.Min(length, t + dep * (0.6f + (float)random.NextDouble() * 1.2f));
                float inset = dep * (0.15f + (float)random.NextDouble() * 0.85f);
                path.LineTo(from.X + dir.X * t + inward.X * inset, from.Y + dir.Y * t + inward.Y * inset);
            }
        }

        Edge(sides.HasFlag(TornSides.Top), new SKPoint(0, 0), new SKPoint(w, 0), new SKPoint(0, 1));
        Edge(sides.HasFlag(TornSides.Right), new SKPoint(w, 0), new SKPoint(w, h), new SKPoint(-1, 0));
        Edge(sides.HasFlag(TornSides.Bottom), new SKPoint(w, h), new SKPoint(0, h), new SKPoint(0, -1));
        Edge(sides.HasFlag(TornSides.Left), new SKPoint(0, h), new SKPoint(0, 0), new SKPoint(1, 0));
        path.Close();

        var result = NewBitmap(source.Width, source.Height);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
        canvas.DrawBitmap(source, 0, 0);
        return new EffectResult(result, new SKPointI(0, 0));
    }

    // ---- Màu (giữ cỡ ảnh) ----

    /// <param name="brightness">-100 (tối hẳn) … 0 … 100 (sáng hẳn).</param>
    /// <param name="contrast">-100 (xám phẳng) … 0 … 100 (tương phản gấp 3).</param>
    public static SKBitmap BrightnessContrast(SKBitmap source, float brightness, float contrast)
    {
        float c = contrast >= 0 ? 1 + contrast / 50f : 1 + contrast / 100f;
        // Ma trận màu của Skia: phần dịch (cột 5) theo thang 0-1. Tương phản quanh mức xám giữa 0.5, rồi cộng độ sáng.
        float offset = 0.5f - 0.5f * c + brightness / 100f;
        return ApplyMatrix(source,
        [
            c, 0, 0, 0, offset,
            0, c, 0, 0, offset,
            0, 0, c, 0, offset,
            0, 0, 0, 1, 0,
        ]);
    }

    public static SKBitmap Grayscale(SKBitmap source) => ApplyMatrix(source,
    [
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0, 0, 0, 1, 0,
    ]);

    public static SKBitmap Sepia(SKBitmap source) => ApplyMatrix(source,
    [
        0.393f, 0.769f, 0.189f, 0, 0,
        0.349f, 0.686f, 0.168f, 0, 0,
        0.272f, 0.534f, 0.131f, 0, 0,
        0, 0, 0, 1, 0,
    ]);

    public static SKBitmap Invert(SKBitmap source) => ApplyMatrix(source,
    [
        -1, 0, 0, 0, 1,
        0, -1, 0, 0, 1,
        0, 0, -1, 0, 1,
        0, 0, 0, 1, 0,
    ]);

    private static SKBitmap ApplyMatrix(SKBitmap source, float[] matrix)
    {
        var result = NewBitmap(source.Width, source.Height);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        using var filter = SKColorFilter.CreateColorMatrix(matrix);
        using var paint = new SKPaint { ColorFilter = filter };
        canvas.DrawBitmap(source, 0, 0, paint);
        return result;
    }

    /// <summary>Làm nét (lọc 3×3 nhấn mạnh chênh lệch với 4 điểm bên cạnh). <paramref name="amount"/> ~0.5 = vừa.</summary>
    public static SKBitmap Sharpen(SKBitmap source, float amount = 0.5f)
    {
        float a = amount;
        float[] kernel = [0, -a, 0, -a, 1 + 4 * a, -a, 0, -a, 0];
        var result = NewBitmap(source.Width, source.Height);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        using var filter = SKImageFilter.CreateMatrixConvolution(new SKSizeI(3, 3), kernel, 1f, 0f, new SKPointI(1, 1),
            SKShaderTileMode.Clamp, false);
        using var paint = new SKPaint { ImageFilter = filter };
        canvas.DrawBitmap(source, 0, 0, paint);
        return result;
    }

    // ---- Watermark ----

    /// <summary>Đóng dấu chữ / ảnh mờ lên ảnh (ở 1 góc, giữa, hoặc lặp kín ảnh theo đường chéo).</summary>
    public static SKBitmap Watermark(SKBitmap source, WatermarkSettings settings, float scale = 1)
    {
        var result = source.Copy();
        using var canvas = new SKCanvas(result);
        float margin = 16 * scale;

        // Vẽ 1 "con dấu" (chữ hoặc ảnh) vào khung (0, 0, size) - mọi vị trí dùng chung.
        SKSize size;
        Action<SKCanvas> drawStamp;
        if (settings.Image is { } image)
        {
            float k = Math.Max(0.01f, settings.ImageScale) * scale;
            size = new SKSize(image.Width * k, image.Height * k);
            drawStamp = c =>
            {
                using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
                c.DrawBitmap(image, SKRect.Create(size), paint);
            };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Text))
            {
                return result;
            }
            var layout = TextLayout.Create(settings.Text, new TextFont(settings.FontFamily, settings.FontSize * scale, settings.Bold, false));
            size = new SKSize(layout.Width, layout.Height);
            drawStamp = c =>
            {
                using var paint = new SKPaint { Color = settings.Color, IsAntialias = true };
                layout.Draw(c, 0, 0, paint);
            };
        }

        // SrcATop: chỉ in lên phần ảnh có nội dung - không in ra nền trong suốt quanh bóng đổ / mép rách.
        using var layer = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(settings.Opacity, 0f, 1f) * 255)),
            BlendMode = SKBlendMode.SrcATop,
        };
        canvas.SaveLayer(layer);
        if (settings.Position == WatermarkPosition.Tile)
        {
            // Xoay -30° quanh tâm ảnh rồi rải lưới con dấu phủ kín cả vùng đã xoay.
            float w = source.Width, h = source.Height;
            canvas.RotateDegrees(-30, w / 2, h / 2);
            float stepX = size.Width + Math.Max(40 * scale, size.Width * 0.6f);
            float stepY = size.Height + Math.Max(40 * scale, size.Height * 1.5f);
            float diagonal = MathF.Sqrt(w * w + h * h);
            int row = 0;
            for (float y = h / 2 - diagonal; y < h / 2 + diagonal; y += stepY, row++)
            {
                float shift = row % 2 == 0 ? 0 : stepX / 2; // hàng so le
                for (float x = w / 2 - diagonal + shift; x < w / 2 + diagonal; x += stepX)
                {
                    canvas.Save();
                    canvas.Translate(x, y);
                    drawStamp(canvas);
                    canvas.Restore();
                }
            }
        }
        else
        {
            float x = settings.Position switch
            {
                WatermarkPosition.TopLeft or WatermarkPosition.BottomLeft => margin,
                WatermarkPosition.TopRight or WatermarkPosition.BottomRight => source.Width - margin - size.Width,
                _ => (source.Width - size.Width) / 2,
            };
            float y = settings.Position switch
            {
                WatermarkPosition.TopLeft or WatermarkPosition.TopRight => margin,
                WatermarkPosition.BottomLeft or WatermarkPosition.BottomRight => source.Height - margin - size.Height,
                _ => (source.Height - size.Height) / 2,
            };
            canvas.Translate(x, y);
            drawStamp(canvas);
        }
        canvas.Restore();
        return result;
    }

    // ---- Nền trong suốt ----

    /// <summary>Ảnh có pixel nào không đục hẳn không (nền trong suốt, đã đổ bóng / xé mép...) - đổi khung / xoá vùng của
    /// ảnh đó tô trong suốt thay vì trắng.</summary>
    public static unsafe bool HasTransparency(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888 && bitmap.ColorType != SKColorType.Rgba8888)
        {
            return false;
        }
        byte* pixels = (byte*)bitmap.GetPixels();
        for (int y = 0; y < bitmap.Height; y++)
        {
            foreach (uint pixel in new ReadOnlySpan<uint>(pixels + (long)y * bitmap.RowBytes, bitmap.Width))
            {
                if (pixel >> 24 != 0xFF)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
