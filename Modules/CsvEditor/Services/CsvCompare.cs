using System.Globalization;
using CsvEditor.Models;

namespace CsvEditor.Services;

public enum CompareStatus { Same, Modified, OnlyB, OnlyA }

/// <param name="KeyColumns">Cột khoá (1 hoặc nhiều, ghép theo tên). Rỗng = không có khoá: tự căn theo thứ tự dòng.</param>
/// <param name="IgnoredColumns">Cột không so (vd ngày cập nhật).</param>
public sealed record CompareOptions(
    IReadOnlyList<string> KeyColumns,
    IReadOnlyCollection<string> IgnoredColumns,
    bool TrimSpaces = true,
    bool IgnoreCase = false,
    bool NumbersByValue = true)
{
    public bool ByOrder => KeyColumns.Count == 0;
}

/// <summary>1 dòng của bảng kết quả: 1 ô khác (Sửa) hoặc cả bản ghi (Giống / Chỉ có ở A / Chỉ có ở B).</summary>
/// <param name="Key">Giá trị khoá (nhiều cột nối bằng " · "), hoặc "Dòng 12 → 13" khi căn theo thứ tự dòng.</param>
/// <param name="IsGroupStart">Dòng đầu của 1 bản ghi (bản ghi sửa nhiều ô có nhiều dòng liền nhau, cùng Key).</param>
/// <param name="RowA">Vị trí bản ghi trong bảng A (0-based), -1 = bản ghi chỉ có ở B.</param>
/// <param name="ColumnA">Cột trong bảng A của ô khác, -1 = cả bản ghi.</param>
public sealed record CompareEntry(
    CompareStatus Status,
    string Key,
    string Column,
    string ValueA,
    string ValueB,
    bool IsGroupStart,
    int RowA,
    int ColumnA);

/// <param name="ChangedCells">Số ô khác trong các bản ghi Sửa.</param>
/// <param name="DuplicateKeysA">Số giá trị khoá xuất hiện nhiều lần ở A (ghép theo thứ tự xuất hiện).</param>
/// <param name="TooDifferent">Căn theo thứ tự dòng mà 2 bảng khác nhau quá nhiều: chỉ ghép dòng giống hệt (không xét thứ tự),
/// còn lại là Chỉ có ở A / B, không đoán Sửa.</param>
public sealed record CompareResult(
    IReadOnlyList<CompareEntry> Entries,
    int Same, int Modified, int OnlyB, int OnlyA,
    int ChangedCells,
    IReadOnlyList<string> ComparedColumns,
    IReadOnlyList<string> ColumnsOnlyA,
    IReadOnlyList<string> ColumnsOnlyB,
    int DuplicateKeysA, int DuplicateKeysB,
    bool TooDifferent)
{
    public int Different => Modified + OnlyA + OnlyB;
}

/// <summary>Kết quả kiểm tra 1 bộ cột khoá trên 1 bảng.</summary>
/// <param name="DuplicateValues">Số giá trị khoá có từ 2 dòng trở lên.</param>
/// <param name="Example">1 giá trị bị trùng (để ghi ví dụ), null nếu không trùng.</param>
public sealed record KeyCheck(int Rows, int DuplicateValues, int EmptyKeys, string? Example, int ExampleCount)
{
    public bool IsUnique => DuplicateValues == 0 && EmptyKeys == 0;
}

/// <summary>
/// So 2 bảng CSV theo bản ghi và từng ô. Cột ghép theo tên (không phân biệt hoa thường) nên thứ tự cột 2 file khác nhau
/// vẫn so được; cột chỉ có ở 1 bên được báo riêng, không tính là sửa.
/// - Có cột khoá: ghép bản ghi cùng khoá (khoá trùng → ghép theo thứ tự xuất hiện).
/// - Không có cột khoá: căn dòng giống hệt bằng Myers (như FileTools › So sánh 2 file - chép, các module không reference
///   nhau), trong mỗi đoạn khác ghép dòng giống nhau ≥ 1 nửa số cột thành Sửa.
/// </summary>
public static class CsvCompare
{
    public const string WholeRecord = "(cả bản ghi)";

    /// <summary>Căn theo thứ tự dòng: quá số bước sửa này thì không dò thứ tự (vết Myers tăng theo D²).</summary>
    public const int MaxEdits = 4000;

