using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Tìm độ dịch (dx, dy) đặt ảnh B lên ảnh A cho khớp nhất (B(x - dx, y - dy) ↔ A(x, y)).
///
/// Cách làm (thô → tinh để ảnh dài vẫn nhanh):
/// 1. Ảnh xám thu nhỏ (cạnh ngắn ~300 px). Ước lượng dy và dx riêng rẽ bằng tương quan 1 chiều của
///    "hồ sơ cạnh": tổng độ chênh sáng giữa 2 pixel kề nhau theo từng dòng / cột - nội dung dịch bao nhiêu
///    thì hồ sơ dịch bấy nhiêu, không phụ thuộc chiều còn lại.
/// 2. Dò 2 chiều ±2 ô quanh ước lượng (và quanh (0, 0)) bằng sai lệch tuyệt đối trung bình (MAD).
/// 3. Tinh chỉnh ở độ phân giải gốc ±(hệ số thu nhỏ + 1) px. Cuối cùng nếu (0, 0) khớp gần bằng thì giữ
///    (0, 0) - 2 ảnh vốn thẳng hàng không bị "căn" lệch vì nhiễu.</summary>
public static class Aligner
{
    private const double MaxShiftRatio = 0.25; // dò tối đa 25% kích thước (+ phần chênh kích thước 2 ảnh)
    private const double MinOverlapRatio = 0.3;

    public static SKPointI FindOffset(SKBitmap a, SKBitmap b, CancellationToken ct = default)
    {
        int shortSide = Math.Min(Math.Min(a.Width, a.Height), Math.Min(b.Width, b.Height));
        int factor = Math.Max(1, (int)Math.Round(shortSide / 300.0));
        var ga = ImageUtil.ToGray(a, SKRectI.Create(a.Width, a.Height), factor);
        var gb = ImageUtil.ToGray(b, SKRectI.Create(b.Width, b.Height), factor);
        ct.ThrowIfCancellationRequested();

        int rangeY = (int)(Math.Max(ga.Height, gb.Height) * MaxShiftRatio) + Math.Abs(ga.Height - gb.Height);
        int rangeX = (int)(Math.Max(ga.Width, gb.Width) * MaxShiftRatio) + Math.Abs(ga.Width - gb.Width);
        int dy = BestShift(RowEdgeProfile(ga), RowEdgeProfile(gb), rangeY);
        int dx = BestShift(ColumnEdgeProfile(ga), ColumnEdgeProfile(gb), rangeX);
        ct.ThrowIfCancellationRequested();

        // Dò 2 chiều quanh ước lượng và quanh (0, 0) ở ảnh thu nhỏ.
        var best = (X: 0, Y: 0);
        double bestScore = double.MaxValue;
        foreach (var (cx, cy) in new[] { (dx, dy), (0, 0) })
        {
            for (int oy = -2; oy <= 2; oy++)
            {
                for (int ox = -2; ox <= 2; ox++)
                {
                    double score = MeanAbsDiff(ga, gb, cx + ox, cy + oy, 1);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = (cx + ox, cy + oy);
                    }
                }
            }
        }
        ct.ThrowIfCancellationRequested();

        if (factor == 1)
        {
            return PreferZero(ga, gb, best, 1);
        }

