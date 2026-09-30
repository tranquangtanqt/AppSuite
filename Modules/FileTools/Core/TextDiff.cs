using System.IO.Hashing;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace FileTools.Core;

public sealed record CompareOptions
{
    public required string PathA { get; init; }
    public required string PathB { get; init; }
    public bool IgnoreCase { get; init; }
    /// <summary>Bỏ khoảng trắng đầu / cuối và gộp khoảng trắng liên tiếp khi so.</summary>
    public bool IgnoreWhitespace { get; init; }
    /// <summary>Số dòng giống nhau hiện quanh mỗi chỗ khác.</summary>
    public int ContextLines { get; init; } = 3;
    /// <summary>Quá số bước sửa này (dòng thêm + bớt) thì không dò thứ tự nữa - chỉ liệt kê dòng chỉ có ở 1 bên.</summary>
    public int MaxEdits { get; init; } = 4000;
    /// <summary>Số dòng tối đa đưa vào kết quả hiển thị / báo cáo (vẫn đếm hết).</summary>
    public int MaxDisplayLines { get; init; } = 20_000;
    /// <summary>Ghi báo cáo HTML ra đây (null = không).</summary>
    public string? ReportPath { get; init; }
}

public enum DiffLineKind
{
    Context,
    /// <summary>Chỉ có ở A (B bỏ đi / sửa).</summary>
    Removed,
    /// <summary>Chỉ có ở B (B thêm vào / sửa).</summary>
    Added,
}

public sealed record DiffLine(DiffLineKind Kind, long? LineA, long? LineB, string Text);

public sealed record DiffHunk(long StartA, long StartB, IReadOnlyList<DiffLine> Lines)
{
    public string Header => $"@@ A dòng {StartA:N0} · B dòng {StartB:N0} @@";
}

/// <param name="TooDifferent">2 file khác nhau quá nhiều để dò thứ tự: <see cref="Hunks"/> khi đó là 2 khối "chỉ có ở A" /
/// "chỉ có ở B" (so như tập hợp, không xét thứ tự).</param>
public sealed record CompareResult(
    long LinesA, long LinesB, long Removed, long Added, int ChangeBlocks,
    IReadOnlyList<DiffHunk> Hunks, bool Truncated, bool TooDifferent)
{
    public bool Identical => Removed == 0 && Added == 0;
}

/// <summary>
/// So sánh 2 file text theo dòng. Mỗi dòng chỉ giữ mã băm 64-bit (8 byte), cắt phần đầu / cuối giống nhau rồi diff Myers
/// phần giữa (chép từ ImageCompare.Engine.RowAligner - module không reference nhau). Vết Myers tăng theo D² nên giới hạn
/// <see cref="CompareOptions.MaxEdits"/>; quá thì chuyển sang so như tập hợp dòng. Chỉ đọc lại nội dung những dòng cần hiện.
/// </summary>
public static class TextDiff
{
    private enum Op : byte { Equal, Delete, Insert }

