using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>1 dải dòng liên tiếp sau khi căn theo dòng. Dải ghép cặp có cả A và B (so từng pixel); dải chỉ có
/// ở A hoặc chỉ có ở B là đoạn bị bỏ đi / thêm vào.</summary>
/// <param name="AY">Dòng bắt đầu trong ảnh A, -1 nếu dải chỉ có ở B.</param>
/// <param name="BY">Dòng bắt đầu trong ảnh B, -1 nếu dải chỉ có ở A.</param>
public sealed record RowBand(int AY, int BY, int Height)
{
    public bool IsPaired => AY >= 0 && BY >= 0;
    public RegionKind Kind => IsPaired ? RegionKind.Changed : AY >= 0 ? RegionKind.OnlyInA : RegionKind.OnlyInB;
}

/// <summary>Căn 2 ảnh theo từng dòng pixel - cho ảnh chụp cuộn / trang dài mà B thêm hoặc bớt 1 đoạn ở giữa so
/// với A (dịch chuyển toàn ảnh không căn được: phần trên khớp thì phần dưới lệch).
///
/// Mỗi dòng (trên các cột 2 ảnh chồng nhau theo chiều ngang) được băm sau khi làm tròn màu (bỏ 3 bit thấp - chịu
/// được khử răng cưa lệch nhẹ), rồi chạy thuật toán diff Myers (như <c>git diff</c> cho dòng văn bản) trên 2 dãy
/// mã băm → các đoạn giống / bỏ / thêm. Trong 1 cụm sửa đổi, số dòng bỏ và thêm bằng nhau được ghép cặp (dòng bị
/// sửa - so pixel), phần dư là đoạn chỉ có ở 1 ảnh.</summary>
public static class RowAligner
{
    /// <summary>Số bước sửa đổi tối đa của Myers - 2 ảnh khác nhau quá nhiều thì bỏ (không còn là "cùng trang").</summary>
    /// Vết lưu tăng theo D² (3000 bước ≈ 36 MB) nên không để lớn hơn.
    public const int MaxEdits = 3000;

    /// <summary>Các dải dòng theo thứ tự trên → dưới, hoặc null nếu 2 ảnh khác nhau quá nhiều để căn theo dòng.</summary>
    /// <param name="dx">Độ lệch ngang của B so với A (px ảnh A).</param>
    public static List<RowBand>? Align(SKBitmap a, SKBitmap b, int dx, CancellationToken ct)
    {
        int x0 = Math.Max(0, dx), x1 = Math.Min(a.Width, b.Width + dx);
        if (x1 - x0 < 8)
        {
            return null;
        }
        var ha = RowHashes(a, x0, x1);
        var hb = RowHashes(b, x0 - dx, x1 - dx);
        ct.ThrowIfCancellationRequested();
        var script = Myers(ha, hb, ct);
        return script is null ? null : ToBands(script, ha.Length, hb.Length);
    }

    private static unsafe ulong[] RowHashes(SKBitmap bmp, int x0, int x1)
    {
        var hashes = new ulong[bmp.Height];
        byte* basePtr = (byte*)bmp.GetPixels();
        for (int y = 0; y < bmp.Height; y++)
        {
            uint* row = (uint*)(basePtr + (long)y * bmp.RowBytes);
            ulong h = 14695981039346656037UL;
            for (int x = x0; x < x1; x++)
            {
                h = (h ^ (row[x] & 0xF8F8F8F8u)) * 1099511628211UL;
            }
            hashes[y] = h;
        }
        return hashes;
    }

    private enum Op : byte { Equal, Delete, Insert }

    /// <summary>Diff Myers O((N + M)·D). Lưu vết V từng bước d (chỉ phần k ∈ [−d−1, d+1]) để lần ngược ra kịch bản.
    /// Null nếu D vượt <see cref="MaxEdits"/>.</summary>
    private static List<Op>? Myers(ulong[] a, ulong[] b, CancellationToken ct)
    {
        int n = a.Length, m = b.Length;
        int max = Math.Min(n + m, MaxEdits);
        int offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        int finalD = -1;
        for (int d = 0; d <= max; d++)
        {
            if ((d & 63) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            // Chụp V (phần có thể dùng tới ở bước d) trước khi cập nhật.
            var snapshot = new int[2 * d + 3];
            Array.Copy(v, offset - d - 1, snapshot, 0, snapshot.Length);
            trace.Add(snapshot);
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }
                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    finalD = d;
                    break;
                }
            }
            if (finalD >= 0)
            {
                break;
            }
        }
        if (finalD < 0)
        {
            return null;
        }

        // Lần ngược từ (n, m) về (0, 0).
        var ops = new List<Op>(n + m);
        int cx = n, cy = m;
        for (int d = finalD; d >= 0; d--)
        {
            var snap = trace[d];
            int V(int k) => snap[k + d + 1];
            int k = cx - cy;
            int prevK = k == -d || (k != d && V(k - 1) < V(k + 1)) ? k + 1 : k - 1;
            int prevX = d == 0 ? 0 : V(prevK);
            int prevY = prevX - prevK;
            while (cx > prevX && cy > prevY)
            {
                ops.Add(Op.Equal);
                cx--;
                cy--;
            }
            if (d > 0)
            {
                ops.Add(cx == prevX ? Op.Insert : Op.Delete);
            }
            cx = prevX;
            cy = prevY;
        }
        ops.Reverse();
        return ops;
    }

    private static List<RowBand> ToBands(List<Op> ops, int n, int m)
    {
        var bands = new List<RowBand>();
        int ai = 0, bi = 0, i = 0;
        void AddPaired(int ay, int by, int h)
        {
            if (h <= 0)
            {
                return;
            }
            // Nối vào dải ghép cặp liền trước nếu liên tục ở cả 2 ảnh.
            if (bands.Count > 0 && bands[^1] is { IsPaired: true } last && last.AY + last.Height == ay && last.BY + last.Height == by)
            {
                bands[^1] = last with { Height = last.Height + h };
            }
            else
            {
                bands.Add(new RowBand(ay, by, h));
            }
        }
        while (i < ops.Count)
        {
            if (ops[i] == Op.Equal)
            {
                int start = i;
                while (i < ops.Count && ops[i] == Op.Equal)
                {
                    i++;
                }
                AddPaired(ai, bi, i - start);
                ai += i - start;
                bi += i - start;
                continue;
            }
            // Cụm sửa đổi: đếm số dòng bỏ (A) và thêm (B).
            int del = 0, ins = 0;
            while (i < ops.Count && ops[i] != Op.Equal)
            {
                if (ops[i] == Op.Delete)
                {
                    del++;
                }
                else
                {
                    ins++;
                }
                i++;
            }
            int paired = Math.Min(del, ins);
            AddPaired(ai, bi, paired);
            if (del > paired)
            {
                bands.Add(new RowBand(ai + paired, -1, del - paired));
            }
            if (ins > paired)
            {
                bands.Add(new RowBand(-1, bi + paired, ins - paired));
            }
            ai += del;
            bi += ins;
        }
        return bands;
    }
}
