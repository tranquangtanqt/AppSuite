using System.IO.Hashing;

namespace FileTools.Core;

public sealed record DuplicateGroup(long Size, string Hash, IReadOnlyList<string> Files)
{
    /// <summary>Dung lượng lãng phí nếu chỉ giữ 1 bản.</summary>
    public long Wasted => Size * (Files.Count - 1);
}

public sealed record DuplicateResult(IReadOnlyList<DuplicateGroup> Groups, int FilesScanned, long BytesHashed, IReadOnlyList<string> Errors);

/// <summary>
/// Tìm file trùng nội dung trong thư mục: gom theo dung lượng (khác cỡ thì chắc chắn khác) → băm 64 KB đầu để loại nhanh →
/// băm toàn bộ (XxHash128) chỉ các file còn trùng. Hầu hết file chỉ cần đọc 64 KB hoặc không cần đọc. File đọc lỗi (đang bị
/// khoá, không có quyền) được ghi nhận, không dừng cả lượt.
/// </summary>
public static class DuplicateFinder
{
    private const int HeadBytes = 64 * 1024;

    public static DuplicateResult Find(IReadOnlyList<string> folders, string patterns, bool recursive, long minSize = 1,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var throttle = new ProgressThrottle(progress);
        var errors = new List<string>();
        var files = new List<FileInfo>();
        foreach (var folder in folders)
        {
            try
            {
                files.AddRange(FileListing.List(folder, patterns, recursive, FileSortOrder.NameNatural));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{folder}: {ex.Message}");
            }
        }
        files = files.DistinctBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).Where(f => f.Length >= minSize).ToList();

        var bySize = files.GroupBy(f => f.Length).Where(g => g.Count() > 1).ToList();
        long totalBytes = Math.Max(1, bySize.Sum(g => g.Key * g.Count()));
        long hashed = 0;
        var groups = new List<DuplicateGroup>();
        foreach (var sizeGroup in bySize)
        {
            ct.ThrowIfCancellationRequested();
            var byHead = GroupBy(sizeGroup, f => HashHead(f.FullName), errors);
            foreach (var headGroup in byHead.Where(g => g.Count() > 1))
            {
                var candidates = headGroup.ToList();
                // File ≤ 64 KB: băm phần đầu đã là băm toàn bộ.
                var byFull = sizeGroup.Key <= HeadBytes
                    ? [headGroup]
                    : GroupBy(candidates, f => HashAll(f.FullName, ct), errors);
                foreach (var full in byFull.Where(g => g.Count() > 1))
                {
                    groups.Add(new DuplicateGroup(sizeGroup.Key, full.Key, full.Select(f => f.FullName).Order(NaturalComparer.Instance).ToList()));
                }
                hashed += sizeGroup.Key * candidates.Count;
                throttle.Report(hashed / (double)totalBytes, $"{groups.Count:N0} nhóm trùng");
            }
        }
        throttle.Report(1, null, force: true);
        return new DuplicateResult(groups.OrderByDescending(g => g.Wasted).ToList(), files.Count, hashed, errors);
    }

    private static List<IGrouping<string, FileInfo>> GroupBy(IEnumerable<FileInfo> files, Func<FileInfo, string> hash, List<string> errors)
    {
        var keyed = new List<(string Key, FileInfo File)>();
        foreach (var f in files)
        {
            try
            {
                keyed.Add((hash(f), f));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{f.FullName}: {ex.Message}");
            }
        }
        return keyed.GroupBy(k => k.Key, k => k.File).ToList();
    }

    private static string HashHead(string path)
    {
        var buffer = new byte[HeadBytes];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return Convert.ToHexString(XxHash128.Hash(buffer.AsSpan(0, read)));
    }

    private static string HashAll(string path, CancellationToken ct)
    {
        var hasher = new XxHash128();
        var buffer = new byte[1 << 20];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hasher.Append(buffer.AsSpan(0, read));
        }
        return Convert.ToHexString(hasher.GetCurrentHash());
    }
}
