using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>1 ô của vùng soi: phần <see cref="Area"/> của ảnh A (toạ độ A) khớp với B khi đặt B tại <see cref="OffsetB"/>
/// (quy ước chung: B(x − dx, y − dy) ↔ A(x, y)).</summary>
public sealed record FocusCell(SKRectI Area, SKPointI OffsetB);

/// <summary>Kết quả căn 1 vùng: độ lệch chung (ô ở tâm vùng), từng ô (phủ kín vùng, không chồng nhau), và ảnh B đã "nắn" theo
/// từng ô - cùng cỡ và vị trí với vùng trên A, so pixel thẳng với A được.</summary>
public sealed record RegionMatch(SKPointI OffsetB, IReadOnlyList<FocusCell> Cells, SKBitmap Warped);

/// <summary>"Soi 1 vùng": người dùng khoanh 1 vùng trên A → tìm chỗ tương ứng ở B rồi chỉ so vùng đó. Cho 2 ảnh lệch bố cục
/// dần (B dùng font / trình duyệt khác nên dòng cao hơn / thấp hơn, ô rộng hơn chút) - không có 1 độ dịch nào khớp cả trang.
///
/// 1. Dò độ lệch của cả vùng quanh vị trí cũ (± <see cref="Margin"/>): sai lệch tuyệt đối trung bình (MAD) trên ảnh xám thu
///    nhỏ, rồi tinh chỉnh ở độ phân giải gốc. Chỉ dò gần vị trí cũ: nhanh, và nội dung lặp lại ở xa (các dòng bảng) không
///    lấn chỗ đúng; khớp ngang nhau thì lấy chỗ gần vị trí cũ nhất.
/// 2. Trong vùng B vẫn lệch dần → chia vùng thành dải ngang theo các dòng trống của A (khe giữa 2 dòng chữ), mỗi dải chia
///    tiếp thành ô theo các cột trống (nhãn, ô nhập...). Dải dò ±<see cref="BandRangeY"/> px quanh dải kề, ô dò
///    ±<see cref="CellRangeX"/> px ngang quanh ô kề - lan dần từ tâm vùng ra.
/// 3. Ghép các ô của B (mỗi ô theo độ lệch riêng) thành ảnh <see cref="RegionMatch.Warped"/> để so pixel.</summary>
public static class RegionAligner
{
    /// <summary>Số phép tính tối đa cho bước dò thô (vị trí × pixel).</summary>
    private const double OpsBudget = 2e8;
    /// <summary>Mỗi dải dò thêm ± bao nhiêu px (dọc / ngang) quanh độ lệch của dải kề.</summary>
    public const int BandRangeY = 8;
    private const int BandRangeX = 6;
    /// <summary>Mỗi ô dò thêm ± bao nhiêu px (ngang / dọc) quanh độ lệch của ô kề trong cùng dải.</summary>
    public const int CellRangeX = 10;
    private const int CellRangeY = 3;
    /// <summary>Pixel "có mực": chênh sáng với màu nền của dòng hơn mức này.</summary>
    private const float InkContrast = 30;
    /// <summary>Các đoạn có mực cách nhau dưới chừng này px (chữ trong 1 từ, nhãn sát ô nhập) gộp thành 1 ô.</summary>
    private const int CellJoinGap = 8;

    /// <summary>Khoảng dò quanh vị trí cũ: 10% cạnh dài của ảnh B, trong [64, 400] px.</summary>
    public static int Margin(SKBitmap b) => Math.Clamp((int)(0.1 * Math.Max(b.Width, b.Height)), 64, 400);

    /// <param name="focus">Vùng trên ảnh A, đã nằm trong khung ảnh A, cạnh ≥ 2 px.</param>
    public static RegionMatch Find(SKBitmap a, SKBitmap b, SKRectI focus, CancellationToken ct = default)
    {
        var whole = FindRegionOffset(a, b, focus, Margin(b), ct);
        var cells = AlignCells(a, b, focus, whole, ct);
        var center = cells.FirstOrDefault(c => c.Area.Contains(focus.MidX, focus.MidY))?.OffsetB ?? whole;
        return new RegionMatch(center, cells, Warp(b, focus, cells));
    }

