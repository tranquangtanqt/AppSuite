using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Độ giống về cấu trúc (SSIM) - khác "% pixel giống": chịu được nén JPG, đổi độ sáng nhẹ, khử răng
/// cưa khác nhau; 1 = giống hệt, &gt; 0.95 thường là "nhìn như nhau".</summary>
public static class Similarity
{
    private const int Window = 8;
    private const double C1 = (0.01 * 255) * (0.01 * 255), C2 = (0.03 * 255) * (0.03 * 255);

    /// <summary>SSIM trung bình trên các ô 8 × 8 của ảnh xám phần chồng nhau, thu nhỏ để cạnh dài ≤ ~1024 px
    /// (ảnh dài 30.000 px vẫn nhanh).</summary>
    public static double Ssim(SKBitmap a, SKBitmap b, SKPointI offset, SKRectI overlap, CancellationToken ct)
    {
        int factor = Math.Max(1, (int)Math.Ceiling(Math.Max(overlap.Width, overlap.Height) / 1024.0));
        var ga = ImageUtil.ToGray(a, overlap, factor);
        var areaB = new SKRectI(overlap.Left - offset.X, overlap.Top - offset.Y, overlap.Right - offset.X, overlap.Bottom - offset.Y);
        var gb = ImageUtil.ToGray(b, areaB, factor);
        ct.ThrowIfCancellationRequested();

        int w = Math.Min(ga.Width, gb.Width), h = Math.Min(ga.Height, gb.Height);
        if (w < Window || h < Window)
        {
            return MeanSsimWindow(ga, gb, 0, 0, w, h);
        }
        double total = 0;
        int windows = 0;
        for (int y = 0; y + Window <= h; y += Window)
        {
            for (int x = 0; x + Window <= w; x += Window)
            {
                total += MeanSsimWindow(ga, gb, x, y, Window, Window);
                windows++;
            }
        }
        return total / windows;
    }

    private static double MeanSsimWindow(GrayImage a, GrayImage b, int x0, int y0, int w, int h)
    {
        int n = w * h;
        if (n == 0)
        {
            return 0;
        }
        double ma = 0, mb = 0;
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                ma += a[x, y];
                mb += b[x, y];
            }
        }
        ma /= n;
        mb /= n;
        double va = 0, vb = 0, cov = 0;
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                double da = a[x, y] - ma, db = b[x, y] - mb;
                va += da * da;
                vb += db * db;
                cov += da * db;
            }
        }
        va /= n;
        vb /= n;
        cov /= n;
        return (2 * ma * mb + C1) * (2 * cov + C2) / ((ma * ma + mb * mb + C1) * (va + vb + C2));
    }
}
