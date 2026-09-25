using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>So từng pixel phần chồng nhau của A và B (B đặt lệch <c>offset</c>) → mặt nạ khác biệt + các vùng.
///
/// Pixel khác khi chênh lệch lớn nhất giữa 3 kênh màu vượt ngưỡng. Tuỳ chọn bỏ qua khử răng cưa: pixel khác
/// mà nằm trên viền mịn của chữ / hình (heuristic <c>antialiased</c> của thư viện pixelmatch, ISC License,
/// © Mapbox) thì không tính - chụp cùng 1 màn hình 2 lần, viền chữ ClearType có thể lệch nhẹ.
///
/// Gom vùng: chia phần chồng thành ô 8 × 8, ô có pixel khác nối với ô khác cách ≤ 2 ô (8 hướng) thành 1 vùng
/// → các chỗ khác cách nhau dưới ~16 px (vd các chữ trong 1 từ) gộp thành 1 khung.</summary>
public static class PixelDiff
{
    private const int Cell = 8;
    private const int JoinCells = 2;

    public static unsafe (byte[] Mask, long DiffPixels, List<DiffRegion> Regions) Compare(
        SKBitmap a, SKBitmap b, SKPointI offset, SKRectI overlap, DiffOptions options, CancellationToken ct)
    {
        int w = overlap.Width, h = overlap.Height;
        var mask = new byte[w * h];
        if (w <= 0 || h <= 0)
        {
            return (mask, 0, []);
        }
        int threshold = options.ThresholdValue;
        var pa = new Pixels(a, SKPointI.Empty);
        var pb = new Pixels(b, offset);
        long diffPixels = 0;
        var ignore = options.IgnoreRects.ToArray();

        for (int y = 0; y < h; y++)
        {
            if ((y & 255) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            int ay = overlap.Top + y;
            uint* rowA = pa.Row(ay);
            uint* rowB = pb.Row(ay);
            for (int x = 0; x < w; x++)
            {
                int ax = overlap.Left + x;
                uint ca = rowA[ax], cb = rowB[ax];
                if (ca == cb || MaxChannelDelta(ca, cb) <= threshold)
                {
                    continue;
                }
                if (ignore.Length > 0 && InAny(ignore, ax, ay))
                {
                    continue;
                }
                if (options.IgnoreAntialiasing
                    && (Antialiased(pa, pb, ax, ay, overlap) || Antialiased(pb, pa, ax, ay, overlap)))
                {
                    continue;
                }
                mask[y * w + x] = 1;
                diffPixels++;
            }
        }
        ct.ThrowIfCancellationRequested();
        return (mask, diffPixels, GroupRegions(mask, w, h, overlap));
    }

    private static bool InAny(SKRectI[] rects, int x, int y)
    {
        foreach (var r in rects)
        {
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
            {
                return true;
            }
        }
        return false;
    }

    private static int MaxChannelDelta(uint p, uint q)
    {
        int db = Math.Abs((int)(p & 0xFF) - (int)(q & 0xFF));
        int dg = Math.Abs((int)((p >> 8) & 0xFF) - (int)((q >> 8) & 0xFF));
        int dr = Math.Abs((int)((p >> 16) & 0xFF) - (int)((q >> 16) & 0xFF));
        int da = Math.Abs((int)(p >> 24) - (int)(q >> 24));
        return Math.Max(Math.Max(db, dg), Math.Max(dr, da));
    }

    private static float Brightness(uint p) => ImageUtil.Luma(p);

    /// <summary>Pixel (x, y) của <paramref name="img"/> có phải điểm khử răng cưa không (pixelmatch): trong 8
    /// lân cận có ≤ 2 pixel cùng màu với nó, có cả lân cận tối hơn và sáng hơn, và lân cận tối nhất hoặc sáng
    /// nhất nằm trong vùng màu đồng nhất (≥ 3 lân cận giống hệt) ở CẢ 2 ảnh - tức là viền mịn của 1 hình vẫn
    /// còn nguyên ở ảnh kia, chỉ lệch nhẹ.</summary>
    private static unsafe bool Antialiased(Pixels img, Pixels other, int x1, int y1, SKRectI area)
    {
        int x0 = Math.Max(x1 - 1, area.Left), y0 = Math.Max(y1 - 1, area.Top);
        int x2 = Math.Min(x1 + 1, area.Right - 1), y2 = Math.Min(y1 + 1, area.Bottom - 1);
        uint center = img.Row(y1)[x1];
        float centerLuma = Brightness(center);
        int zeroes = x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2 ? 1 : 0;
        float min = 0, max = 0;
        int minX = 0, minY = 0, maxX = 0, maxY = 0;
        for (int x = x0; x <= x2; x++)
        {
            for (int y = y0; y <= y2; y++)
            {
                if (x == x1 && y == y1)
                {
                    continue;
                }
                float delta = centerLuma - Brightness(img.Row(y)[x]);
                if (delta == 0)
                {
                    if (++zeroes > 2)
                    {
                        return false;
                    }
                }
                else if (delta < min)
                {
                    min = delta;
                    minX = x;
                    minY = y;
                }
                else if (delta > max)
                {
                    max = delta;
                    maxX = x;
                    maxY = y;
                }
            }
        }
        if (min == 0 || max == 0)
        {
            return false;
        }
        return (HasManySiblings(img, minX, minY, area) && HasManySiblings(other, minX, minY, area))
            || (HasManySiblings(img, maxX, maxY, area) && HasManySiblings(other, maxX, maxY, area));
    }

    private static unsafe bool HasManySiblings(Pixels img, int x1, int y1, SKRectI area)
    {
        int x0 = Math.Max(x1 - 1, area.Left), y0 = Math.Max(y1 - 1, area.Top);
        int x2 = Math.Min(x1 + 1, area.Right - 1), y2 = Math.Min(y1 + 1, area.Bottom - 1);
        uint center = img.Row(y1)[x1];
        int zeroes = x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2 ? 1 : 0;
        for (int x = x0; x <= x2; x++)
        {
            for (int y = y0; y <= y2; y++)
            {
                if ((x != x1 || y != y1) && img.Row(y)[x] == center && ++zeroes > 2)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Gom mặt nạ thành vùng (xem mô tả lớp). Khung vùng tính chính xác theo pixel khác, toạ độ ảnh A.</summary>
    private static List<DiffRegion> GroupRegions(byte[] mask, int w, int h, SKRectI overlap)
    {
        int cw = (w + Cell - 1) / Cell, ch = (h + Cell - 1) / Cell;
        var cellMin = new SKPointI[cw * ch];
        var cellMax = new SKPointI[cw * ch];
        var cellCount = new int[cw * ch];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (mask[y * w + x] == 0)
                {
                    continue;
                }
                int c = y / Cell * cw + x / Cell;
                if (cellCount[c]++ == 0)
                {
                    cellMin[c] = new SKPointI(x, y);
                    cellMax[c] = new SKPointI(x, y);
                }
                else
                {
                    cellMin[c] = new SKPointI(Math.Min(cellMin[c].X, x), Math.Min(cellMin[c].Y, y));
                    cellMax[c] = new SKPointI(Math.Max(cellMax[c].X, x), Math.Max(cellMax[c].Y, y));
                }
            }
        }

        var visited = new bool[cw * ch];
        var found = new List<(SKRectI Bounds, int Pixels)>();
        var queue = new Queue<int>();
        for (int start = 0; start < cellCount.Length; start++)
        {
            if (cellCount[start] == 0 || visited[start])
            {
                continue;
            }
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue, pixels = 0;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                left = Math.Min(left, cellMin[c].X);
                top = Math.Min(top, cellMin[c].Y);
                right = Math.Max(right, cellMax[c].X);
                bottom = Math.Max(bottom, cellMax[c].Y);
                pixels += cellCount[c];
                int cx = c % cw, cy = c / cw;
                for (int ny = Math.Max(0, cy - JoinCells); ny <= Math.Min(ch - 1, cy + JoinCells); ny++)
                {
                    for (int nx = Math.Max(0, cx - JoinCells); nx <= Math.Min(cw - 1, cx + JoinCells); nx++)
                    {
                        int n = ny * cw + nx;
                        if (cellCount[n] > 0 && !visited[n])
                        {
                            visited[n] = true;
                            queue.Enqueue(n);
                        }
                    }
                }
            }
            found.Add((new SKRectI(overlap.Left + left, overlap.Top + top, overlap.Left + right + 1, overlap.Top + bottom + 1), pixels));
        }

        return found
            .OrderBy(r => r.Bounds.Top / 16).ThenBy(r => r.Bounds.Left) // trên → dưới, cùng "dòng" thì trái → phải
            .Select((r, i) => new DiffRegion(i + 1, r.Bounds, r.Pixels))
            .ToList();
    }

    /// <summary>Truy cập dòng pixel của 1 ảnh theo toạ độ ảnh A (ảnh B dịch <c>offset</c>).</summary>
    private readonly unsafe struct Pixels(SKBitmap bitmap, SKPointI offset)
    {
        private readonly byte* _base = (byte*)bitmap.GetPixels();
        private readonly int _rowBytes = bitmap.RowBytes;
        private readonly SKPointI _offset = offset;

        /// <summary>Con trỏ sao cho <c>Row(y)[x]</c> là pixel tại (x, y) theo toạ độ ảnh A.</summary>
        public uint* Row(int y) => (uint*)(_base + (long)(y - _offset.Y) * _rowBytes) - _offset.X;
    }
}
