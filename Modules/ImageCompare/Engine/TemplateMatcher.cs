using System.Diagnostics;
using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>1 chỗ tìm thấy ảnh con trong ảnh lớn: khung (toạ độ ảnh lớn) + độ khớp NCC (1 = giống hệt).</summary>
public sealed record TemplateMatch(int Number, SKRectI Bounds, double Score);

public sealed record TemplateResult(IReadOnlyList<TemplateMatch> Matches, bool Swapped, double BestScore, TimeSpan Elapsed);

/// <summary>Tìm ảnh nhỏ (vd 1 nút, 1 icon, 1 đoạn chữ cắt ra) trong ảnh lớn bằng tương quan chéo chuẩn hoá (NCC)
/// trên ảnh xám - chịu được đổi độ sáng / tương phản đều.
///
/// Thô → tinh: thu nhỏ cả 2 ảnh cùng hệ số f (chọn sao cho số phép tính ≲ 3·10⁸, ảnh con thu nhỏ còn ≥ 8 px),
/// tính NCC mọi vị trí, lấy các đỉnh cục bộ đạt (ngưỡng − 0.15), rồi tinh chỉnh từng đỉnh ±(f + 1) px ở độ phân
/// giải gốc. Các chỗ trùng nhau &gt; 30% diện tích chỉ giữ chỗ khớp nhất.</summary>
public static class TemplateMatcher
{
    private const double OpsBudget = 3e8;
    private const int MaxMatches = 50;

    /// <param name="minScore">Độ khớp tối thiểu 0–1 (mặc định 0.9).</param>
    public static TemplateResult Find(SKBitmap a, SKBitmap b, double minScore, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        // Luôn tìm ảnh nhỏ hơn trong ảnh lớn hơn.
        bool swapped = (long)b.Width * b.Height > (long)a.Width * a.Height;
        var (image, template) = swapped ? (b, a) : (a, b);
        if (template.Width > image.Width || template.Height > image.Height || template.Width < 2 || template.Height < 2)
        {
            return new TemplateResult([], swapped, 0, watch.Elapsed);
        }

        double ops = (double)image.Width * image.Height * template.Width * template.Height;
        int f = Math.Max(1, (int)Math.Ceiling(Math.Pow(ops / OpsBudget, 0.25)));
        f = Math.Min(f, Math.Max(1, Math.Min(template.Width, template.Height) / 8));
        var gi = ImageUtil.ToGray(image, SKRectI.Create(image.Width, image.Height), f);
        var gt = ImageUtil.ToGray(template, SKRectI.Create(template.Width, template.Height), f);
        ct.ThrowIfCancellationRequested();

        var coarse = NccMap(gi, gt, ct);
        int mw = gi.Width - gt.Width + 1, mh = gi.Height - gt.Height + 1;
        double coarseMin = Math.Max(0.3, minScore - 0.15);
        var peaks = new List<(int X, int Y, double S)>();
        for (int y = 0; y < mh; y++)
        {
            for (int x = 0; x < mw; x++)
            {
                double s = coarse[y * mw + x];
                if (s >= coarseMin && IsLocalMax(coarse, mw, mh, x, y, s))
                {
                    peaks.Add((x, y, s));
                }
            }
        }
        // Ứng viên theo toạ độ gốc. Ở mức thô, nội dung na ná nhau (các dòng bảng) cho điểm gần bằng nhau → giữ
        // nhiều ứng viên, lọc dần qua các mức phân giải trung gian (kim tự tháp) rồi mới tinh chỉnh ở mức gốc.
        var candidates = peaks.OrderByDescending(p => p.S).Take(2000).Select(p => (X: p.X * f, Y: p.Y * f, S: p.S)).ToList();
        // Các mức: f/3, f/9, ... (> 1) rồi mức gốc 1. f = 1 thì điểm thô đã là điểm ở mức gốc.
        var levels = new List<int>();
        for (int g = f / 3; g > 1; g /= 3)
        {
            levels.Add(g);
        }
        if (f > 1)
        {
            levels.Add(1);
        }
        int radius = f + 1;
        foreach (int level in levels)
        {
            candidates = RefineLevel(image, template, level, candidates, radius, ct)
                .OrderByDescending(c => c.S).Take(level == 1 ? int.MaxValue : 400).ToList();
            radius = level + 1;
        }
        var refined = candidates.Where(c => c.S >= minScore)
            .Select(c => (Box: SKRectI.Create(c.X, c.Y, template.Width, template.Height), c.S)).ToList();

        // Bỏ chỗ trùng: giữ chỗ khớp nhất.
        var kept = new List<(SKRectI Box, double S)>();
        foreach (var candidate in refined.OrderByDescending(c => c.S))
        {
            if (kept.All(k => OverlapRatio(k.Box, candidate.Box) < 0.3) && kept.Count < MaxMatches)
            {
                kept.Add(candidate);
            }
        }
        // Chỗ khớp nhất lên đầu (nhiều chỗ na ná nhau - vd các dòng bảng - thì người dùng xem chỗ tốt nhất trước).
        var matches = kept.OrderByDescending(k => k.S).ThenBy(k => k.Box.Top).ThenBy(k => k.Box.Left)
            .Select((k, i) => new TemplateMatch(i + 1, k.Box, k.S)).ToList();
        double bestScore = peaks.Count > 0 ? Math.Max(peaks[0].S, kept.Count > 0 ? kept.Max(k => k.S) : 0) : 0;
        return new TemplateResult(matches, swapped, bestScore, watch.Elapsed);
    }

