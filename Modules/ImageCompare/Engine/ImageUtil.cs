using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Tiện ích ảnh dùng chung cho Engine. Mọi ảnh vào Engine đều ở 1 định dạng pixel thống nhất
/// (BGRA 8-bit, alpha premultiplied) để các vòng lặp pixel đọc thẳng <c>uint</c> không cần rẽ nhánh.</summary>
public static class ImageUtil
{
    public const SKColorType ColorType = SKColorType.Bgra8888;
    public const SKAlphaType AlphaType = SKAlphaType.Premul;

    /// <summary>Bản sao ở định dạng chuẩn (PNG xám / RGBA / JPG... đều về BGRA premul). Trả về ảnh mới,
    /// không đụng tới ảnh gốc.</summary>
    public static SKBitmap Normalize(SKBitmap source)
    {
        var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, ColorType, AlphaType));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }

    /// <summary>Ảnh xám (độ sáng 0–255) của vùng <paramref name="area"/>, thu nhỏ <paramref name="factor"/> lần
    /// bằng lấy trung bình từng ô factor × factor.</summary>
    public static unsafe GrayImage ToGray(SKBitmap bitmap, SKRectI area, int factor)
    {
        factor = Math.Max(1, factor);
        int w = Math.Max(1, area.Width / factor), h = Math.Max(1, area.Height / factor);
        var data = new float[w * h];
        byte* basePtr = (byte*)bitmap.GetPixels();
        int rowBytes = bitmap.RowBytes;
        float norm = 1f / (factor * factor);
        for (int gy = 0; gy < h; gy++)
        {
            for (int fy = 0; fy < factor; fy++)
            {
                uint* row = (uint*)(basePtr + (long)(area.Top + gy * factor + fy) * rowBytes) + area.Left;
                for (int gx = 0; gx < w; gx++)
                {
                    float sum = 0;
                    uint* p = row + gx * factor;
                    for (int fx = 0; fx < factor; fx++)
                    {
                        sum += Luma(p[fx]);
                    }
                    data[gy * w + gx] += sum * norm;
                }
            }
        }
        return new GrayImage(data, w, h);
    }

    /// <summary>Độ sáng (Rec. 601) của 1 pixel BGRA - đủ cho căn chỉnh / SSIM, không cần chính xác màu.</summary>
    public static float Luma(uint bgra) =>
        0.114f * (bgra & 0xFF) + 0.587f * ((bgra >> 8) & 0xFF) + 0.299f * ((bgra >> 16) & 0xFF);
}

/// <summary>Ảnh xám dạng mảng float (row-major).</summary>
public sealed record GrayImage(float[] Data, int Width, int Height)
{
    public float this[int x, int y] => Data[y * Width + x];
}