    private const int SuggestSampleRows = 20_000;
    private const int SuggestMaxColumns = 10;
    private static readonly string[] KeyHints = { "id", "mã", "ma ", "code", "key", "コード", "番号", "ｺｰﾄﾞ", "ＩＤ" };

    // ----- Cột khoá -----

    /// <summary>Gợi ý cột khoá: 1 cột không trống, không trùng (ưu tiên tên giống mã / id); không có thì tổ hợp 2 rồi 3
    /// cột trong 10 cột đầu. Xét tối đa 20.000 dòng đầu (chỉ là gợi ý - hộp thoại kiểm tra lại trên cả bảng). Rỗng = không
    /// tìm được (nên căn theo thứ tự dòng).</summary>
    public static IReadOnlyList<string> SuggestKey(IReadOnlyList<CsvColumn> columns, IReadOnlyList<CsvRow> rows)
    {
        var sample = rows.Count > SuggestSampleRows ? rows.Take(SuggestSampleRows).ToList() : rows;
        if (columns.Count == 0 || sample.Count == 0)
        {
            return Array.Empty<string>();
        }

        var options = new CompareOptions(Array.Empty<string>(), Array.Empty<string>());
        var singles = Enumerable.Range(0, columns.Count).Where(c => IsUnique(sample, new[] { c }, options)).ToList();
        if (singles.Count > 0)
        {
            var hinted = singles.FirstOrDefault(c => LooksLikeKey(columns[c].Name), -1);
            return new[] { columns[hinted >= 0 ? hinted : singles[0]].Name };
        }

        var n = Math.Min(columns.Count, SuggestMaxColumns);
        for (var size = 2; size <= 3; size++)
        {
            foreach (var combo in Combinations(n, size))
            {
                if (IsUnique(sample, combo, options))
                {
                    return combo.Select(c => columns[c].Name).ToArray();
                }
            }
        }
        return Array.Empty<string>();
    }

    private static bool LooksLikeKey(string name)
    {
        var lower = name.Trim().ToLowerInvariant() + " ";
        return KeyHints.Any(h => lower.Contains(h, StringComparison.Ordinal));
    }

    private static IEnumerable<int[]> Combinations(int n, int size)
    {
        if (size > n)
        {
            yield break;
        }
        var idx = Enumerable.Range(0, size).ToArray();
        while (true)
        {
            yield return (int[])idx.Clone();
            var i = size - 1;
            while (i >= 0 && idx[i] == n - size + i)
            {
                i--;
            }
            if (i < 0)
            {
                yield break;
            }
            idx[i]++;
            for (var j = i + 1; j < size; j++)
            {
                idx[j] = idx[j - 1] + 1;
            }
        }
    }

    private static bool IsUnique(IReadOnlyList<CsvRow> rows, int[] keyIdx, CompareOptions options)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (keyIdx.All(i => row.GetCell(i).Trim().Length == 0) || !seen.Add(KeyOf(row, keyIdx, options)))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Đếm giá trị khoá trùng / trống của <paramref name="keyColumns"/> trên cả bảng.</summary>
    public static KeyCheck CheckKey(IReadOnlyList<CsvColumn> columns, IReadOnlyList<CsvRow> rows, IReadOnlyList<string> keyColumns, CompareOptions options)
    {
        var idx = keyColumns.Select(k => IndexOf(columns, k)).Where(i => i >= 0).ToArray();
        if (idx.Length == 0)
        {
            return new KeyCheck(rows.Count, 0, 0, null, 0);
        }

        var counts = new Dictionary<string, (int Count, string Display)>(StringComparer.Ordinal);
        var empty = 0;
        foreach (var row in rows)
        {
            if (idx.All(i => row.GetCell(i).Trim().Length == 0))
            {
                empty++;
                continue;
            }
            var k = KeyOf(row, idx, options);
            counts[k] = counts.TryGetValue(k, out var n) ? (n.Count + 1, n.Display) : (1, DisplayKey(row, idx));
        }

        var duplicates = counts.Values.Where(v => v.Count > 1).ToList();
        var example = duplicates.OrderByDescending(v => v.Count).FirstOrDefault();
        return new KeyCheck(rows.Count, duplicates.Count, empty, example.Display, example.Count);
    }

