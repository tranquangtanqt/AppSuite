namespace FileTools.Core;

public enum FileSortOrder
{
    /// <summary>Theo tên, hiểu số: file2 trước file10.</summary>
    NameNatural,
    Modified,
    Size,
}

/// <summary>Liệt kê file trong thư mục theo mẫu lọc ("*.csv;*.txt"), có / không thư mục con.</summary>
public static class FileListing
{
    public static List<FileInfo> List(string folder, string patterns, bool recursive, FileSortOrder order, bool descending = false)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var masks = patterns.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (masks.Length == 0)
        {
            masks = ["*"];
        }
        var files = masks
            .SelectMany(m => new DirectoryInfo(folder).EnumerateFiles(m, option))
            .Where(f => !f.Name.StartsWith("~$", StringComparison.Ordinal) && !f.Name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(f => f.FullName, StringComparer.OrdinalIgnoreCase);
        return Sort(files, order, descending).ToList();
    }

    /// <summary>Tăng dần: tên A → Z (2 trước 10), cũ → mới, nhỏ → lớn. Giảm dần = đảo ngược đúng thứ tự đó.</summary>
    public static IEnumerable<FileInfo> Sort(IEnumerable<FileInfo> files, FileSortOrder order, bool descending = false)
    {
        var sorted = order switch
        {
            FileSortOrder.Modified => files.OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.FullName, NaturalComparer.Instance),
            FileSortOrder.Size => files.OrderBy(f => f.Length).ThenBy(f => f.FullName, NaturalComparer.Instance),
            _ => files.OrderBy(f => f.FullName, NaturalComparer.Instance),
        };
        return descending ? sorted.Reverse() : sorted;
    }

    /// <summary>Chữ trên nút đổi chiều, vd "Cũ → mới".</summary>
    public static string DirectionText(FileSortOrder order, bool descending) => (order, descending) switch
    {
        (FileSortOrder.Modified, false) => "Cũ → mới",
        (FileSortOrder.Modified, true) => "Mới → cũ",
        (FileSortOrder.Size, false) => "Nhỏ → lớn",
        (FileSortOrder.Size, true) => "Lớn → nhỏ",
        (_, false) => "A → Z (1 → 10)",
        (_, true) => "Z → A (10 → 1)",
    };
}

/// <summary>So sánh chuỗi "tự nhiên": đoạn số so theo giá trị (a2 &lt; a10), đoạn chữ không phân biệt hoa thường.</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x is null)
        {
            return -1;
        }
        if (y is null)
        {
            return 1;
        }
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var a = x.AsSpan(si, i - si).TrimStart('0');
                var b = y.AsSpan(sj, j - sj).TrimStart('0');
                if (a.Length != b.Length)
                {
                    return a.Length.CompareTo(b.Length);
                }
                int cmp = a.SequenceCompareTo(b);
                if (cmp != 0)
                {
                    return cmp;
                }
                continue;
            }
            int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
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
