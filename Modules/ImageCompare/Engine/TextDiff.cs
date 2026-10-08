using System.Globalization;
using System.Text;
using Common.Ocr;
using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Loại khác biệt ở chế độ So chữ.</summary>
public enum TextDiffKind
{
    /// <summary>Cùng chỗ nhưng chữ khác (vd giá trị đổi).</summary>
    Changed,
    /// <summary>Có ở A, không thấy ở B.</summary>
    OnlyInA,
    /// <summary>Có ở B, không thấy ở A.</summary>
    OnlyInB,
    /// <summary>Chữ giống nhau nhưng màu chữ khác (vd ô bị khoá chữ xám ở A, chữ đen ở B).</summary>
    ColorChanged,
    /// <summary>Chỉ khác 1 ký tự - nhiều khả năng do OCR đọc lệch, không phải khác thật. Mặc định ẩn.</summary>
    Similar,
}

/// <summary>1 đoạn chữ liền (1 nhãn / 1 giá trị). <see cref="Bounds"/> theo toạ độ ảnh chứa nó;
/// <see cref="Key"/> = chữ đã chuẩn hoá để so (xem <see cref="TextDiff.Key"/>).</summary>
public sealed record TextSegment(string Text, string Key, SKRectI Bounds);

/// <summary>1 chỗ khác. <see cref="A"/> / <see cref="B"/> null khi chỉ có ở ảnh kia; khung theo toạ độ ảnh của nó.
/// <see cref="Other"/>: với mục chỉ có ở 1 phía - chỗ dự đoán ở ảnh kia (toạ độ ảnh kia).</summary>
public sealed record TextDiffItem(int Number, TextDiffKind Kind, TextSegment? A, TextSegment? B, string? Note = null, SKRectI? Other = null)
{
    /// <summary>Mục 2 phía mà OCR đọc mỗi phía 1 kiểu nhưng nét chữ 2 phía trùng khít từng chữ (<see
    /// cref="TextDiffVerifier.SameShapeStrict"/>) - gần như chắc chắn là chữ giống (chữ xám ô bị khoá, viền ô IE mode ↔ Edge
    /// khác nhau). Màn hình ẩn các mục này khi bật "So nét chữ".</summary>
    public bool SameGlyphs { get; init; }

    /// <summary>Khung bao cả 2 phía theo toạ độ ảnh A (B dịch <paramref name="offsetB"/>) - để phóng tới.</summary>
    public SKRectI BoundsInA(SKPointI offsetB)
    {
        SKRectI? box = A?.Bounds;
        if (B is { } b)
        {
            var rb = b.Bounds;
            rb.Offset(offsetB);
            if (box is { } a)
            {
                a.Union(rb);
                box = a;
            }
            else
            {
                box = rb;
            }
        }
        return box ?? SKRectI.Empty;
    }
}

/// <param name="VerifiedSame">Số chỗ ban đầu nghi khác mà đọc lại riêng từng vùng thì ra giống (<see cref="TextDiffVerifier"/>).</param>
public sealed record TextDiffResult(IReadOnlyList<TextDiffItem> Items, int SegmentsA, int SegmentsB, int SameCount, SKPointI OffsetB, int VerifiedSame = 0)
{
    public int Count(TextDiffKind kind) => Items.Count(i => i.Kind == kind);
}

