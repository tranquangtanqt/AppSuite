using ImageCompare.Engine;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>Dựng ảnh thử bằng SkiaSharp - luôn cùng 1 kết quả (Random có seed), không cần file ảnh mẫu.</summary>
internal static class Images
{
    /// <summary>"Trang" giả: nền trắng + các khối màu như dòng chữ / nút / ảnh - đủ cạnh để tự căn bám vào.</summary>
    public static SKBitmap Page(int width, int height, int seed = 1)
    {
        var bmp = New(width, height, SKColors.White);
        using var canvas = new SKCanvas(bmp);
        var rnd = new Random(seed);
        using var paint = new SKPaint { IsAntialias = false };
        for (int y = 12; y < height - 20; y += 18 + rnd.Next(10))
        {
            int x = 10 + rnd.Next(30);
            while (x < width - 40)
            {
                int w = 8 + rnd.Next(60);
                paint.Color = new SKColor((byte)rnd.Next(160), (byte)rnd.Next(160), (byte)rnd.Next(160));
                canvas.DrawRect(x, y, Math.Min(w, width - 20 - x), 6 + rnd.Next(8), paint);
                x += w + 4 + rnd.Next(20);
            }
        }
        return bmp;
    }

    /// <summary>Nhiễu ngẫu nhiên từng pixel - mỗi dòng khác mọi dòng khác.</summary>
    public static unsafe SKBitmap Noise(int width, int height, int seed)
    {
        var bmp = New(width, height, SKColors.Black);
        var rnd = new Random(seed);
        uint* p = (uint*)bmp.GetPixels();
        for (int i = 0; i < width * height; i++)
        {
            p[i] = 0xFF000000u | (uint)rnd.Next(0x1000000);
        }
        return bmp;
    }

    public static SKBitmap New(int width, int height, SKColor background)
    {
        var bmp = new SKBitmap(new SKImageInfo(width, height, ImageUtil.ColorType, ImageUtil.AlphaType));
        bmp.Erase(background);
        return bmp;
    }

    /// <summary>Cắt 1 vùng ra ảnh mới.</summary>
    public static SKBitmap Crop(SKBitmap source, SKRectI area)
    {
        var bmp = New(area.Width, area.Height, SKColors.Transparent);
        using var canvas = new SKCanvas(bmp);
        canvas.DrawBitmap(source, area, SKRect.Create(area.Width, area.Height));
        return bmp;
    }

    public static SKBitmap Copy(SKBitmap source) => Crop(source, SKRectI.Create(source.Width, source.Height));

    public static void Fill(SKBitmap bmp, SKRectI area, SKColor color)
    {
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = color, IsAntialias = false };
        canvas.DrawRect(area, paint);
    }

    /// <summary>Chèn <paramref name="band"/> vào giữa <paramref name="source"/> tại dòng <paramref name="atY"/>
    /// (phần dưới bị đẩy xuống).</summary>
    public static SKBitmap InsertRows(SKBitmap source, int atY, SKBitmap band)
    {
        var bmp = New(source.Width, source.Height + band.Height, SKColors.White);
        using var canvas = new SKCanvas(bmp);
        canvas.DrawBitmap(source, SKRectI.Create(0, 0, source.Width, atY), SKRect.Create(0, 0, source.Width, atY));
        canvas.DrawBitmap(band, 0, atY);
        int rest = source.Height - atY;
        canvas.DrawBitmap(source, SKRectI.Create(0, atY, source.Width, rest), SKRect.Create(0, atY + band.Height, source.Width, rest));
        return bmp;
    }

    /// <summary>Viết chữ (font hệ thống Segoe UI - có đủ dấu tiếng Việt) lên nền, mỗi phần tử 1 dòng.</summary>
    public static SKBitmap Text(IReadOnlyList<string> lines, SKColor background, SKColor foreground, float size = 30)
    {
        using var typeface = SKTypeface.FromFamilyName("Segoe UI");
        using var paint = new SKPaint { Color = foreground, IsAntialias = true, TextSize = size, Typeface = typeface };
        int lineHeight = (int)(size * 1.8);
        int width = 40 + (int)lines.Max(l => paint.MeasureText(l));
        var bmp = New(width, 30 + lineHeight * lines.Count, background);
        using var canvas = new SKCanvas(bmp);
        for (int i = 0; i < lines.Count; i++)
        {
            canvas.DrawText(lines[i], 20, 15 + lineHeight * i + size, paint);
        }
        return bmp;
    }
}
