namespace DbTools.Core;

/// <summary>Kết quả sắp thứ tự + ghi chú cho người dùng (vd vòng phụ thuộc phải cắt).</summary>
public sealed record OrderResult(IReadOnlyList<SqlScriptFile> Files, IReadOnlyList<string> Notes);

/// <summary>
/// Tự sắp thứ tự chạy các file của 1 thư mục: chạy theo vần ABC (như FOR /R của file bat) thì CHECK / COMMENT chạy trước khi
/// có bảng, view chạy trước bảng nó dùng → lỗi. Quy tắc:
/// 1. File A nhắc tới đối tượng do file B tạo (bảng, view, hàm...) → B chạy trước A.
/// 2. File xoá dữ liệu (DELETE / TRUNCATE) bảng X chạy trước file thêm dữ liệu (INSERT) vào X - kiểu "dọn trước, ghi lại sau"
///    của script chạy lại được (vd deleteHistoryTab_*.sql trước *_R.sql cùng ghi fcm_history_table).
/// 3. Còn lại theo loại: schema / kiểu → sequence → bảng → view → hàm → trigger → index / ràng buộc → comment → dữ liệu;
///    cùng loại theo đường dẫn (hiểu số: 2 trước 10).
/// Vòng phụ thuộc (A cần B, B cần A) → cắt theo quy tắc 3 và ghi chú.
/// </summary>
public static class RunOrder
{
    public static int RankOf(IReadOnlyList<StatementInfo> statements)
    {
        foreach (var s in statements)
        {
            var rank = RankOf(s.Kind);
            if (rank >= 0)
            {
                return rank;
            }
        }
        return 9;
    }

    /// <summary>-1 = không quyết định loại file (DROP / SET / GRANT... thường đứng đầu file "xoá rồi tạo").</summary>
    public static int RankOf(StatementKind kind) => kind switch
    {
        StatementKind.CreateSchema => 0,
        StatementKind.CreateType or StatementKind.CreateDomain or StatementKind.CreateSequence => 1,
        StatementKind.CreateTable => 2,
        StatementKind.CreateView => 3,
        StatementKind.CreateFunction or StatementKind.CreateProcedure => 4,
        StatementKind.CreateTrigger => 5,
        StatementKind.CreateIndex or StatementKind.AddConstraint or StatementKind.AlterOther => 6,
        StatementKind.Comment => 7,
        StatementKind.Insert or StatementKind.Update or StatementKind.Delete or StatementKind.Truncate => 8,
        _ => -1,
    };

    public static OrderResult AutoSort(IReadOnlyList<SqlScriptFile> files)
    {
        var notes = new List<string>();
        // Thứ tự gốc: theo đường dẫn (hiểu số) - để kết quả luôn giống nhau.
        var baseOrder = files.OrderBy(f => f.RelativePath, NaturalComparer.Instance).ToList();
        var count = baseOrder.Count;
        var after = new List<HashSet<int>>(); // after[i] = các file phải chạy trước file i
        for (var i = 0; i < count; i++)
        {
            after.Add([]);
        }

        // Ai tạo đối tượng nào.
        var creators = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            foreach (var name in baseOrder[i].CreatedNames)
            {
                if (!creators.TryGetValue(name, out var list))
                {
                    creators[name] = list = [];
                }
                list.Add(i);
            }
        }

        for (var i = 0; i < count; i++)
        {
            var file = baseOrder[i];
            var mentioned = file.Statements.SelectMany(s => s.Names).ToHashSet(StringComparer.Ordinal);
            mentioned.ExceptWith(file.CreatedNames);
            foreach (var name in mentioned)
            {
                if (creators.TryGetValue(name, out var list))
                {
                    foreach (var j in list.Where(j => j != i))
                    {
                        after[i].Add(j);
                    }
                }
            }
        }

        // Dọn trước, ghi sau.
        var cleared = Tables(baseOrder, StatementKind.Delete, StatementKind.Truncate);
        var inserted = Tables(baseOrder, StatementKind.Insert);
        foreach (var (table, cleaners) in cleared)
        {
            if (!inserted.TryGetValue(table, out var writers))
            {
                continue;
            }
            foreach (var w in writers)
            {
                foreach (var c in cleaners.Where(c => c != w))
                {
                    after[w].Add(c);
                }
            }
        }

        // Kahn: trong các file đã đủ điều kiện, chọn loại nhỏ nhất rồi đường dẫn.
        var done = new bool[count];
        var result = new List<SqlScriptFile>(count);
        int Key(int i) => baseOrder[i].Rank * 100000 + i;
        while (result.Count < count)
        {
            var ready = Enumerable.Range(0, count).Where(i => !done[i] && after[i].All(j => done[j])).ToList();
            int pick;
            if (ready.Count > 0)
            {
                pick = ready.MinBy(Key);
            }
            else
            {
                pick = Enumerable.Range(0, count).Where(i => !done[i]).MinBy(Key);
                var waiting = after[pick].Where(j => !done[j]).Select(j => baseOrder[j].RelativePath);
                notes.Add($"Vòng phụ thuộc: {baseOrder[pick].RelativePath} và {string.Join(", ", waiting)} cần nhau - đặt {baseOrder[pick].RelativePath} trước theo loại lệnh. Kiểm tra lại bằng Chạy thử.");
            }
            done[pick] = true;
            result.Add(baseOrder[pick]);
        }
        return new OrderResult(result, notes);
    }

    /// <summary>Dùng thứ tự đã lưu (đường dẫn tương đối). File mới (chưa có trong thứ tự đã lưu) xếp cuối theo thứ tự tự động.</summary>
    public static OrderResult ApplySaved(IReadOnlyList<SqlScriptFile> files, IReadOnlyList<string> saved)
    {
        var byPath = files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        var result = new List<SqlScriptFile>();
        foreach (var path in saved)
        {
            if (byPath.Remove(path, out var file))
            {
                result.Add(file);
            }
        }
        var notes = new List<string>();
        if (byPath.Count > 0)
        {
            var extra = AutoSort(byPath.Values.ToList()).Files;
            result.AddRange(extra);
            notes.Add($"{extra.Count} file mới chưa có trong thứ tự đã lưu - xếp ở cuối: {string.Join(", ", extra.Select(f => f.RelativePath))}.");
        }
        return new OrderResult(result, notes);
    }

    private static Dictionary<string, List<int>> Tables(List<SqlScriptFile> files, params StatementKind[] kinds)
    {
        var map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < files.Count; i++)
        {
            foreach (var s in files[i].Statements.Where(s => kinds.Contains(s.Kind) && s.Table is not null))
            {
                if (!map.TryGetValue(s.Table!.Key, out var list))
                {
                    map[s.Table.Key] = list = [];
                }
                if (!list.Contains(i))
                {
                    list.Add(i);
                }
            }
        }
        return map;
    }
}

/// <summary>So sánh chuỗi không phân biệt hoa / thường, dãy số so theo giá trị (file2 trước file10).</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.Compare(x, y, StringComparison.Ordinal);
        }
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsDigit(x[i]))
                {
                    i++;
                }
                while (j < y.Length && char.IsDigit(y[j]))
                {
                    j++;
                }
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                var cmp = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (cmp != 0)
                {
                    return cmp;
                }
                continue;
            }
            var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (c != 0)
            {
                return c;
            }
            i++;
            j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