    /// <summary>Chấm lại từng ứng viên ở mức thu nhỏ <paramref name="factor"/>: dò NCC trong cửa sổ ±radius px
    /// (toạ độ gốc) quanh ứng viên, lấy vị trí tốt nhất. Ứng viên trùng vị trí sau khi chấm chỉ giữ 1.</summary>
    private static List<(int X, int Y, double S)> RefineLevel(SKBitmap image, SKBitmap template, int factor,
        List<(int X, int Y, double S)> candidates, int radius, CancellationToken ct)
    {
        var gt = ImageUtil.ToGray(template, SKRectI.Create(template.Width, template.Height), factor);
        var bounds = SKRectI.Create(image.Width, image.Height);
        var result = new Dictionary<(int, int), double>();
        foreach (var (cx, cy, _) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var area = SKRectI.Intersect(new SKRectI(cx - radius, cy - radius, cx + radius + template.Width, cy + radius + template.Height), bounds);
            if (area.Width < template.Width || area.Height < template.Height)
            {
                continue;
            }
            var local = ImageUtil.ToGray(image, area, factor);
            var map = NccMap(local, gt, ct);
            int lw = local.Width - gt.Width + 1, lh = local.Height - gt.Height + 1;
            if (lw <= 0 || lh <= 0)
            {
                continue;
            }
            int best = 0;
            for (int i = 1; i < lw * lh; i++)
            {
                if (map[i] > map[best])
                {
                    best = i;
                }
            }
            var key = (area.Left + best % lw * factor, area.Top + best / lw * factor);
            if (!result.TryGetValue(key, out double old) || map[best] > old)
            {
                result[key] = map[best];
            }
        }
        return result.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();
    }

    private static bool IsLocalMax(double[] map, int w, int h, int x, int y, double s)
    {
        for (int ny = Math.Max(0, y - 1); ny <= Math.Min(h - 1, y + 1); ny++)
        {
            for (int nx = Math.Max(0, x - 1); nx <= Math.Min(w - 1, x + 1); nx++)
            {
                double o = map[ny * w + nx];
                // Hoà nhau thì chỉ nhận ô đầu tiên (trên-trái) làm đỉnh.
                if (o > s || (o == s && (ny < y || (ny == y && nx < x))))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static double OverlapRatio(SKRectI a, SKRectI b)
    {
        var i = SKRectI.Intersect(a, b);
        return i.IsEmpty ? 0 : (double)i.Width * i.Height / Math.Min((long)a.Width * a.Height, (long)b.Width * b.Height);
    }

    /// <summary>NCC tại mọi vị trí đặt <paramref name="t"/> trong <paramref name="img"/>. Tổng và tổng bình phương
    /// cửa sổ của ảnh lớn lấy từ ảnh tích phân (O(1) mỗi vị trí); tử số tính trực tiếp. Vùng phẳng (độ lệch chuẩn
    /// ≈ 0) → 1 nếu ảnh con cũng phẳng cùng mức, không thì 0.</summary>
    private static double[] NccMap(GrayImage img, GrayImage t, CancellationToken ct)
    {
        int w = img.Width, h = img.Height, tw = t.Width, th = t.Height;
        int mw = w - tw + 1, mh = h - th + 1;
        var result = new double[Math.Max(0, mw) * Math.Max(0, mh)];
        if (mw <= 0 || mh <= 0)
        {
            return result;
        }
        int n = tw * th;
        double tMean = t.Data.Average();
        var tc = new float[n];
        double tVar = 0;
        for (int i = 0; i < n; i++)
        {
            tc[i] = (float)(t.Data[i] - tMean);
            tVar += tc[i] * tc[i];
        }

        // Ảnh tích phân của giá trị và bình phương.
        var sum = new double[(w + 1) * (h + 1)];
        var sq = new double[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            double rs = 0, rq = 0;
            for (int x = 0; x < w; x++)
            {
                double v = img.Data[y * w + x];
                rs += v;
                rq += v * v;
                sum[(y + 1) * (w + 1) + x + 1] = sum[y * (w + 1) + x + 1] + rs;
                sq[(y + 1) * (w + 1) + x + 1] = sq[y * (w + 1) + x + 1] + rq;
            }
        }
        double Box(double[] s, int x, int y) =>
            s[(y + th) * (w + 1) + x + tw] - s[y * (w + 1) + x + tw] - s[(y + th) * (w + 1) + x] + s[y * (w + 1) + x];

        Parallel.For(0, mh, new ParallelOptions { CancellationToken = ct }, y =>
        {
            for (int x = 0; x < mw; x++)
            {
                double s = Box(sum, x, y), q = Box(sq, x, y);
                double iVar = q - s * s / n;
                if (iVar < 1e-6 || tVar < 1e-6)
                {
                    result[y * mw + x] = iVar < 1e-6 && tVar < 1e-6 && Math.Abs(s / n - tMean) < 2 ? 1 : 0;
                    continue;
                }
                double cross = 0;
                for (int ty = 0; ty < th; ty++)
                {
                    int ir = (y + ty) * w + x, tr = ty * tw;
                    for (int tx = 0; tx < tw; tx++)
                    {
                        cross += img.Data[ir + tx] * tc[tr + tx];
                    }
                }
                result[y * mw + x] = cross / Math.Sqrt(iVar * tVar);
            }
        });
        return result;
    }
}
