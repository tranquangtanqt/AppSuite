using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Kiểm tra lại các chỗ <see cref="TextDiff"/> nghi khác bằng cách đọc riêng từng vùng.
///
/// Đọc cả trang, OCR hay đọc sai 1 phía - nhất là font bitmap của màn hình cũ (đã gặp: "230107" đọc thành "230川7",
/// "用途区分" thành "厓途区分") → mục "đổi chữ" mà chữ thật giống hệt, và danh sách hiện chữ rác. Đọc lại 1 vùng nhỏ
/// cắt ra (ít nhiễu xung quanh) ở vài mức phóng cho ra nhiều cách đọc; có 1 cách đọc của A trùng 1 cách đọc của B →
/// chữ giống nhau, bỏ khỏi danh sách. Còn khác thì hiện cặp cách đọc sát nhau nhất (bớt chữ rác).</summary>
public static class TextDiffVerifier
{
    /// <summary>Cách đọc lại mỗi vùng: mức phóng × ngưỡng (null = Otsu kẹp như khi đọc cả trang; 225 = ngưỡng cao cho
    /// chữ xám nét 1 px - ở ngưỡng thường nét bị đứt thành mảnh, OCR không đọc nổi).</summary>
    private static readonly (int Scale, int? Threshold)[] Variants = [(3, null), (4, null), (3, 225), (4, 225)];