        // Tinh chỉnh ở độ phân giải gốc, đọc thẳng pixel (không tạo ảnh xám cỡ gốc - ảnh 1400 × 30.000 sẽ tốn
        // ~170 MB), lấy mẫu thưa ~400k điểm.
        int step = Math.Max(1, (int)Math.Sqrt((double)a.Width * a.Height / 400_000));
        var fine = (X: best.X * factor, Y: best.Y * factor);
        double fineScore = double.MaxValue;
        var center = fine;
        for (int oy = -factor - 1; oy <= factor + 1; oy++)
        {
            for (int ox = -factor - 1; ox <= factor + 1; ox++)
            {
                double score = MeanAbsDiff(a, b, center.X + ox, center.Y + oy, step);
                if (score < fineScore)
                {
                    fineScore = score;
                    fine = (center.X + ox, center.Y + oy);
                }
            }
        }
        double atZero = MeanAbsDiff(a, b, 0, 0, step);
        return atZero <= fineScore + 0.05 ? SKPointI.Empty : new SKPointI(fine.X, fine.Y);
    }

    private static SKPointI PreferZero(GrayImage a, GrayImage b, (int X, int Y) best, int step)
    {
        double atBest = MeanAbsDiff(a, b, best.X, best.Y, step);
        double atZero = MeanAbsDiff(a, b, 0, 0, step);
        return atZero <= atBest + 0.05 ? SKPointI.Empty : new SKPointI(best.X, best.Y);
    }

    /// <summary>Như <see cref="MeanAbsDiff(GrayImage, GrayImage, int, int, int)"/> nhưng đọc thẳng độ sáng
    /// từ 2 bitmap gốc.</summary>
    private static unsafe double MeanAbsDiff(SKBitmap a, SKBitmap b, int dx, int dy, int step)
    {
        int x0 = Math.Max(0, dx), y0 = Math.Max(0, dy);
        int x1 = Math.Min(a.Width, b.Width + dx), y1 = Math.Min(a.Height, b.Height + dy);
        long area = (long)(x1 - x0) * (y1 - y0);
        long minArea = (long)(Math.Min((long)a.Width * a.Height, (long)b.Width * b.Height) * MinOverlapRatio);
        if (x1 <= x0 || y1 <= y0 || area < minArea)
        {
            return double.MaxValue;
        }
        byte* pa = (byte*)a.GetPixels(), pb = (byte*)b.GetPixels();
        int ra = a.RowBytes, rb = b.RowBytes;
        double sum = 0;
        long count = 0;
        for (int y = y0; y < y1; y += step)
        {
            uint* rowA = (uint*)(pa + (long)y * ra);
            uint* rowB = (uint*)(pb + (long)(y - dy) * rb) - dx;
            for (int x = x0; x < x1; x += step)
            {
                sum += Math.Abs(ImageUtil.Luma(rowA[x]) - ImageUtil.Luma(rowB[x]));
                count++;
            }
        }
        return sum / count;
    }

    private static float[] RowEdgeProfile(GrayImage g)
    {
        var profile = new float[g.Height];
        for (int y = 0; y < g.Height; y++)
        {
            float sum = 0;
            for (int x = 1; x < g.Width; x++)
            {
                sum += Math.Abs(g[x, y] - g[x - 1, y]);
            }
            profile[y] = sum;
        }
        return profile;
    }

    private static float[] ColumnEdgeProfile(GrayImage g)
    {
        var profile = new float[g.Width];
        for (int y = 1; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                profile[x] += Math.Abs(g[x, y] - g[x, y - 1]);
            }
        }
        return profile;
    }

    /// <summary>Độ dịch s ∈ [−range, range] để b[i − s] khớp a[i] nhất (hệ số tương quan chuẩn hoá trên phần
    /// chồng nhau, yêu cầu chồng ≥ 30% chiều ngắn hơn).</summary>
    private static int BestShift(float[] a, float[] b, int range)
    {
        int minOverlap = Math.Max(4, (int)(Math.Min(a.Length, b.Length) * MinOverlapRatio));
        int bestShift = 0;
        double bestCorr = double.MinValue;
        for (int s = -range; s <= range; s++)
        {
            int start = Math.Max(0, s), end = Math.Min(a.Length, b.Length + s);
            int n = end - start;
            if (n < minOverlap)
            {
                continue;
            }
            double ma = 0, mb = 0;
            for (int i = start; i < end; i++)
            {
                ma += a[i];
                mb += b[i - s];
            }
            ma /= n;
            mb /= n;
            double cov = 0, va = 0, vb = 0;
            for (int i = start; i < end; i++)
            {
                double da = a[i] - ma, db = b[i - s] - mb;
                cov += da * db;
                va += da * da;
                vb += db * db;
            }
            double corr = va > 0 && vb > 0 ? cov / Math.Sqrt(va * vb) : (va == 0 && vb == 0 ? 1 : 0);
            // Ưu tiên nhẹ độ dịch nhỏ khi tương quan xấp xỉ bằng nhau (nội dung lặp lại đều như bảng).
            corr -= Math.Abs(s) * 1e-6;
            if (corr > bestCorr)
            {
                bestCorr = corr;
                bestShift = s;
            }
        }
        return bestShift;
    }

    /// <summary>Sai lệch tuyệt đối trung bình giữa A(x, y) và B(x − dx, y − dy) trên phần chồng nhau (lấy mẫu
    /// cách <paramref name="step"/>). Phần chồng quá nhỏ → +∞.</summary>
    public static double MeanAbsDiff(GrayImage a, GrayImage b, int dx, int dy, int step)
    {
        int x0 = Math.Max(0, dx), y0 = Math.Max(0, dy);
        int x1 = Math.Min(a.Width, b.Width + dx), y1 = Math.Min(a.Height, b.Height + dy);
        long area = (long)(x1 - x0) * (y1 - y0);
        long minArea = (long)(Math.Min((long)a.Width * a.Height, (long)b.Width * b.Height) * MinOverlapRatio);
        if (x1 <= x0 || y1 <= y0 || area < minArea)
        {
            return double.MaxValue;
        }
        double sum = 0;
        long count = 0;
        for (int y = y0; y < y1; y += step)
        {
            int rowA = y * a.Width, rowB = (y - dy) * b.Width - dx;
            for (int x = x0; x < x1; x += step)
            {
                sum += Math.Abs(a.Data[rowA + x] - b.Data[rowB + x]);
                count++;
            }
        }
        return sum / count;
    }
}