    // ---- 1. Độ lệch của cả vùng ----

    private static SKPointI FindRegionOffset(SKBitmap a, SKBitmap b, SKRectI focus, int margin, CancellationToken ct)
    {
        // Thu nhỏ f lần sao cho (số vị trí dò) × (số pixel vùng) ≲ OpsBudget: 4·M²·S / f⁴ ≤ budget.
        double area = (double)focus.Width * focus.Height;
        int f = (int)Math.Ceiling(Math.Pow(4.0 * margin * margin * area / OpsBudget, 0.25));
        f = Math.Clamp(f, 1, Math.Max(1, Math.Min(focus.Width, focus.Height) / 4));

        var window = SKRectI.Intersect(SKRectI.Inflate(focus, margin, margin), SKRectI.Create(b.Width, b.Height));
        if (window.Width < focus.Width || window.Height < focus.Height)
        {
            return SKPointI.Empty; // B quá nhỏ để chứa cả vùng → đặt trùng vị trí
        }
        var template = ImageUtil.ToGray(a, focus, f);
        var searched = ImageUtil.ToGray(b, window, f);
        ct.ThrowIfCancellationRequested();

        // Vị trí (theo ô f) của góc trên-trái vùng trong cửa sổ B; vị trí cũ (B không lệch) = (focus − window) / f.
        int maxX = searched.Width - template.Width, maxY = searched.Height - template.Height;
        if (maxX < 0 || maxY < 0)
        {
            return SKPointI.Empty;
        }
        var expected = ((focus.Left - window.Left) / f, (focus.Top - window.Top) / f);
        int sample = Math.Max(1, (int)Math.Sqrt(template.Width * (double)template.Height / 20_000));
        var scores = new double[(maxX + 1) * (maxY + 1)];
        Parallel.For(0, maxY + 1, new ParallelOptions { CancellationToken = ct }, y =>
        {
            for (int x = 0; x <= maxX; x++)
            {
                scores[y * (maxX + 1) + x] = Mad(template, searched, x, y, sample);
            }
        });
        var (cx, cy) = PickNearest(scores, maxX + 1, expected);

        // Tinh chỉnh ở độ phân giải gốc quanh chỗ tìm được (theo độ lệch, quy ước chung).
        var coarse = new SKPointI(focus.Left - (window.Left + cx * f), focus.Top - (window.Top + cy * f));
        int step = Math.Max(1, (int)Math.Sqrt(area / 100_000));
        return BestOffset(a, b, focus, coarse, f + 1, f + 1, step);
    }