    /// <param name="readers">Bộ đọc dùng để đọc lại - nên gồm cả 2 engine: font bitmap mà engine này đọc sai thì engine
    /// kia có khi đọc đúng.</param>
    public static TextDiffResult Verify(TextDiffResult result, SKBitmap a, SKBitmap b, IReadOnlyList<IFormTextReader> readers,
        CancellationToken ct = default, IProgress<double>? progress = null)
    {
        // Mỗi mục độc lập → kiểm tra song song (bộ đọc tự lo đa luồng: Tesseract có pool engine, Windows OCR tự khoá).
        var outcome = new TextDiffItem?[result.Items.Count]; // null = đọc lại thì giống → bỏ
        int done = 0;
        var parallel = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) };
        Parallel.For(0, result.Items.Count, parallel, n =>
        {
            outcome[n] = Check(result.Items[n], a, b, readers);
            progress?.Report((double)Interlocked.Increment(ref done) / result.Items.Count);
        });
        ct.ThrowIfCancellationRequested();
        int confirmedSame = outcome.Count(o => o is null);
        var renumbered = outcome.Where(o => o is not null).Select((item, n) => item! with { Number = n + 1 }).ToList();
        return result with { Items = renumbered, SameCount = result.SameCount + confirmedSame, VerifiedSame = confirmedSame };
    }

    /// <summary>1 mục: null nếu đọc lại thì giống; không thì mục (có thể đã đổi chữ hiển thị / loại).</summary>
    private static TextDiffItem? Check(TextDiffItem item, SKBitmap a, SKBitmap b, IReadOnlyList<IFormTextReader> readers)
    {
        if (item.Kind == TextDiffKind.ColorChanged)
        {
            return item; // chữ đã giống - chỉ khác màu
        }
        if (item.A is null || item.B is null)
        {
            return CheckOneSided(item, a, b, readers);
        }
        var readsA = Readings(a, Region(item.A, item.B) ?? item.Other, predicted: item.A is null, item.A, readers, stop: null);
        // Phía B: dừng ngay khi đã có cách đọc trùng - mục giống không cần đọc hết các biến thể.
        var readsB = Readings(b, Region(item.B, item.A) ?? item.Other, predicted: item.B is null, item.B, readers,
            stop: reads => Matches(item, readsA, reads) is not null);
        if (Matches(item, readsA, readsB) is not { } same)
        {
            return Closest(item, readsA, readsB);
        }
        // Đọc lại thì giống - nhưng màu chữ vẫn có thể khác: ô bị khoá ở A (chữ xám nét mảnh, OCR cả trang chỉ đọc
        // được 1 phần: "受注" → "三") mà ở B chữ đen (đã gặp 4 chỗ trên ảnh thật). Màu đo trong khung gốc của mục, không
        // phải vùng cắt nới rộng (có thể dính nhãn đen bên cạnh).
        // Màu nét quá sáng (≥ 200) không phải màu chữ: khung nhỏ của bản đọc rác trên nút bị mờ đo trúng viền nổi trắng
        // (đã gặp "商品仕入": #A0A0A0 → #E4EBEE).
        if (item.A is { } sa && item.B is { } sb
            && TextDiff.InkColor(a, sa.Bounds) is { } ca && TextDiff.InkColor(b, sb.Bounds) is { } cb
            && Luma(ca) < 200 && Luma(cb) < 200 && TextDiff.ColorsDiffer(ca, cb))
        {
            return item with
            {
                Kind = TextDiffKind.ColorChanged,
                A = sa with { Text = same.Text, Key = same.Key },
                B = sb with { Text = same.Text, Key = same.Key },
                Note = $"{Hex(ca)} → {Hex(cb)}",
            };
        }
        return null;
    }

    /// <summary>Mục chỉ có ở 1 phía. Giống nếu: (1) vùng dự đoán ở ảnh kia (cắt rộng) chứa chữ của mục, hoặc (2) đọc lại
    /// riêng chính mục ra đúng chữ mà vùng dự đoán cắt sát cũng đọc ra - OCR cả trang bỏ sót hẳn 1 phía và đọc sai phía
    /// còn lại (đã gặp: ô "枚" chữ xám ở A đọc thành "物", B chữ đen không đọc ra; "胴ｻｲｽﾞ" A không đọc ra, B thành
    /// "鋼サイス。"). (2) chỉ so với vùng cắt sát: vùng rộng dễ dính chữ ô bên cạnh. Giống thì so màu như mục 2 phía.</summary>
    private static TextDiffItem? CheckOneSided(TextDiffItem item, SKBitmap a, SKBitmap b, IReadOnlyList<IFormTextReader> readers)
    {
        if (item.Other is not { } predicted)
        {
            return item;
        }
        bool inA = item.A is not null;
        var segment = (item.A ?? item.B)!;
        SKBitmap own = inA ? a : b, other = inA ? b : a;
        var tight = Readings(other, predicted, predicted: false, null, readers, stop: null);
        var wide = Readings(other, predicted, predicted: true, null, readers,
            stop: reads => segment.Key.Length >= 2 && reads.Any(r => r.Key.Contains(segment.Key, StringComparison.Ordinal)));
        (string Text, string Key)? same = segment.Key.Length >= 2
            && tight.Concat(wide).Any(r => r.Key.Contains(segment.Key, StringComparison.Ordinal)) ? (segment.Text, segment.Key) : null;
        if (same is null)
        {
            var mine = Readings(own, segment.Bounds, predicted: false, null, readers, stop: reads => reads.Any(r => tight.Any(t => t.Key == r.Key)));
            same = mine.Where(r => tight.Any(t => t.Key == r.Key)).Select(r => ((string, string)?)r).FirstOrDefault();
        }
        if (same is null && SameShape(own, segment.Bounds, other, predicted) is { } found)
        {
            // OCR không chốt được nhưng nét chữ 2 phía trùng hình (cùng font, chỉ khác màu): lấy chữ của phía nét đậm
            // hơn (dễ đọc hơn) - chữ xám thì OCR hay đọc sai ("枚" → "物" / "板").
            predicted = found;
            bool ownDarker = TextDiff.InkColor(own, segment.Bounds) is not { } co || TextDiff.InkColor(other, found) is not { } cf
                || Luma(co) <= Luma(cf);
            same = ownDarker || tight.Count == 0 ? (segment.Text, segment.Key) : tight[0];
        }
        if (same is not { } text)
        {
            return item;
        }
        // Màu: khung của mục ↔ vùng dự đoán (đã khớp chữ nên vùng dự đoán là ô đó).
        var (boxA, boxB) = inA ? (segment.Bounds, predicted) : (predicted, segment.Bounds);
        if (TextDiff.InkColor(a, boxA) is { } ca && TextDiff.InkColor(b, boxB) is { } cb
            && Luma(ca) < 200 && Luma(cb) < 200 && TextDiff.ColorsDiffer(ca, cb))
        {
            var sa = new TextSegment(text.Text, text.Key, boxA);
            var sb = new TextSegment(text.Text, text.Key, boxB);
            return item with { Kind = TextDiffKind.ColorChanged, A = sa, B = sb, Other = null, Note = $"{Hex(ca)} → {Hex(cb)}" };
        }
        return null;
    }

    /// <summary>Nét chữ trong <paramref name="box"/> (ảnh 1) và quanh <paramref name="near"/> (ảnh 2) trùng hình: mặt nạ
    /// nét (lệch nền ≥ 40% độ lệch lớn nhất trong vùng - chữ xám lẫn chữ đen) cắt sát theo khung nét, gần cùng cỡ, dóng
    /// cột cho phép trôi dần (xem thân hàm), pixel lệch ≤ 30% số pixel nét. Trả khung nét ở ảnh 2, null nếu không trùng.
    /// Không dùng OCR: font bitmap cũ mà OCR đọc sai mỗi phía 1 kiểu ("胴ｻｲｽﾞ": A "胴サれ。", B "鋼サイス。") vẫn so được khi 2
    /// phía cùng font. Vùng ảnh 2 nới 3 px (vị trí dự đoán lệch vài px); dính viền / chữ bên cạnh thì khung nét lệch cỡ →
    /// không trùng (an toàn: mục vẫn giữ).</summary>
    internal static SKRectI? SameShape(SKBitmap bitmap1, SKRectI box, SKBitmap bitmap2, SKRectI near)
    {
        if (GrownMask(bitmap1, SKRectI.Inflate(box, 1, 1)) is not { } m1 || GrownMask(bitmap2, SKRectI.Inflate(near, 3, 3)) is not { } m2
            || Math.Abs(m1.Bounds.Height - m2.Bounds.Height) > 2
            || Math.Abs(m1.Bounds.Width - m2.Bounds.Width) > Math.Max(2, m1.Bounds.Width / 8))
        {
            return null;
        }
        // Dóng cột: cột x của mặt nạ 1 ứng với cột x + d của mặt nạ 2, d trôi tối đa 1 px mỗi cột (quy hoạch động) - 2 trình
        // duyệt dựng cùng font nhưng khoảng cách chữ lệch 1 px, cộng dồn qua từng chữ ("胴ｻｲｽﾞ" ở A hẹp hơn B 2 px, "ｲ" và
        // "ｽ" dính nhau nên không tách theo cột trống được). Chi phí = số pixel lệch (cả cột của mặt nạ 2 bị bỏ qua khi d tăng).
        const int MaxShift = 4;
        int w1 = m1.Bounds.Width, w2 = m2.Bounds.Width, ink1 = m1.Ink.Count(p => p), ink2 = m2.Ink.Count(p => p);
        int best = int.MaxValue;
        for (int dy = -2; dy <= 2; dy++)
        {
            var dp = new int[2 * MaxShift + 1];
            for (int x = 0; x < w1; x++)
            {
                var next = new int[dp.Length];
                for (int k = 0; k < dp.Length; k++)
                {
                    int d = k - MaxShift;
                    int cost = 0;
                    for (int y = Math.Min(0, dy); y < Math.Max(m1.Bounds.Height, m2.Bounds.Height + dy); y++)
                    {
                        cost += m1.At(x, y) != m2.At(x + d, y - dy) ? 1 : 0;
                    }
                    if (x == 0)
                    {
                        next[k] = Math.Abs(d) <= 1 ? cost : int.MaxValue / 2;
                        continue;
                    }
                    int prev = dp[k];
                    if (k > 0)
                    {
                        prev = Math.Min(prev, dp[k - 1] + ColumnInk(m2, x + d - 1)); // d tăng: bỏ qua 1 cột mặt nạ 2
                    }
                    if (k < dp.Length - 1)
                    {
                        prev = Math.Min(prev, dp[k + 1]);
                    }
                    next[k] = prev + cost;
                }
                dp = next;
            }
            for (int k = 0; k < dp.Length; k++)
            {
                if (Math.Abs(w1 - 1 + k - MaxShift - (w2 - 1)) <= 1)
                {
                    best = Math.Min(best, dp[k]);
                }
            }
        }
        return best <= 0.3 * (ink1 + ink2) / 2 ? m2.Bounds : null;

        static int ColumnInk(Mask m, int x)
        {
            int n = 0;
            for (int y = 0; y < m.Bounds.Height; y++)
            {
                n += m.At(x, y) ? 1 : 0;
            }
            return n;
        }
    }

    /// <summary><see cref="InkMask"/>, nới vùng thêm 2 px về phía nào nét còn chạm mép (khung OCR / vị trí dự đoán hay
    /// cắt hụt chữ - "枚" dự đoán lệch 5 px), tối đa 8 lần. Khoảng trống 1 cột không nét là dừng nên viền ô cách chữ
    /// không bị nuốt.</summary>
    private static Mask? GrownMask(SKBitmap bitmap, SKRectI area)
    {
        var image = SKRectI.Create(bitmap.Width, bitmap.Height);
        area = SKRectI.Intersect(area, image);
        var mask = InkMask(bitmap, area);
        for (int i = 0; i < 8 && mask is not null; i++)
        {
            var b = mask.Bounds;
            var grown = new SKRectI(
                b.Left == area.Left ? area.Left - 2 : area.Left, b.Top == area.Top ? area.Top - 2 : area.Top,
                b.Right == area.Right ? area.Right + 2 : area.Right, b.Bottom == area.Bottom ? area.Bottom + 2 : area.Bottom);
            grown = SKRectI.Intersect(grown, image);
            if (grown == area)
            {
                break;
            }
            area = grown;
            mask = InkMask(bitmap, area);
        }
        return mask;
    }

    private sealed record Mask(bool[] Ink, SKRectI Bounds)
    {
        public bool At(int x, int y) => x >= 0 && y >= 0 && x < Bounds.Width && y < Bounds.Height && Ink[y * Bounds.Width + x];
    }

    /// <summary>Mặt nạ nét trong vùng, cắt sát theo khung nét (toạ độ ảnh trong <see cref="Mask.Bounds"/>). Nền = độ sáng
    /// hay gặp nhất. Null nếu vùng trống / quá ít nét.</summary>
    private static unsafe Mask? InkMask(SKBitmap bitmap, SKRectI area)
    {
        area = SKRectI.Intersect(area, SKRectI.Create(bitmap.Width, bitmap.Height));
        if (area.Width < 3 || area.Height < 3)
        {
            return null;
        }
        var luma = new float[area.Width * area.Height];
        var hist = new int[32];
        byte* basePtr = (byte*)bitmap.GetPixels();
        for (int y = 0; y < area.Height; y++)
        {
            uint* row = (uint*)(basePtr + (long)(area.Top + y) * bitmap.RowBytes);
            for (int x = 0; x < area.Width; x++)
            {
                float l = ImageUtil.Luma(row[area.Left + x]);
                luma[y * area.Width + x] = l;
                hist[Math.Clamp((int)l / 8, 0, 31)]++;
            }
        }
        float bg = Array.IndexOf(hist, hist.Max()) * 8 + 4;
        float maxDiff = luma.Max(l => Math.Abs(l - bg));
        if (maxDiff < 40)
        {
            return null;
        }
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
        var ink = new bool[luma.Length];
        for (int i = 0; i < luma.Length; i++)
        {
            if (Math.Abs(luma[i] - bg) >= 0.4f * maxDiff)
            {
                ink[i] = true;
                count++;
                int x = i % area.Width, y = i / area.Width;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        }
        if (count < 6)
        {
            return null;
        }
        int w = maxX - minX + 1, h = maxY - minY + 1;
        var cropped = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            Array.Copy(ink, (minY + y) * area.Width + minX, cropped, y * w, w);
        }
        return new Mask(cropped, SKRectI.Create(area.Left + minX, area.Top + minY, w, h));
    }

    private static double Luma(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;

    private static string Hex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

    /// <summary>Vùng đọc lại 1 phía của mục 2 phía: khung của phía đó nới ra cho bằng khung phía kia (nới đều 2 bên).
    /// OCR cả trang có khi chỉ bắt được 1 phần chữ ("商印その他" → "商印そ", khung chỉ phủ 3 chữ đầu) - cắt đúng khung
    /// đó thì đọc lại kiểu gì cũng thiếu chữ.</summary>
    private static SKRectI? Region(TextSegment? own, TextSegment? other)
    {
        if (own is null)
        {
            return null;
        }
        var r = own.Bounds;
        if (other is not null)
        {
            int dx = Math.Max(0, other.Bounds.Width - r.Width), dy = Math.Max(0, other.Bounds.Height - r.Height);
            r = SKRectI.Inflate(r, dx, (dy + 1) / 2);
        }
        return r;
    }

    /// <summary>Mục 2 phía có cách đọc trùng nhau: có phần chữ chung (<see cref="Common"/>) VÀ chữ chung phải na ná chữ của chính mục này (giống ≥ 1/2 với A hoặc B): vùng cắt dính
    /// nhãn bên cạnh mà OCR chỉ đọc ra nhãn đó ở cả 2 phía thì không được tính là "giống" (giá trị đổi thật bị bỏ sót).</summary>
    /// <returns>Cách đọc trùng (null nếu không có).</returns>
    private static (string Text, string Key)? Matches(TextDiffItem item, List<(string Text, string Key)> readsA, List<(string Text, string Key)> readsB)
    {
        string keyA = item.A!.Key, keyB = item.B!.Key;
        // Phía có vùng cắt được nới (khung OCR hẹp hơn - xem Region) mới được đọc dư chữ.
        bool widenedA = item.A!.Bounds.Width < item.B!.Bounds.Width, widenedB = item.B.Bounds.Width < item.A.Bounds.Width;
        foreach (var ra in readsA)
        {
            foreach (var rb in readsB)
            {
                if (Common(ra, rb, widenedA, widenedB) is { } inner
                    && (TextDiff.Similarity(inner.Key, keyA) >= 0.5 || TextDiff.Similarity(inner.Key, keyB) >= 0.5))
                {
                    return inner;
                }
            }
        }
        return null;
    }

    /// <summary>Phần chữ chung của 2 cách đọc: trùng hẳn, hoặc 1 bên nằm trọn trong bên kia (≥ 3 ký tự) mà phần dư không
    /// có chữ số. Vùng đọc lại được nới cho bằng khung phía kia nên hay dính nhãn bên cạnh ("区分商印その他" ↔ "商印その他",
    /// "法営業売上" ↔ "営業売上" - đã gặp); còn "200" ↔ "1200" phần dư là số → khác thật. Bên dư chữ phải là bên có vùng
    /// cắt được nới (<paramref name="xMayHaveExtra"/> / <paramref name="yMayHaveExtra"/>): "商印その" ↔ "商印その他" mà
    /// chữ dư nằm ở phía rộng hơn sẵn thì là chữ thêm thật.</summary>
    private static (string Text, string Key)? Common((string Text, string Key) x, (string Text, string Key) y, bool xMayHaveExtra, bool yMayHaveExtra)
    {
        if (x.Key == y.Key)
        {
            return x;
        }
        bool xOuter = x.Key.Length > y.Key.Length;
        if (!(xOuter ? xMayHaveExtra : yMayHaveExtra))
        {
            return null;
        }
        var (inner, outer) = xOuter ? (y, x) : (x, y);
        int at = outer.Key.IndexOf(inner.Key, StringComparison.Ordinal);
        if (inner.Key.Length < 3 || at < 0)
        {
            return null;
        }
        string extra = outer.Key[..at] + outer.Key[(at + inner.Key.Length)..];
        return extra.Any(char.IsAsciiDigit) ? null : inner;
    }

    /// <summary>Mục còn khác: lấy cặp cách đọc sát nhau nhất làm chữ hiển thị (thay bản đọc cả trang có thể là rác),
    /// phân loại lại gần giống / đổi chữ theo cặp đó.</summary>
    private static TextDiffItem Closest(TextDiffItem item, List<(string Text, string Key)> readsA, List<(string Text, string Key)> readsB)
    {
        if (item.A is null || item.B is null)
        {
            return item;
        }
        // Chỉ chọn trong các cách đọc na ná chữ của chính phía đó (bản đọc cả trang luôn được tính) - cách đọc dính nhãn
        // bên cạnh trùng ở 2 phía sẽ hiện "X → X" vô nghĩa (đã gặp trong test).
        var plausibleA = readsA.Where(r => r.Key == item.A.Key || TextDiff.Similarity(r.Key, item.A.Key) >= 0.5).ToList();
        var plausibleB = readsB.Where(r => r.Key == item.B.Key || TextDiff.Similarity(r.Key, item.B.Key) >= 0.5).ToList();
        var candidates = plausibleA.SelectMany(ra => plausibleB.Select(rb => (A: ra, B: rb, D: TextDiff.Levenshtein(ra.Key, rb.Key))))
            .Where(t => t.A.Key != t.B.Key)
            // Có 1 cặp cách đọc chỉ lệch kiểu OCR (cùng chữ số) → ưu tiên: các cách đọc khác của cùng chữ có khi lệch cả chữ
            // số ("JF60紹証製品" ↔ "JFS0紹証製品" khi "FSC認証製品" ở 2 phía vẫn là 1 nhãn - đã gặp).
            .OrderByDescending(t => TextDiff.IsLikelyOcrNoise(t.A.Key, t.B.Key)).ThenBy(t => t.D).ThenByDescending(t => t.A.Key.Length + t.B.Key.Length)
            .ToList();
        if (candidates.Count == 0)
        {
            return item;
        }
        var best = candidates[0];
        var kind = TextDiff.IsLikelyOcrNoise(best.A.Key, best.B.Key) ? TextDiffKind.Similar : TextDiffKind.Changed;
        return item with
        {
            Kind = kind,
            A = item.A with { Text = best.A.Text, Key = best.A.Key },
            B = item.B with { Text = best.B.Text, Key = best.B.Key },
        };
    }

    /// <summary>Các cách đọc 1 vùng: bản đọc cả trang (nếu có) + đọc riêng vùng cắt ở mỗi mức phóng. Vùng dự đoán (phía
    /// không thấy chữ) cắt rộng hơn vì vị trí chỉ là ước lượng.</summary>
    private static List<(string Text, string Key)> Readings(SKBitmap bitmap, SKRectI? region, bool predicted, TextSegment? segment,
        IReadOnlyList<IFormTextReader> readers, Func<List<(string Text, string Key)>, bool>? stop)
    {
        var reads = new List<(string Text, string Key)>();
        if (segment is not null)
        {
            reads.Add((segment.Text, segment.Key));
        }
        if (region is not { } r)
        {
            return reads;
        }
        int padX = predicted ? 24 : 6, padY = predicted ? 6 : 3;
        var area = SKRectI.Intersect(SKRectI.Inflate(r, padX, padY), SKRectI.Create(bitmap.Width, bitmap.Height));
        if (area.Width < 4 || area.Height < 4)
        {
            return reads;
        }
        using var crop = new SKBitmap(new SKImageInfo(area.Width, area.Height, ImageUtil.ColorType, ImageUtil.AlphaType));
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, area, SKRect.Create(area.Width, area.Height));
        }
        foreach (var reader in readers)
        {
            foreach (var (scale, threshold) in Variants)
            {
                string text = reader.ReadText(crop, scale, threshold);
                string key = TextDiff.Key(text);
                if (key.Length > 0 && !reads.Any(x => x.Key == key))
                {
                    reads.Add((text, key));
                    if (stop?.Invoke(reads) == true)
                    {
                        return reads;
                    }
                }
            }
        }
        return reads;
    }
}
