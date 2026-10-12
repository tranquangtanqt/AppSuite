using System.Text.RegularExpressions;
using MeRon.Core.Data;

namespace MeRon.Core.Import;

/// <summary>
/// 1 bộ backup đã có sẵn trên máy: file TSV của từng bảng. <see cref="Name"/> = tên thư mục backup của điện thoại
/// (thời điểm xuất, vd 20261011_231039).
/// </summary>
public sealed class BackupFileSet
{
    public required string Name { get; init; }

    /// <summary>Nơi lấy (đường dẫn thư mục, sau này link Google Drive) - để hiện và ghi nhớ.</summary>
    public required string Source { get; init; }

    public required IReadOnlyDictionary<TableDef, string> Files { get; init; }

    public IReadOnlyList<TableDef> Missing => Schema.BackupTables.Where(t => !Files.ContainsKey(t)).ToList();
}

/// <summary>
/// Nguồn lấy backup. Phần nạp vào SQLite (<see cref="BackupImporter"/>) chỉ làm việc với <see cref="BackupFileSet"/>
/// (file trên máy), nên thêm nguồn mới (Google Drive: tải 8 file về thư mục tạm) không phải sửa phần nạp.
/// </summary>
public interface IBackupSource
{
    Task<BackupFileSet> GetLatestAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Thư mục trên máy. Chấp nhận 2 kiểu:
/// - chính thư mục 1 lần backup (chứa orders_*.tsv, users_*.tsv...);
/// - thư mục cha chứa nhiều lần backup (Download/MeRon của điện thoại, hoặc folder tải từ Google Drive) → tự chọn
///   thư mục con mới nhất có tên dạng yyyyMMdd_HHmmss (bỏ qua bản phụ như 20260712_225408_bk).
/// </summary>
public sealed partial class LocalFolderBackupSource(string path) : IBackupSource
{
    [GeneratedRegex(@"^\d{8}_\d{6}$")]
    private static partial Regex BackupFolderName();

    public Task<BackupFileSet> GetLatestAsync(CancellationToken cancellationToken = default) => Task.Run(Resolve, cancellationToken);

    public BackupFileSet Resolve()
    {
        if (!Directory.Exists(path))
        {
            throw new BackupSourceException($"Không tìm thấy thư mục: {path}");
        }

        var folder = path;
        if (!HasAnyBackupFile(folder))
        {
            var latest = Directory.EnumerateDirectories(folder)
                .Where(d => BackupFolderName().IsMatch(System.IO.Path.GetFileName(d)) && HasAnyBackupFile(d))
                .OrderByDescending(d => System.IO.Path.GetFileName(d), StringComparer.Ordinal)
                .FirstOrDefault();
            folder = latest ?? throw new BackupSourceException(
                "Thư mục này không có file backup (.tsv) nào, và cũng không có thư mục con dạng yyyyMMdd_HHmmss chứa file backup.");
        }

        return new BackupFileSet
        {
            Name = System.IO.Path.GetFileName(folder.TrimEnd('\\', '/')),
            Source = folder,
            Files = FindFiles(folder),
        };
    }

    private static bool HasAnyBackupFile(string folder) => FindFiles(folder).Count > 0;

    /// <summary>File của bảng X: "x_*.tsv" (không phân biệt hoa thường). "orders_" không khớp "orderitems_..." nên không lẫn.
    /// Có nhiều file cùng bảng thì lấy tên lớn nhất (= thời điểm xuất mới nhất).</summary>
    internal static Dictionary<TableDef, string> FindFiles(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.tsv").ToList();
        var result = new Dictionary<TableDef, string>();
        foreach (var table in Schema.BackupTables)
        {
            var match = files
                .Where(f => System.IO.Path.GetFileName(f).StartsWith(table.FilePrefix + "_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => System.IO.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match is not null)
            {
                result[table] = match;
            }
        }
        return result;
    }
}

public sealed class BackupSourceException(string message) : Exception(message);