/// <summary>So chữ giữa 2 ảnh chụp cùng 1 màn hình ở 2 môi trường (font / độ rộng ô khác → bố cục xê dịch cục bộ,
/// so pixel vô nghĩa). Đầu vào là kết quả OCR 2 ảnh; không phụ thuộc OCR nào → test được bằng dữ liệu giả.
///
/// 1. Tách đoạn: 1 dòng OCR trên form chứa nhiều ô ("受注日付 230107 下版日 230125") → cắt ở khoảng trống ngang lớn.
/// 2. Khoá so (<see cref="Key"/>): NFKC (gộp toàn / nửa độ rộng), bỏ khoảng trắng + vết viền ô, gộp nhóm ký tự OCR
///    hay nhầm (0/O/ロ/口, 1/l/I, -/ー/一, カ/力...). Cùng 1 chữ mà 2 lần đọc ra khác nhau vẫn khớp.
/// 3. Ghép cặp theo độ lệch CỤC BỘ: các đoạn có khoá duy nhất ở cả 2 ảnh làm mốc → mỗi đoạn dự đoán vị trí ở B
///    theo các mốc gần nhất (B trôi dần tới vài chục px, 1 độ lệch chung không đủ). Thứ tự: giống hệt gần nhau →
///    1 đoạn ứng với nhiều đoạn liền nhau ở ảnh kia (OCR cắt khác) → gần giống (Levenshtein) → còn lại là chỉ có 1 phía.
/// 4. Cặp giống chữ: so màu nét chữ (phần tối nhất của nét, bỏ viền khử răng cưa) → "khác màu chữ".</summary>
public static class TextDiff
{
    /// <summary>Lệch dự đoán cho phép khi ghép (px ảnh A) - ngang rộng hơn dọc vì độ rộng ô thay đổi nhiều hơn.</summary>
    private const int MatchToleranceX = 40, MatchToleranceY = 18;

    /// <summary>Mốc (khoá duy nhất) chỉ nhận khi cách vị trí theo độ lệch chung không quá chừng này.</summary>
    private const int AnchorMaxDistance = 250;

    /// <summary>Độ giống tối thiểu (1 - Levenshtein / độ dài) để coi 2 đoạn cùng chỗ là "chữ bị đổi" chứ không phải
    /// 2 thứ khác nhau.</summary>
    private const double MinChangedSimilarity = 0.5;

    /// <summary>Chênh độ sáng nét chữ (0–255) từ mức này là khác màu (chữ xám ~110 vs chữ đen ~20).</summary>
    private const double ColorLumaDelta = 45, ColorRgbDelta = 80;