    // ----- So sánh -----

    public static CompareResult Compare(
        IReadOnlyList<CsvColumn> colsA, IReadOnlyList<CsvRow> rowsA,
        IReadOnlyList<CsvColumn> colsB, IReadOnlyList<CsvRow> rowsB,
        CompareOptions options, CancellationToken cancellationToken = default)
    {
        var keyA = options.KeyColumns.Select(k => IndexOf(colsA, k)).ToArray();
        var keyB = options.KeyColumns.Select(k => IndexOf(colsB, k)).ToArray();
        var missing = options.KeyColumns.Where((_, i) => keyA[i] < 0 || keyB[i] < 0).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Cột khoá {string.Join(", ", missing.Select(m => $"\"{m}\""))} không có ở cả 2 file.");
        }

        var ignored = new HashSet<string>(options.IgnoredColumns, StringComparer.OrdinalIgnoreCase);
        var keyNames = new HashSet<string>(options.KeyColumns, StringComparer.OrdinalIgnoreCase);
        var pairs = new List<(string Name, int A, int B)>();
        for (var i = 0; i < colsA.Count; i++)
        {
            var name = colsA[i].Name;
            var j = IndexOf(colsB, name);
            if (j >= 0 && !keyNames.Contains(name) && !ignored.Contains(name) && pairs.All(p => p.B != j))
            {
                pairs.Add((name, i, j));
            }
        }
        var onlyA = colsA.Where(c => IndexOf(colsB, c.Name) < 0).Select(c => c.Name).ToList();
        var onlyB = colsB.Where(c => IndexOf(colsA, c.Name) < 0).Select(c => c.Name).ToList();
        var preview = new Preview(colsA, colsB, keyA, keyB);

        var builder = new Builder(rowsA, rowsB, pairs, options, preview);
        int dupA = 0, dupB = 0;
        var tooDifferent = false;
        if (options.ByOrder)
        {
            tooDifferent = !CompareByOrder(builder, cancellationToken);
        }
        else
        {
            (dupA, dupB) = CompareByKey(builder, keyA, keyB, cancellationToken);
        }