    /// <summary>Vị trí có MAD nhỏ nhất; các vị trí khớp gần ngang (≤ tốt nhất + 5%, tối thiểu 0.3 mức sáng) thì lấy vị trí gần
    /// <paramref name="expected"/> nhất.</summary>
    private static (int X, int Y) PickNearest(double[] scores, int width, (int X, int Y) expected)
    {
        double best = scores.Min();
        double tolerance = Math.Max(0.3, best * 0.05);
        int chosen = -1, chosenDistance = int.MaxValue;
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i] <= best + tolerance)
            {
                int d = Math.Abs(i % width - expected.X) + Math.Abs(i / width - expected.Y);
                if (d < chosenDistance)
                {
                    chosen = i;
                    chosenDistance = d;
                }
            }
        }
        return (chosen % width, chosen / width);
    }

    /// <summary>Độ lệch tốt nhất cho phần <paramref name="area"/> của A trong ±(<paramref name="rangeX"/>, <paramref name="rangeY"/>)
    /// quanh <paramref name="expected"/>; ngang nhau thì lấy gần <paramref name="expected"/> nhất.</summary>
    private static SKPointI BestOffset(SKBitmap a, SKBitmap b, SKRectI area, SKPointI expected, int rangeX, int rangeY, int step)
    {
        var best = expected;
        double bestScore = double.MaxValue;
        int bestDistance = int.MaxValue;
        for (int dy = -rangeY; dy <= rangeY; dy++)
        {
            for (int dx = -rangeX; dx <= rangeX; dx++)
            {
                var offset = new SKPointI(expected.X + dx, expected.Y + dy);
                double score = Mad(a, b, area, offset, step);
                int distance = Math.Abs(dx) + Math.Abs(dy);
                if (score < bestScore - 1e-9 || (Math.Abs(score - bestScore) < 1e-9 && distance < bestDistance))
                {
                    best = offset;
                    bestScore = score;
                    bestDistance = distance;
                }
            }
        }
        return best;
    }

    /// <summary>Số dải ngang khi chấm điểm cả vùng (xem <see cref="Mad(GrayImage, GrayImage, int, int, int)"/>).</summary>
    private const int ScoreStrips = 8;

    /// <summary>MAD giữa ảnh xám <paramref name="t"/> và vùng cùng cỡ của <paramref name="img"/> đặt tại (x, y) - tính riêng
    /// từng dải ngang rồi bỏ ¼ số dải khớp tệ nhất. Chỗ khác thật (1 dòng đổi chữ) chỉ làm xấu 1-2 dải; nếu tính trung bình cả
    /// vùng thì nó có thể kéo vùng sang khớp nhầm dòng kề ở bảng nhiều dòng giống nhau (ở đó dòng kề chỉ khác chữ số).</summary>
    private static double Mad(GrayImage t, GrayImage img, int x, int y, int sample)
    {
        int strips = Math.Clamp(t.Height / Math.Max(1, sample * 2), 1, ScoreStrips);
        Span<double> sums = stackalloc double[strips];
        Span<long> counts = stackalloc long[strips];
        for (int ty = 0; ty < t.Height; ty += sample)
        {
            int strip = ty * strips / t.Height;
            int tr = ty * t.Width, ir = (y + ty) * img.Width + x;
            for (int tx = 0; tx < t.Width; tx += sample)
            {
                sums[strip] += Math.Abs(t.Data[tr + tx] - img.Data[ir + tx]);
                counts[strip]++;
            }
        }
        Span<double> means = stackalloc double[strips];
        int used = 0;
        for (int i = 0; i < strips; i++)
        {
            if (counts[i] > 0)
            {
                means[used++] = sums[i] / counts[i];
            }
        }
        means = means[..used];
        means.Sort();
        int keep = Math.Max(1, used - used / 4);
        double total = 0;
        for (int i = 0; i < keep; i++)
        {
            total += means[i];
        }
        return total / keep;
    }

    /// <summary>MAD giữa phần <paramref name="area"/> của A và B đặt tại <paramref name="offset"/>, đọc thẳng pixel gốc. Chỉ tính
    /// các điểm có ở B; phần có ở B dưới 60% → +∞.</summary>
    private static unsafe double Mad(SKBitmap a, SKBitmap b, SKRectI area, SKPointI offset, int step)
    {
        byte* pa = (byte*)a.GetPixels(), pb = (byte*)b.GetPixels();
        int ra = a.RowBytes, rb = b.RowBytes;
        double sum = 0;
        long n = 0, total = 0;
        for (int y = area.Top; y < area.Bottom; y += step)
        {
            int by = y - offset.Y;
            uint* rowA = (uint*)(pa + (long)y * ra);
            uint* rowB = by >= 0 && by < b.Height ? (uint*)(pb + (long)by * rb) : null;
            for (int x = area.Left; x < area.Right; x += step)
            {
                total++;
                int bx = x - offset.X;
                if (rowB is null || bx < 0 || bx >= b.Width)
                {
                    continue;
                }
                sum += Math.Abs(ImageUtil.Luma(rowA[x]) - ImageUtil.Luma(rowB[bx]));
                n++;
            }
        }
        return n == 0 || n < total * 0.6 ? double.MaxValue : sum / n;
    }

    // ---- 2. Dải ngang → ô ----

    private static List<FocusCell> AlignCells(SKBitmap a, SKBitmap b, SKRectI focus, SKPointI whole, CancellationToken ct)
    {
        var ink = InkMap(a, focus);
        var bands = Split(Enumerable.Range(0, focus.Height).Select(y => RowInk(ink, focus.Width, y)).ToArray(),
            Math.Max(3, focus.Width / 50), joinGap: 0);
        if (bands.Count == 0)
        {
            return [new FocusCell(focus, whole)];
        }

        // Dải chứa tâm vùng dò quanh độ lệch chung; các dải khác dò quanh độ lệch của dải kề phía tâm.
        int center = Math.Max(0, bands.FindIndex(r => focus.Top + r.Outer.End > focus.MidY));
        var bandOffsets = new SKPointI[bands.Count];
        var cells = new List<FocusCell>[bands.Count];
        foreach (int i in OutwardFrom(center, bands.Count))
        {
            ct.ThrowIfCancellationRequested();
            var expected = i == center ? whole : bandOffsets[i < center ? i + 1 : i - 1];
            var (outer, content) = bands[i];
            var contentRect = new SKRectI(focus.Left, focus.Top + content.Start, focus.Right, focus.Top + content.End);
            bandOffsets[i] = BestOffset(a, b, contentRect, expected, BandRangeX, BandRangeY, Math.Max(1, focus.Width / 600));
            cells[i] = AlignBandCells(a, b, focus, ink, outer, content, bandOffsets[i]);
        }
        return cells.SelectMany(c => c).ToList();
    }

    /// <summary>Chia 1 dải thành ô theo cột trống (trong phần có nội dung của dải), mỗi ô dò độ lệch quanh ô kề - lan từ ô
    /// giữa dải ra 2 bên.</summary>
    private static List<FocusCell> AlignBandCells(SKBitmap a, SKBitmap b, SKRectI focus, byte[] ink,
        (int Start, int End) outer, (int Start, int End) content, SKPointI bandOffset)
    {
        var columnInk = new int[focus.Width];
        for (int y = content.Start; y < content.End; y++)
        {
            for (int x = 0; x < focus.Width; x++)
            {
                columnInk[x] += ink[y * focus.Width + x];
            }
        }
        var columns = Split(columnInk, blankLimit: 0, CellJoinGap);
        int top = focus.Top + outer.Start, bottom = focus.Top + outer.End;
        if (columns.Count <= 1)
        {
            return [new FocusCell(new SKRectI(focus.Left, top, focus.Right, bottom), bandOffset)];
        }

        int center = Math.Max(0, columns.FindIndex(c => focus.Left + c.Outer.End > focus.MidX));
        var offsets = new SKPointI[columns.Count];
        foreach (int i in OutwardFrom(center, columns.Count))
        {
            var expected = i == center ? bandOffset : offsets[i < center ? i + 1 : i - 1];
            var probe = new SKRectI(focus.Left + columns[i].Content.Start, focus.Top + content.Start,
                focus.Left + columns[i].Content.End, focus.Top + content.End);
            offsets[i] = BestOffset(a, b, probe, expected, CellRangeX, CellRangeY, 1);
        }
        return columns.Select((c, i) => new FocusCell(
            new SKRectI(focus.Left + c.Outer.Start, top, focus.Left + c.Outer.End, bottom), offsets[i])).ToList();
    }

    /// <summary>Chỉ số center, center + 1, …, cuối, rồi center − 1, …, 0.</summary>
    private static IEnumerable<int> OutwardFrom(int center, int count)
    {
        for (int i = center; i < count; i++)
        {
            yield return i;
        }
        for (int i = center - 1; i >= 0; i--)
        {
            yield return i;
        }
    }

    /// <summary>1 = pixel "có mực" (chênh sáng với màu nền của dòng đó - màu xuất hiện nhiều nhất trong dòng).</summary>
    private static unsafe byte[] InkMap(SKBitmap a, SKRectI focus)
    {
        var ink = new byte[focus.Width * focus.Height];
        var histogram = new int[256];
        var luma = new float[focus.Width];
        byte* pa = (byte*)a.GetPixels();
        for (int y = 0; y < focus.Height; y++)
        {
            uint* row = (uint*)(pa + (long)(focus.Top + y) * a.RowBytes) + focus.Left;
            Array.Clear(histogram);
            for (int x = 0; x < focus.Width; x++)
            {
                luma[x] = ImageUtil.Luma(row[x]);
                histogram[(int)luma[x]]++;
            }
            int background = Array.IndexOf(histogram, histogram.Max());
            for (int x = 0; x < focus.Width; x++)
            {
                ink[y * focus.Width + x] = Math.Abs(luma[x] - background) > InkContrast ? (byte)1 : (byte)0;
            }
        }
        return ink;
    }

    private static int RowInk(byte[] ink, int width, int y)
    {
        int count = 0;
        for (int x = 0; x < width; x++)
        {
            count += ink[y * width + x];
        }
        return count;
    }

    /// <summary>Tách dãy theo các vị trí "trống" (mực ≤ <paramref name="blankLimit"/>): mỗi đoạn có nội dung (Content, gộp các
    /// đoạn cách nhau &lt; <paramref name="joinGap"/>) + nửa khe trống 2 bên (Outer - các Outer nối liền, phủ kín cả dãy).</summary>
    private static List<((int Start, int End) Outer, (int Start, int End) Content)> Split(int[] inkCounts, int blankLimit, int joinGap)
    {
        var contents = new List<(int Start, int End)>();
        for (int i = 0; i < inkCounts.Length;)
        {
            if (inkCounts[i] <= blankLimit)
            {
                i++;
                continue;
            }
            int start = i;
            while (i < inkCounts.Length && inkCounts[i] > blankLimit)
            {
                i++;
            }
            if (contents.Count > 0 && start - contents[^1].End < joinGap)
            {
                contents[^1] = (contents[^1].Start, i);
            }
            else
            {
                contents.Add((start, i));
            }
        }
        var result = new List<((int, int), (int, int))>();
        for (int i = 0; i < contents.Count; i++)
        {
            int start = i == 0 ? 0 : (contents[i - 1].End + contents[i].Start) / 2;
            int end = i == contents.Count - 1 ? inkCounts.Length : (contents[i].End + contents[i + 1].Start) / 2;
            result.Add(((start, end), contents[i]));
        }
        return result;
    }

    // ---- 3. Ghép B theo từng ô ----

    /// <summary>Ảnh cỡ vùng soi: mỗi ô lấy từ B theo độ lệch của ô đó. Phần rơi ra ngoài B để trong suốt (so pixel sẽ báo
    /// khác - nội dung đó không có ở B).</summary>
    private static SKBitmap Warp(SKBitmap b, SKRectI focus, IReadOnlyList<FocusCell> cells)
    {
        var warped = new SKBitmap(new SKImageInfo(focus.Width, focus.Height, ImageUtil.ColorType, ImageUtil.AlphaType));
        warped.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(warped);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.None, BlendMode = SKBlendMode.Src };
        var bRect = SKRectI.Create(b.Width, b.Height);
        foreach (var cell in cells)
        {
            var o = cell.OffsetB;
            var src = SKRectI.Intersect(new SKRectI(cell.Area.Left - o.X, cell.Area.Top - o.Y, cell.Area.Right - o.X, cell.Area.Bottom - o.Y), bRect);
            if (src.Width <= 0 || src.Height <= 0)
            {
                continue;
            }
            // Vị trí trong ảnh ghép = vị trí trên A − góc vùng soi.
            var dst = SKRectI.Create(src.Left + o.X - focus.Left, src.Top + o.Y - focus.Top, src.Width, src.Height);
            canvas.DrawBitmap(b, src, dst, paint);
        }
        return warped;
    }
}