    public static CompareResult Compare(CompareOptions o, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var throttle = new ProgressThrottle(progress);
        var a = ReadHashes(o.PathA, o, p => throttle.Report(p * 0.4, "Đọc file A"), ct);
        var b = ReadHashes(o.PathB, o, p => throttle.Report(0.4 + p * 0.4, "Đọc file B"), ct);

        int prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix])
        {
            prefix++;
        }
        int suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix])
        {
            suffix++;
        }
        var midA = a.GetRange(prefix, a.Count - prefix - suffix).ToArray();
        var midB = b.GetRange(prefix, b.Count - prefix - suffix).ToArray();
        throttle.Report(0.82, "Dò chỗ khác");
        var ops = Myers(midA, midB, o.MaxEdits, ct);

        CompareResult result = ops is null
            ? SetDifference(o, a, b, midA, midB, prefix, ct)
            : BuildHunks(o, a.Count, b.Count, prefix, ops, ct);
        if (o.ReportPath is { } report)
        {
            WriteReport(report, o, result);
        }
        throttle.Report(1, null, force: true);
        return result;
    }

    private static List<ulong> ReadHashes(string path, CompareOptions o, Action<double> progress, CancellationToken ct)
    {
        var list = new List<ulong>();
        using var reader = LineReader.Open(path);
        while (reader.TryRead(out var line))
        {
            if ((reader.LinesRead & 0x3FF) == 0)
            {
                ct.ThrowIfCancellationRequested();
                progress(reader.Fraction);
            }
            list.Add(Hash(Normalize(line.Text, o)));
        }
        return list;
    }

    private static string Normalize(string text, CompareOptions o)
    {
        if (o.IgnoreWhitespace)
        {
            text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        return o.IgnoreCase ? text.ToUpperInvariant() : text;
    }

    private static ulong Hash(string s) => XxHash3.HashToUInt64(MemoryMarshal.AsBytes(s.AsSpan()));

    /// <summary>Diff Myers O((N + M)·D), lưu vết V từng bước d (chỉ phần k ∈ [−d−1, d+1]). Null nếu D &gt; maxEdits.</summary>
    private static List<Op>? Myers(ulong[] a, ulong[] b, int maxEdits, CancellationToken ct)
    {
        int n = a.Length, m = b.Length;
        int max = Math.Min(n + m, maxEdits);
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

    /// <summary>1 dòng trong kịch bản đầy đủ (vị trí 0-based ở A / B; -1 = không có).</summary>
    private readonly record struct Step(DiffLineKind Kind, int A, int B);

    private static CompareResult BuildHunks(CompareOptions o, int countA, int countB, int prefix, List<Op> ops, CancellationToken ct)
    {
        // Tạo cụm ngay khi duyệt kịch bản: dòng giống chỉ được giữ trong hàng đợi ≤ ctx dòng gần nhất, nên đoạn giống dài
        // (kể cả phần đầu / cuối đã cắt trước Myers) không bao giờ nằm cả trong bộ nhớ. Sau 1 chỗ khác lấy thêm ctx dòng
        // giống; nếu chỗ khác kế tiếp cách ≤ 2·ctx dòng thì gộp vào cùng cụm. Trong 1 chỗ khác, dòng bỏ (A) đứng trước dòng
        // thêm (B) để đọc như "cũ → mới".
        int ctx = Math.Max(0, o.ContextLines);
        var hunks = new List<List<Step>>();
        List<Step>? current = null;
        var recent = new Queue<Step>();
        int trailing = 0, gap = 0;

        void Equal(int a, int b)
        {
            var step = new Step(DiffLineKind.Context, a, b);
            if (current is not null && trailing < ctx)
            {
                current.Add(step);
                trailing++;
                return;
            }
            recent.Enqueue(step);
            if (recent.Count > ctx)
            {
                recent.Dequeue();
            }
            if (current is not null && ++gap > ctx)
            {
                current = null;
            }
        }

        void Change(List<Step> block)
        {
            if (current is null)
            {
                current = [.. recent];
                hunks.Add(current);
            }
            else
            {
                // Khoảng cách tới cụm trước ≤ 2·ctx: nối nốt các dòng giống ở giữa.
                current.AddRange(recent.Skip(Math.Max(0, recent.Count - gap)));
            }
            current.AddRange(block);
            recent.Clear();
            trailing = 0;
            gap = 0;
        }

        for (int x = prefix - Math.Min(ctx, prefix); x < prefix; x++)
        {
            Equal(x, x);
        }
        long removed = 0, added = 0;
        int blocks = 0;
        int ai = prefix, bi = prefix, i = 0;
        while (i < ops.Count)
        {
            if ((i & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            if (ops[i] == Op.Equal)
            {
                Equal(ai++, bi++);
                i++;
                continue;
            }
            blocks++;
            var dels = new List<Step>();
            var ins = new List<Step>();
            while (i < ops.Count && ops[i] != Op.Equal)
            {
                if (ops[i] == Op.Delete)
                {
                    dels.Add(new Step(DiffLineKind.Removed, ai++, -1));
                }
                else
                {
                    ins.Add(new Step(DiffLineKind.Added, -1, bi++));
                }
                i++;
            }
            removed += dels.Count;
            added += ins.Count;
            Change([.. dels, .. ins]);
        }
        for (int t = 0; t < ctx && ai + t < countA && bi + t < countB; t++)
        {
            Equal(ai + t, bi + t);
        }

        var shown = new List<Step>();
        bool truncated = false;

        int budget = o.MaxDisplayLines;
        var kept = new List<List<Step>>();
        foreach (var h in hunks)
        {
            if (budget <= 0)
            {
                truncated = true;
                break;
            }
            kept.Add(h.Count <= budget ? h : h.GetRange(0, budget));
            truncated |= h.Count > budget;
            budget -= h.Count;
        }
        shown.AddRange(kept.SelectMany(h => h));

        var textA = FetchLines(o.PathA, shown.Where(s => s.A >= 0).Select(s => s.A), ct);
        var textB = FetchLines(o.PathB, shown.Where(s => s.B >= 0).Select(s => s.B), ct);
        var result = kept.Select(h => new DiffHunk(
            (h.FirstOrDefault(s => s.A >= 0).A) + 1,
            (h.FirstOrDefault(s => s.B >= 0).B) + 1,
            h.Select(s => new DiffLine(s.Kind, s.A >= 0 ? s.A + 1 : null, s.B >= 0 ? s.B + 1 : null,
                s.A >= 0 ? textA[s.A] : textB[s.B])).ToList())).ToList();
        return new CompareResult(countA, countB, removed, added, blocks, result, truncated, TooDifferent: false);
    }

    /// <summary>Quá nhiều chỗ khác để dò thứ tự: đếm dòng chỉ có ở 1 bên như tập hợp nhiều phần tử (1 dòng lặp 3 lần ở A, 1
    /// lần ở B → 2 lần "chỉ có ở A").</summary>
    private static CompareResult SetDifference(CompareOptions o, List<ulong> a, List<ulong> b, ulong[] midA, ulong[] midB, int prefix, CancellationToken ct)
    {
        var countB = new Dictionary<ulong, int>();
        foreach (var h in midB)
        {
            countB[h] = countB.GetValueOrDefault(h) + 1;
        }
        var countA = new Dictionary<ulong, int>();
        foreach (var h in midA)
        {
            countA[h] = countA.GetValueOrDefault(h) + 1;
        }
        var onlyA = new List<int>();
        var onlyB = new List<int>();
        var usedB = new Dictionary<ulong, int>(countB);
        for (int i = 0; i < midA.Length; i++)
        {
            if (usedB.TryGetValue(midA[i], out int left) && left > 0)
            {
                usedB[midA[i]] = left - 1;
            }
            else
            {
                onlyA.Add(prefix + i);
            }
        }
        var usedA = new Dictionary<ulong, int>(countA);
        for (int i = 0; i < midB.Length; i++)
        {
            if (usedA.TryGetValue(midB[i], out int left) && left > 0)
            {
                usedA[midB[i]] = left - 1;
            }
            else
            {
                onlyB.Add(prefix + i);
            }
        }
        int half = Math.Max(1, o.MaxDisplayLines / 2);
        bool truncated = onlyA.Count > half || onlyB.Count > half;
        var showA = onlyA.Take(half).ToList();
        var showB = onlyB.Take(half).ToList();
        var textA = FetchLines(o.PathA, showA, ct);
        var textB = FetchLines(o.PathB, showB, ct);
        var hunks = new List<DiffHunk>();
        if (showA.Count > 0)
        {
            hunks.Add(new DiffHunk(showA[0] + 1, 0, showA.Select(x => new DiffLine(DiffLineKind.Removed, x + 1, null, textA[x])).ToList()));
        }
        if (showB.Count > 0)
        {
            hunks.Add(new DiffHunk(0, showB[0] + 1, showB.Select(x => new DiffLine(DiffLineKind.Added, null, x + 1, textB[x])).ToList()));
        }
        return new CompareResult(a.Count, b.Count, onlyA.Count, onlyB.Count, 0, hunks, truncated, TooDifferent: true);
    }

    /// <summary>Đọc nội dung các dòng (0-based) cần hiện trong 1 lượt, dừng sau dòng lớn nhất.</summary>
    private static Dictionary<int, string> FetchLines(string path, IEnumerable<int> wanted, CancellationToken ct)
    {
        var set = new HashSet<int>(wanted);
        var result = new Dictionary<int, string>(set.Count);
        if (set.Count == 0)
        {
            return result;
        }
        int last = set.Max();
        using var reader = LineReader.Open(path);
        int index = -1;
        while (index < last && reader.TryRead(out var line))
        {
            index++;
            if ((index & 0x3FF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            if (set.Contains(index))
            {
                result[index] = line.Text;
            }
        }
        return result;
    }

    private static void WriteReport(string path, CompareOptions o, CompareResult r)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>So sánh: ")
          .Append(E(Path.GetFileName(o.PathA))).Append(" ↔ ").Append(E(Path.GetFileName(o.PathB))).Append("</title><style>")
          .Append("body{font-family:'Segoe UI',system-ui,sans-serif;margin:24px;color:#222;background:#f6f7f9}")
          .Append("table{border-collapse:collapse;width:100%;background:#fff;font-family:Consolas,monospace;font-size:13px}")
          .Append("td{padding:1px 8px;vertical-align:top;white-space:pre-wrap;word-break:break-all}")
          .Append("td.n{color:#888;text-align:right;width:70px;white-space:nowrap}")
          .Append("tr.del{background:#fde8e8}tr.add{background:#e6f6e6}tr.hunk td{background:#e8eef7;color:#445;font-family:'Segoe UI'}")
          .Append(".muted{color:#666}</style></head><body>")
          .Append("<h1>So sánh file</h1><p><b>A:</b> ").Append(E(o.PathA)).Append(" (").Append(r.LinesA.ToString("N0"))
          .Append(" dòng)<br><b>B:</b> ").Append(E(o.PathB)).Append(" (").Append(r.LinesB.ToString("N0")).Append(" dòng)</p><p>");
        sb.Append(r.Identical
            ? "<b>Giống hệt nhau.</b>"
            : $"<b>{r.Removed:N0}</b> dòng chỉ có ở A (bỏ / sửa) · <b>{r.Added:N0}</b> dòng chỉ có ở B (thêm / sửa)"
              + (r.TooDifferent ? " - 2 file khác nhau quá nhiều nên so như tập hợp dòng, không xét thứ tự." : $" · {r.ChangeBlocks:N0} chỗ khác"));
        if (r.Truncated)
        {
            sb.Append(" <span class=\"muted\">(chỉ hiện một phần)</span>");
        }
        sb.Append("</p><table>");
        foreach (var h in r.Hunks)
        {
            sb.Append("<tr class=\"hunk\"><td colspan=\"3\">").Append(E(h.Header)).Append("</td></tr>");
            foreach (var l in h.Lines)
            {
                string cls = l.Kind switch { DiffLineKind.Removed => "del", DiffLineKind.Added => "add", _ => "" };
                string mark = l.Kind switch { DiffLineKind.Removed => "− ", DiffLineKind.Added => "+ ", _ => "  " };
                sb.Append("<tr class=\"").Append(cls).Append("\"><td class=\"n\">").Append(l.LineA?.ToString("N0"))
                  .Append("</td><td class=\"n\">").Append(l.LineB?.ToString("N0")).Append("</td><td>")
                  .Append(mark).Append(E(l.Text)).Append("</td></tr>");
            }
        }
        sb.Append("</table></body></html>");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}