    public static TextDiffResult Compare(OcrResult a, OcrResult b, SKPointI offsetB,
        IReadOnlyList<SKRectI>? ignoreRects = null, SKBitmap? bitmapA = null, SKBitmap? bitmapB = null,
        CancellationToken ct = default)
    {
        ignoreRects ??= [];
        var segA = Segments(a, bitmapA).Where(s => !Ignored(s.Bounds, SKPointI.Empty, ignoreRects)).ToList();
        var segB = Segments(b, bitmapB).Where(s => !Ignored(s.Bounds, offsetB, ignoreRects)).ToList();
        // Toạ độ B quy về A (độ lệch chung) để so vị trí.
        var posB = segB.Select(s => { var r = s.Bounds; r.Offset(offsetB); return r; }).ToList();
        var usedA = new bool[segA.Count];
        var usedB = new bool[segB.Count];
        var pairs = new List<(List<int> A, List<int> B)>();

        // 1. Mốc: khoá duy nhất ở cả 2 ảnh.
        var anchors = new List<(SKPoint At, SKPoint Shift)>();
        var onceA = segA.Select((s, i) => (s.Key, i)).Where(t => t.Key.Length >= 2).GroupBy(t => t.Key).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().i);
        var onceB = segB.Select((s, i) => (s.Key, i)).Where(t => t.Key.Length >= 2).GroupBy(t => t.Key).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().i);
        foreach (var (key, ia) in onceA)
        {
            if (onceB.TryGetValue(key, out int ib) && Distance(Center(segA[ia].Bounds), Center(posB[ib])) <= AnchorMaxDistance)
            {
                usedA[ia] = usedB[ib] = true;
                pairs.Add(([ia], [ib]));
                anchors.Add((Center(segA[ia].Bounds), Center(posB[ib]) - Center(segA[ia].Bounds)));
            }
        }
        ct.ThrowIfCancellationRequested();

        SKPoint Predict(SKRectI boundsA) => PredictShift(anchors, Center(boundsA));
        bool Near(SKRectI boundsA, SKRectI boundsBInA, SKPoint shift)
        {
            var expected = Center(boundsA) + shift;
            var actual = Center(boundsBInA);
            return Math.Abs(actual.X - expected.X) <= MatchToleranceX + boundsA.Width / 4f
                && Math.Abs(actual.Y - expected.Y) <= MatchToleranceY;
        }

        // 2. Giống hệt, gần vị trí dự đoán.
        for (int i = 0; i < segA.Count; i++)
        {
            if (usedA[i])
            {
                continue;
            }
            var shift = Predict(segA[i].Bounds);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int j = 0; j < segB.Count; j++)
            {
                if (usedB[j] || segB[j].Key != segA[i].Key || !Near(segA[i].Bounds, posB[j], shift))
                {
                    continue;
                }
                float d = Distance(Center(segA[i].Bounds) + shift, Center(posB[j]));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = j;
                }
            }
            if (best >= 0)
            {
                usedA[i] = usedB[best] = true;
                pairs.Add(([i], [best]));
            }
        }
        ct.ThrowIfCancellationRequested();

        // 3. 1 đoạn ↔ nhiều đoạn liền nhau cùng dòng ở ảnh kia (OCR cắt đoạn khác nhau ở 2 ảnh).
        MergeRuns(segA, segB, posB, usedA, usedB, pairs, Predict, reverse: false);
        MergeRuns(segA, segB, posB, usedA, usedB, pairs, Predict, reverse: true);

        // 4. Gần giống ở cùng chỗ → chữ bị đổi (hoặc OCR lệch 1 ký tự).
        var candidates = new List<(int I, int J, double Similarity, float Distance)>();
        for (int i = 0; i < segA.Count; i++)
        {
            if (usedA[i])
            {
                continue;
            }
            var shift = Predict(segA[i].Bounds);
            for (int j = 0; j < segB.Count; j++)
            {
                if (usedB[j] || !Near(segA[i].Bounds, posB[j], shift))
                {
                    continue;
                }
                double sim = Similarity(segA[i].Key, segB[j].Key);
                if (sim >= MinChangedSimilarity)
                {
                    candidates.Add((i, j, sim, Distance(Center(segA[i].Bounds) + shift, Center(posB[j]))));
                }
            }
        }
        foreach (var c in candidates.OrderByDescending(c => c.Similarity).ThenBy(c => c.Distance))
        {
            if (!usedA[c.I] && !usedB[c.J])
            {
                usedA[c.I] = usedB[c.J] = true;
                pairs.Add(([c.I], [c.J]));
            }
        }

        // 5. Còn lẻ mà nằm đúng cùng chỗ (lệch ≤ nửa sai số) và là gần nhất của nhau → cùng 1 ô, chữ khác hẳn
        //    (vd OCR đọc "200" thành "2圓", độ giống 0,33): 1 mục "đổi chữ" thay vì 2 mục chỉ-A + chỉ-B.
        var closeA = new int[segA.Count];
        var closeB = new int[segB.Count];
        Array.Fill(closeA, -1);
        Array.Fill(closeB, -1);
        var bestA = new float[segA.Count];
        var bestB = new float[segB.Count];
        Array.Fill(bestA, float.MaxValue);
        Array.Fill(bestB, float.MaxValue);
        for (int i = 0; i < segA.Count; i++)
        {
            if (usedA[i])
            {
                continue;
            }
            var expected = Center(segA[i].Bounds) + Predict(segA[i].Bounds);
            for (int j = 0; j < segB.Count; j++)
            {
                var actual = Center(posB[j]);
                if (usedB[j] || Math.Abs(actual.X - expected.X) > MatchToleranceX / 2f + segA[i].Bounds.Width / 4f
                    || Math.Abs(actual.Y - expected.Y) > MatchToleranceY / 2f)
                {
                    continue;
                }
                float d = Distance(expected, actual);
                if (d < bestA[i])
                {
                    bestA[i] = d;
                    closeA[i] = j;
                }
                if (d < bestB[j])
                {
                    bestB[j] = d;
                    closeB[j] = i;
                }
            }
        }
        for (int i = 0; i < segA.Count; i++)
        {
            int j = closeA[i];
            if (j >= 0 && closeB[j] == i && !usedA[i] && !usedB[j])
            {
                usedA[i] = usedB[j] = true;
                pairs.Add(([i], [j]));
            }
        }

        // 6. Mảnh lẻ nằm gọn trong khung của 1 cặp chữ khác nhau ở ảnh kia, sát đoạn cùng cặp ở ảnh mình → OCR cắt 1 ô
        //    thành nhiều mảnh ở 1 ảnh (A: "へ" + "ゾ数", B: "へ叮ゾ数" - đã gặp): nhập vào cặp thay vì thêm 1 mục chỉ-1-phía.
        AbsorbFragments(segA, segB, posB, usedA, usedB, pairs, Predict);

        // 7. Phân loại.
        var items = new List<(TextDiffKind Kind, TextSegment? A, TextSegment? B, string? Note, SKRectI SortBox, SKRectI? Other)>();
        int same = 0;
        foreach (var (ia, ib) in pairs)
        {
            var sa = Merge(ia.Select(i => segA[i]).ToList());
            var sb = Merge(ib.Select(j => segB[j]).ToList());
            if (sa.Key == sb.Key)
            {
                if (bitmapA is not null && bitmapB is not null
                    && InkColor(bitmapA, sa.Bounds) is { } ca && InkColor(bitmapB, sb.Bounds) is { } cb && ColorsDiffer(ca, cb))
                {
                    items.Add((TextDiffKind.ColorChanged, sa, sb, $"{Hex(ca)} → {Hex(cb)}", sa.Bounds, null));
                }
                else
                {
                    same++;
                }
                continue;
            }
            var kind = IsLikelyOcrNoise(sa.Key, sb.Key) ? TextDiffKind.Similar : TextDiffKind.Changed;
            items.Add((kind, sa, sb, null, sa.Bounds, null));
        }
        // Chỉ có ở 1 phía: ghi chỗ dự đoán ở ảnh kia (toạ độ ảnh kia) để kiểm tra lại bằng cách đọc riêng vùng đó.
        for (int i = 0; i < segA.Count; i++)
        {
            if (!usedA[i])
            {
                var shift = Predict(segA[i].Bounds);
                var other = segA[i].Bounds;
                other.Offset((int)MathF.Round(shift.X) - offsetB.X, (int)MathF.Round(shift.Y) - offsetB.Y);
                items.Add((TextDiffKind.OnlyInA, segA[i], null, null, segA[i].Bounds, other));
            }
        }
        for (int j = 0; j < segB.Count; j++)
        {
            if (!usedB[j])
            {
                var shift = Predict(posB[j]);
                var other = posB[j];
                other.Offset(-(int)MathF.Round(shift.X), -(int)MathF.Round(shift.Y));
                items.Add((TextDiffKind.OnlyInB, null, segB[j], null, posB[j], other));
            }
        }

        var ordered = items
            .OrderBy(t => t.SortBox.Top / 12).ThenBy(t => t.SortBox.Left) // trên → dưới, cùng "dòng" thì trái → phải
            .Select((t, n) => new TextDiffItem(n + 1, t.Kind, t.A, t.B, t.Note, t.Other))
            .ToList();
        return new TextDiffResult(ordered, segA.Count, segB.Count, same, offsetB);
    }

    /// <summary>Đoạn ở 1 phía chưa ghép mà chữ ghép từ 2–4 đoạn liền nhau (cùng dòng, theo thứ tự trái → phải) ở phía
    /// kia thì ghép chung. <paramref name="reverse"/>: 1 đoạn B ↔ nhiều đoạn A.</summary>
    private static void MergeRuns(List<TextSegment> segA, List<TextSegment> segB, List<SKRectI> posB, bool[] usedA, bool[] usedB,
        List<(List<int> A, List<int> B)> pairs, Func<SKRectI, SKPoint> predict, bool reverse)
    {
        int countOne = reverse ? segB.Count : segA.Count;
        for (int i = 0; i < countOne; i++)
        {
            if (reverse ? usedB[i] : usedA[i])
            {
                continue;
            }
            // Vị trí theo toạ độ "phía nhiều": nhiều = B → dịch theo dự đoán; nhiều = A → ngược lại.
            SKRectI one = reverse ? posB[i] : segA[i].Bounds;
            var shift = reverse ? new SKPoint(-predict(one).X, -predict(one).Y) : predict(one);
            var expected = SKRect.Create(one.Left + shift.X - MatchToleranceX, one.Top + shift.Y - MatchToleranceY,
                one.Width + 2 * MatchToleranceX, one.Height + 2 * MatchToleranceY);
            var others = Enumerable.Range(0, reverse ? segA.Count : segB.Count)
                .Where(j => !(reverse ? usedA[j] : usedB[j]))
                .Select(j => (j, Box: reverse ? segA[j].Bounds : posB[j]))
                .Where(t => expected.Contains(Center(t.Box)))
                .OrderBy(t => t.Box.Left)
                .ToList();
            string target = reverse ? segB[i].Key : segA[i].Key;
            for (int start = 0; start < others.Count; start++)
            {
                var key = new StringBuilder();
                bool found = false;
                for (int end = start; end < others.Count && end < start + 4; end++)
                {
                    key.Append(reverse ? segA[others[end].j].Key : segB[others[end].j].Key);
                    if (end > start && key.ToString() == target)
                    {
                        var many = others.Skip(start).Take(end - start + 1).Select(t => t.j).ToList();
                        foreach (int j in many)
                        {
                            if (reverse) usedA[j] = true; else usedB[j] = true;
                        }
                        if (reverse) usedB[i] = true; else usedA[i] = true;
                        pairs.Add(reverse ? (many, [i]) : ([i], many));
                        found = true;
                        break;
                    }
                    if (key.Length >= target.Length)
                    {
                        break;
                    }
                }
                if (found)
                {
                    break;
                }
            }
        }
    }

    private static void AbsorbFragments(List<TextSegment> segA, List<TextSegment> segB, List<SKRectI> posB, bool[] usedA, bool[] usedB,
        List<(List<int> A, List<int> B)> pairs, Func<SKRectI, SKPoint> predict)
    {
        var differing = pairs.Where(p => string.Concat(p.A.Select(i => segA[i].Key)) != string.Concat(p.B.Select(j => segB[j].Key))).ToList();
        for (int i = 0; i < segA.Count; i++)
        {
            if (!usedA[i] && FindHost(segA[i].Bounds, Center(segA[i].Bounds) + predict(segA[i].Bounds),
                    p => p.A.Select(k => segA[k].Bounds), p => p.B.Select(j => posB[j])) is { } host)
            {
                host.A.Add(i);
                usedA[i] = true;
            }
        }
        for (int j = 0; j < segB.Count; j++)
        {
            if (!usedB[j] && FindHost(posB[j], Center(posB[j]) - predict(posB[j]),
                    p => p.B.Select(k => posB[k]), p => p.A.Select(i => segA[i].Bounds)) is { } host)
            {
                host.B.Add(j);
                usedB[j] = true;
            }
        }

        // Cặp mà khung phía kia chứa vị trí dự đoán của mảnh (nới dọc MatchToleranceY/2 - dự đoán dọc lệch được vài px), và
        // có đoạn cùng phía cùng dòng cách mảnh ≤ 1 chiều cao chữ.
        (List<int> A, List<int> B)? FindHost(SKRectI own, SKPoint expected,
            Func<(List<int> A, List<int> B), IEnumerable<SKRectI>> sameSide, Func<(List<int> A, List<int> B), IEnumerable<SKRectI>> otherSide)
        {
            foreach (var p in differing)
            {
                var other = otherSide(p).Aggregate((r, q) => { r.Union(q); return r; });
                if (expected.X < other.Left || expected.X > other.Right || Math.Abs(expected.Y - Center(other).Y) > other.Height / 2f + MatchToleranceY / 2f)
                {
                    continue;
                }
                // Chiều cao chữ lấy theo đoạn cao hơn: mảnh có thể là 1 ký tự dẹt ("へ" cao ~5 px).
                if (sameSide(p).Any(r => Math.Max(r.Left - own.Right, own.Left - r.Right) <= Math.Max(own.Height, r.Height)
                    && Math.Min(r.Bottom, own.Bottom) - Math.Max(r.Top, own.Top) > Math.Min(own.Height, r.Height) / 2))
                {
                    return p;
                }
            }
            return null;
        }
    }

    /// <summary>Tách các dòng OCR thành đoạn tại khoảng trống ngang lớn hơn ~0,8 chiều cao chữ (khoảng giữa nhãn và
    /// ô giá trị; khoảng giữa 2 ký tự / 2 từ thường nhỏ hơn nhiều), và - khi có ảnh - tại viền ô nằm giữa 2 từ: nhãn
    /// sát combobox thì khoảng trống nhỏ, OCR (đọc trên ảnh đã xoá viền) gộp "受注区分 受注" thành 1 đoạn (đã gặp).</summary>
    public static List<TextSegment> Segments(OcrResult ocr, SKBitmap? bitmap = null)
    {
        var result = new List<TextSegment>();
        foreach (var line in ocr.Lines)
        {
            var words = line.Words.Where(w => w.Text.Length > 0).OrderBy(w => w.Bounds.Left).ToList();
            if (words.Count == 0)
            {
                continue;
            }
            var heights = words.Select(w => w.Bounds.Height).OrderBy(h => h).ToList();
            float gapLimit = Math.Max(6, heights[heights.Count / 2] * 0.8f);
            var current = new List<OcrWord> { words[0] };
            for (int k = 1; k < words.Count; k++)
            {
                if (words[k].Bounds.Left - current[^1].Bounds.Right > gapLimit
                    || (bitmap is not null && HasBorderBetween(bitmap, current[^1].Bounds, words[k].Bounds)))
                {
                    Add(current);
                    current = [];
                }
                current.Add(words[k]);
            }
            Add(current);
        }
        return result;

        void Add(List<OcrWord> words)
        {
            string text = OcrText.JoinWords(words.Select(w => w.Text));
            string key = Key(text);
            if (key.Length == 0)
            {
                return; // chỉ có vết viền / dấu câu lẻ
            }
            var bounds = words[0].Bounds;
            foreach (var w in words)
            {
                bounds.Union(w.Bounds);
            }
            result.Add(new TextSegment(text, key, bounds));
        }
    }

    /// <summary>Khoá so: NFKC (toàn / nửa độ rộng: "ｵｰﾊﾞｰ" = "オーバー", "１" = "1"), chữ thường, bỏ khoảng trắng và vết
    /// viền ô OCR hay đọc thành ký tự (| [ ] _; viền nút: 「 」 【 】 〔 〕 \ - đã gặp "「その他金額」" ↔ "その他金額\"),
    /// gộp nhóm ký tự OCR hay nhầm về 1 đại diện.</summary>
    public static string Key(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c0 in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(c0) || c0 is '|' or '[' or ']' or '_' or '｜' or '「' or '」' or '【' or '】' or '〔' or '〕' or '\\')
            {
                continue;
            }
            char c = char.ToLower(c0, CultureInfo.InvariantCulture);
            sb.Append(Fold(c));
        }
        return sb.ToString();
    }

    private static char Fold(char c) => c switch
    {
        'o' or '〇' or '○' or 'ロ' or '口' => '0',
        'l' or 'i' or '丨' or 'ı' => '1',
        'ー' or '一' or '‐' or '‑' or '–' or '—' or '―' or '−' or '－' => '-',
        '力' => 'カ',
        '工' => 'エ',
        '二' => 'ニ',
        '卜' => 'ト',
        '夕' => 'タ',
        '八' => 'ハ',
        'ヘ' => 'へ',
        // Dấu chấm / phẩy: OCR hay đọc lẫn ("1,360,000" → "1.360.000" - gặp khi thử Tesseract trên số tiền).
        '。' or '、' or ',' or '・' or '·' => '.',
        _ => c,
    };

    public static double Similarity(string a, string b)
    {
        int max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1 : 1 - (double)Levenshtein(a, b) / max;
    }

    /// <summary>Lệch ít ký tự (≤ 1/5 độ dài, chuỗi ≥ 4 ký tự) mà phần chữ số giống hệt → nhiều khả năng OCR đọc lệch
    /// 1 nhãn chứ không phải khác thật. Chữ số khác (giá trị, ngày, số tiền) luôn coi là đổi thật, dù chỉ lệch 1 số.
    /// (Thử trên màn hình thật bằng Tesseract: nhãn / tiêu đề dài hay lệch 1–3 ký tự giữa 2 lần đọc.)</summary>
    internal static bool IsLikelyOcrNoise(string a, string b)
    {
        int max = Math.Max(a.Length, b.Length);
        if (max < 4 || !string.Equals(Digits(a), Digits(b), StringComparison.Ordinal))
        {
            return false;
        }
        int distance = Levenshtein(a, b);
        return distance >= 1 && distance <= Math.Max(1, max / 5);

        static string Digits(string s) => new(s.Where(char.IsAsciiDigit).ToArray());
    }

    public static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Lệch dự đoán tại <paramref name="at"/>: trung bình có trọng số (nghịch đảo khoảng cách) của 4 mốc gần nhất.</summary>
    private static SKPoint PredictShift(List<(SKPoint At, SKPoint Shift)> anchors, SKPoint at)
    {
        if (anchors.Count == 0)
        {
            return SKPoint.Empty;
        }
        float sx = 0, sy = 0, sw = 0;
        foreach (var (pos, shift) in anchors.OrderBy(t => Distance(t.At, at)).Take(4))
        {
            float w = 1 / (Distance(pos, at) + 20);
            sx += shift.X * w;
            sy += shift.Y * w;
            sw += w;
        }
        return new SKPoint(sx / sw, sy / sw);
    }

    private static TextSegment Merge(List<TextSegment> parts)
    {
        if (parts.Count == 1)
        {
            return parts[0];
        }
        var ordered = parts.OrderBy(p => p.Bounds.Left).ToList();
        var bounds = ordered[0].Bounds;
        foreach (var p in ordered)
        {
            bounds.Union(p.Bounds);
        }
        return new TextSegment(OcrText.JoinWords(ordered.Select(p => p.Text)), string.Concat(ordered.Select(p => p.Key)), bounds);
    }

    private static bool Ignored(SKRectI bounds, SKPointI offset, IReadOnlyList<SKRectI> ignoreRects)
    {
        var c = Center(bounds) + new SKPoint(offset.X, offset.Y);
        return ignoreRects.Any(r => c.X >= r.Left && c.X < r.Right && c.Y >= r.Top && c.Y < r.Bottom);
    }

    private static SKPoint Center(SKRectI r) => new(r.Left + r.Width / 2f, r.Top + r.Height / 2f);

    private static float Distance(SKPoint p, SKPoint q) => (p - q).Length;

    // ---- Viền ô ----

    /// <summary>Chênh độ sáng 2 cột pixel kề nhau từ mức này là 1 bậc (viền ô / mép nền ô khác nền trang).</summary>
    private const int BorderStep = 24;

    /// <summary>Dòng xét viền nới thêm trên / dưới chiều cao chữ: viền ô cao hơn chữ, còn nét dọc của 1 ký tự mà khung
    /// từ OCR bỏ sót (đã gặp: nửa trái chữ 版 của "水性ﾆｽ版1") thì không vượt quá dòng chữ.</summary>
    private const int BorderOvershoot = 2;

    /// <summary>Trong khoảng trống giữa 2 từ có 1 cột mà ≥ 80% số dòng (chiều cao chữ + <see cref="BorderOvershoot"/> mỗi
    /// phía) đổi độ sáng đột ngột so với cột bên trái → viền ô hoặc mép ô (nền trắng / xám cạnh nền trang). Chỉ xét
    /// pixel nằm hẳn trong khoảng trống; khoảng trống &lt; 2 px thì không xét.</summary>
    internal static unsafe bool HasBorderBetween(SKBitmap bitmap, SKRectI left, SKRectI right)
    {
        int x0 = Math.Max(1, left.Right + 1), x1 = Math.Min(bitmap.Width, right.Left);
        int top = Math.Max(0, Math.Min(left.Top, right.Top) - BorderOvershoot);
        int bottom = Math.Min(bitmap.Height, Math.Max(left.Bottom, right.Bottom) + BorderOvershoot);
        int rows = bottom - top;
        if (x1 <= x0 || rows < 4)
        {
            return false;
        }
        byte* basePtr = (byte*)bitmap.GetPixels();
        for (int x = x0; x < x1; x++)
        {
            int steps = 0;
            for (int y = top; y < bottom; y++)
            {
                uint* row = (uint*)(basePtr + (long)y * bitmap.RowBytes);
                if (Math.Abs(ImageUtil.Luma(row[x]) - ImageUtil.Luma(row[x - 1])) >= BorderStep)
                {
                    steps++;
                }
            }
            if (steps * 5 >= rows * 4)
            {
                return true;
            }
        }
        return false;
    }

    // ---- Màu chữ ----

    /// <summary>Màu nét chữ trong khung: nền = màu hay gặp nhất; nét = pixel lệch nền rõ; lấy 30% pixel lệch nền
    /// nhiều nhất (lõi nét - bỏ viền khử răng cưa, vốn làm chữ đen trông như xám). Null nếu quá ít nét.</summary>
    public static unsafe SKColor? InkColor(SKBitmap bitmap, SKRectI area)
    {
        area = SKRectI.Intersect(area, SKRectI.Create(bitmap.Width, bitmap.Height));
        if (area.Width < 2 || area.Height < 2)
        {
            return null;
        }
        var counts = new Dictionary<uint, int>();
        var pixels = new List<uint>(area.Width * area.Height);
        byte* basePtr = (byte*)bitmap.GetPixels();
        for (int y = area.Top; y < area.Bottom; y++)
        {
            uint* row = (uint*)(basePtr + (long)y * bitmap.RowBytes);
            for (int x = area.Left; x < area.Right; x++)
            {
                uint p = row[x] | 0xFF000000u;
                pixels.Add(p);
                uint q = p & 0xFFF0F0F0u; // gộp màu gần nhau khi tìm nền
                counts[q] = counts.GetValueOrDefault(q) + 1;
            }
        }
        uint bgKey = counts.MaxBy(kv => kv.Value).Key;
        float bgLuma = ImageUtil.Luma(bgKey | 0x00080808u);
        var ink = pixels.Select(p => (P: p, D: Math.Abs(ImageUtil.Luma(p) - bgLuma))).Where(t => t.D > 40).ToList();
        if (ink.Count < 6)
        {
            return null;
        }
        var core = ink.OrderByDescending(t => t.D).Take(Math.Max(3, ink.Count * 3 / 10)).ToList();
        double r = core.Average(t => (t.P >> 16) & 0xFF), g = core.Average(t => (t.P >> 8) & 0xFF), b = core.Average(t => t.P & 0xFF);
        return new SKColor((byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
    }

    public static bool ColorsDiffer(SKColor a, SKColor b)
    {
        double la = 0.299 * a.Red + 0.587 * a.Green + 0.114 * a.Blue;
        double lb = 0.299 * b.Red + 0.587 * b.Green + 0.114 * b.Blue;
        double dr = a.Red - b.Red, dg = a.Green - b.Green, db = a.Blue - b.Blue;
        return Math.Abs(la - lb) >= ColorLumaDelta || Math.Sqrt(dr * dr + dg * dg + db * db) >= ColorRgbDelta;
    }

    private static string Hex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
}