        return new CompareResult(builder.Entries, builder.Same, builder.Modified, builder.OnlyB, builder.OnlyA, builder.ChangedCells,
            pairs.Select(p => p.Name).ToList(), onlyA, onlyB, dupA, dupB, tooDifferent);
    }

    private static (int DupA, int DupB) CompareByKey(Builder builder, int[] keyA, int[] keyB, CancellationToken ct)
    {
        var options = builder.Options;
        // Khoá trùng: các bản ghi cùng khoá ghép theo thứ tự xuất hiện.
        var mapB = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (var j = 0; j < builder.RowsB.Count; j++)
        {
            var k = KeyOf(builder.RowsB[j], keyB, options);
            if (!mapB.TryGetValue(k, out var q))
            {
                mapB[k] = q = new Queue<int>();
            }
            q.Enqueue(j);
        }
        var dupB = mapB.Values.Count(q => q.Count > 1);
        var countA = new Dictionary<string, int>(StringComparer.Ordinal);

        var onlyAEntries = new List<int>();
        for (var i = 0; i < builder.RowsA.Count; i++)
        {
            if ((i & 0x3FFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            var rowA = builder.RowsA[i];
            var k = KeyOf(rowA, keyA, options);
            countA[k] = countA.TryGetValue(k, out var n) ? n + 1 : 1;
            if (mapB.TryGetValue(k, out var q) && q.Count > 0)
            {
                builder.Pair(i, q.Dequeue(), DisplayKey(rowA, keyA));
            }
            else
            {
                onlyAEntries.Add(i);
            }
        }

        // Thứ tự trong bảng kết quả: bản ghi có ở cả 2 (theo A) → chỉ có ở B (theo B) → chỉ có ở A (theo A).
        foreach (var q in mapB.Values)
        {
            builder.PendingB.AddRange(q);
        }
        builder.PendingB.Sort();
        foreach (var j in builder.PendingB)
        {
            builder.AddOnlyB(j, DisplayKey(builder.RowsB[j], keyB));
        }
        foreach (var i in onlyAEntries)
        {
            builder.AddOnlyA(i, DisplayKey(builder.RowsA[i], keyA));
        }
        return (countA.Values.Count(v => v > 1), dupB);
    }

    /// <summary>Căn theo thứ tự dòng. False = khác nhau quá nhiều (quá <see cref="MaxEdits"/> bước): chỉ ghép dòng giống hệt.</summary>
    private static bool CompareByOrder(Builder builder, CancellationToken ct)
    {
        // Mỗi dòng → 1 số (dòng giống nhau theo cách so = cùng số) để Myers so nhanh.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] Signatures(IReadOnlyList<CsvRow> rows, bool sideA)
        {
            var result = new int[rows.Count];
            for (var r = 0; r < rows.Count; r++)
            {
                if ((r & 0x3FFF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                }
                var sig = string.Join('\u001F', builder.Pairs.Select(p => Canonical(rows[r].GetCell(sideA ? p.A : p.B), builder.Options)));
                if (!ids.TryGetValue(sig, out var id))
                {
                    ids[sig] = id = ids.Count;
                }
                result[r] = id;
            }
            return result;
        }
        var a = Signatures(builder.RowsA, true);
        var b = Signatures(builder.RowsB, false);

        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            prefix++;
        }
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
        {
            suffix++;
        }
        var midA = a[prefix..(a.Length - suffix)];
        var midB = b[prefix..(b.Length - suffix)];
        var ops = Myers(midA, midB, MaxEdits, ct);

        for (var x = 0; x < prefix; x++)
        {
            builder.Pair(x, x, LineKey(x, x));
        }

        if (ops is null)
        {
            // Quá khác: ghép dòng giống hệt như tập hợp (không xét thứ tự), còn lại là Chỉ có ở A / B.
            var freeB = new Dictionary<int, Queue<int>>();
            for (var j = 0; j < midB.Length; j++)
            {
                if (!freeB.TryGetValue(midB[j], out var q))
                {
                    freeB[midB[j]] = q = new Queue<int>();
                }
                q.Enqueue(prefix + j);
            }
            var onlyA = new List<int>();
            for (var i = 0; i < midA.Length; i++)
            {
                if (freeB.TryGetValue(midA[i], out var q) && q.Count > 0)
                {
                    var j = q.Dequeue();
                    builder.Pair(prefix + i, j, LineKey(prefix + i, j));
                }
                else
                {
                    onlyA.Add(prefix + i);
                }
            }
            foreach (var j in freeB.Values.SelectMany(q => q).Order())
            {
                builder.AddOnlyB(j, $"Dòng {j + 1:N0} (B)");
            }
            foreach (var i in onlyA)
            {
                builder.AddOnlyA(i, $"Dòng {i + 1:N0} (A)");
            }
        }
        else
        {
            int ai = prefix, bi = prefix, k = 0;
            while (k < ops.Count)
            {
                if (ops[k] == Op.Equal)
                {
                    builder.Pair(ai, bi, LineKey(ai, bi));
                    ai++;
                    bi++;
                    k++;
                    continue;
                }
                var dels = new List<int>();
                var ins = new List<int>();
                while (k < ops.Count && ops[k] != Op.Equal)
                {
                    if (ops[k] == Op.Delete)
                    {
                        dels.Add(ai++);
                    }
                    else
                    {
                        ins.Add(bi++);
                    }
                    k++;
                }
                PairBlock(builder, dels, ins, ct);
            }
        }

        for (var t = 0; t < suffix; t++)
        {
            int x = a.Length - suffix + t, y = b.Length - suffix + t;
            builder.Pair(x, y, LineKey(x, y));
        }
        return ops is not null;
    }

    /// <summary>1 đoạn khác (dòng bỏ ở A, dòng thêm ở B): ghép dòng giống nhau ≥ 1 nửa số cột thành Sửa (chọn dòng B giống
    /// nhất, theo thứ tự). Đoạn quá lớn chỉ thử dòng cùng vị trí trong đoạn.</summary>
    private static void PairBlock(Builder builder, List<int> dels, List<int> ins, CancellationToken ct)
    {
        var matchOf = new int[dels.Count];
        Array.Fill(matchOf, -1);
        var used = new bool[ins.Count];
        var full = (long)dels.Count * ins.Count <= 250_000;
        var next = 0; // giữ thứ tự: dòng A sau chỉ ghép với dòng B sau dòng B đã ghép trước đó
        for (var d = 0; d < dels.Count; d++)
        {
            ct.ThrowIfCancellationRequested();
            int best = -1, bestScore = -1;
            var from = full ? next : d;
            var to = full ? ins.Count : Math.Min(d + 1, ins.Count);
            for (var j = from; j < to; j++)
            {
                if (used[j])
                {
                    continue;
                }
                var score = builder.EqualCells(dels[d], ins[j]);
                if (score > bestScore)
                {
                    best = j;
                    bestScore = score;
                }
            }
            if (best >= 0 && builder.Pairs.Count > 0 && bestScore * 2 >= builder.Pairs.Count)
            {
                matchOf[d] = best;
                used[best] = true;
                next = best + 1;
            }
        }

        // Trong đoạn: lần lượt theo A (Sửa / Chỉ có ở A), dòng B chưa ghép xen vào đúng chỗ theo thứ tự B.
        var bPos = 0;
        for (var d = 0; d < dels.Count; d++)
        {
            if (matchOf[d] >= 0)
            {
                for (; bPos < matchOf[d]; bPos++)
                {
                    if (!used[bPos])
                    {
                        builder.AddOnlyB(ins[bPos], $"Dòng {ins[bPos] + 1:N0} (B)");
                    }
                }
                builder.Pair(dels[d], ins[matchOf[d]], LineKey(dels[d], ins[matchOf[d]]));
                bPos = matchOf[d] + 1;
            }
            else
            {
                builder.AddOnlyA(dels[d], $"Dòng {dels[d] + 1:N0} (A)");
            }
        }
        for (; bPos < ins.Count; bPos++)
        {
            if (!used[bPos])
            {
                builder.AddOnlyB(ins[bPos], $"Dòng {ins[bPos] + 1:N0} (B)");
            }
        }
    }

    private static string LineKey(int a, int b) => a == b ? $"Dòng {a + 1:N0}" : $"Dòng {a + 1:N0} → {b + 1:N0}";

    // ----- So 1 ô -----

    public static bool Equal(string a, string b, CompareOptions options) =>
        string.Equals(Canonical(a, options), Canonical(b, options), StringComparison.Ordinal);

    /// <summary>Dạng chuẩn của 1 ô theo cách so: bỏ khoảng trắng đầu / cuối, hoa thường, số theo giá trị (1.0 = 1).</summary>
    private static string Canonical(string value, CompareOptions options)
    {
        var v = options.TrimSpaces ? value.Trim() : value;
        if (options.NumbersByValue && v.Length > 0 && CsvNumber.TryParse(v, out var number))
        {
            return "\u0001" + number.ToString("R", CultureInfo.InvariantCulture);
        }
        return options.IgnoreCase ? v.ToUpperInvariant() : v;
    }

    private static string KeyOf(CsvRow row, int[] idx, CompareOptions options) =>
        string.Join('\u001F', idx.Select(i => Canonical(row.GetCell(i), options)));

    private static string DisplayKey(CsvRow row, int[] idx) => string.Join(" · ", idx.Select(i => row.GetCell(i).Trim()));

    private static int IndexOf(IReadOnlyList<CsvColumn> columns, string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i].Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    // ----- Dựng danh sách kết quả -----

    private sealed class Builder
    {
        public Builder(IReadOnlyList<CsvRow> rowsA, IReadOnlyList<CsvRow> rowsB, List<(string Name, int A, int B)> pairs, CompareOptions options, Preview preview)
        {
            RowsA = rowsA;
            RowsB = rowsB;
            Pairs = pairs;
            Options = options;
            _preview = preview;
        }

        private readonly Preview _preview;
        public IReadOnlyList<CsvRow> RowsA { get; }
        public IReadOnlyList<CsvRow> RowsB { get; }
        public List<(string Name, int A, int B)> Pairs { get; }
        public CompareOptions Options { get; }
        public List<CompareEntry> Entries { get; } = new();
        public List<int> PendingB { get; } = new();
        public int Same, Modified, OnlyA, OnlyB, ChangedCells;

        public int EqualCells(int i, int j)
        {
            var n = 0;
            foreach (var (_, a, b) in Pairs)
            {
                if (Equal(RowsA[i].GetCell(a), RowsB[j].GetCell(b), Options))
                {
                    n++;
                }
            }
            return n;
        }

        public void Pair(int i, int j, string key)
        {
            var rowA = RowsA[i];
            var rowB = RowsB[j];
            var first = true;
            foreach (var (name, a, b) in Pairs)
            {
                var va = rowA.GetCell(a);
                var vb = rowB.GetCell(b);
                if (Equal(va, vb, Options))
                {
                    continue;
                }
                Entries.Add(new CompareEntry(CompareStatus.Modified, key, name, va, vb, first, i, a));
                first = false;
                ChangedCells++;
            }
            if (first)
            {
                Same++;
                Entries.Add(new CompareEntry(CompareStatus.Same, key, WholeRecord, _preview.OfA(rowA), _preview.OfB(rowB), true, i, -1));
            }
            else
            {
                Modified++;
            }
        }

        public void AddOnlyA(int i, string key)
        {
            OnlyA++;
            Entries.Add(new CompareEntry(CompareStatus.OnlyA, key, WholeRecord, _preview.OfA(RowsA[i]), "", true, i, -1));
        }

        public void AddOnlyB(int j, string key)
        {
            OnlyB++;
            Entries.Add(new CompareEntry(CompareStatus.OnlyB, key, WholeRecord, "", _preview.OfB(RowsB[j]), true, -1, -1));
        }
    }

    /// <summary>Tóm tắt cả bản ghi: 3 giá trị không trống đầu tiên (bỏ cột khoá), cả 2 bên theo thứ tự cột của A (cột chỉ có
    /// ở B xếp sau) - bản ghi giống nhau thì 2 bên đọc giống nhau dù file B đổi thứ tự cột.</summary>
    private sealed class Preview
    {
        private readonly int[] _a;
        private readonly int[] _b;

        public Preview(IReadOnlyList<CsvColumn> colsA, IReadOnlyList<CsvColumn> colsB, int[] keyA, int[] keyB)
        {
            _a = Enumerable.Range(0, colsA.Count).Where(i => !keyA.Contains(i)).ToArray();
            var b = _a.Select(i => IndexOf(colsB, colsA[i].Name)).Where(j => j >= 0 && !keyB.Contains(j)).ToList();
            b.AddRange(Enumerable.Range(0, colsB.Count).Where(j => !keyB.Contains(j) && !b.Contains(j)));
            _b = b.ToArray();
        }

        public string OfA(CsvRow row) => Build(row, _a);

        public string OfB(CsvRow row) => Build(row, _b);

        private static string Build(CsvRow row, int[] order)
        {
            var parts = new List<string>(3);
            var more = false;
            foreach (var i in order)
            {
                var v = row.GetCell(i).Trim();
                if (v.Length == 0)
                {
                    continue;
                }
                if (parts.Count == 3)
                {
                    more = true;
                    break;
                }
                parts.Add(v.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' '));
            }
            return string.Join(" · ", parts) + (more ? " · …" : "");
        }
    }

    // ----- Myers (chép từ FileTools.Core.TextDiff) -----

    private enum Op : byte { Equal, Delete, Insert }

    /// <summary>Diff Myers O((N + M)·D), lưu vết V từng bước d. Null nếu D &gt; maxEdits.</summary>
    private static List<Op>? Myers(int[] a, int[] b, int maxEdits, CancellationToken ct)
    {
        int n = a.Length, m = b.Length;
        var max = Math.Min(n + m, maxEdits);
        var offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        var finalD = -1;
        for (var d = 0; d <= max; d++)
        {
            if ((d & 63) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            var snapshot = new int[2 * d + 3];
            Array.Copy(v, offset - d - 1, snapshot, 0, snapshot.Length);
            trace.Add(snapshot);
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                var y = x - k;
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
        for (var d = finalD; d >= 0; d--)
        {
            var snap = trace[d];
            int V(int k) => snap[k + d + 1];
            var k = cx - cy;
            var prevK = k == -d || (k != d && V(k - 1) < V(k + 1)) ? k + 1 : k - 1;
            var prevX = d == 0 ? 0 : V(prevK);
            var prevY = prevX - prevK;
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
}
