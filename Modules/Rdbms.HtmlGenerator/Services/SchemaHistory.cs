using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>1 bản đọc cũ trong thư mục Lịch sử. <paramref name="ReadAt"/> = lúc bản đó được đọc (giờ sửa file .db gốc).</summary>
public sealed record HistoryEntry(string Path, DateTime ReadAt);

/// <summary>
/// Giữ các lần đọc trước: "1. Đọc database" ghi đè <c>{tên}.db</c>, nên ngay trước khi ghi đè, file cũ được chép vào
/// <c>Data\Database\Lịch sử\{tên}\yyyy-MM-dd HHmmss.db</c> (tên = lúc file cũ được đọc) - nhờ đó So sánh schema "bản cũ ↔
/// bản mới" không phải tự chép file. Lần đọc giống hệt lần trước thì ViewModel xoá bản vừa chép (không thêm bản trùng),
/// nên bản mới nhất trong Lịch sử luôn là bản trước lần thay đổi gần nhất.
/// </summary>
public static class SchemaHistory
{
    public const string FolderName = "Lịch sử";
    public const int DefaultKeep = 10;
    public const int MaxKeep = 50;

    private const string TimeFormat = "yyyy-MM-dd HHmmss";

    /// <summary>Thư mục lịch sử của 1 file .db: <c>{thư mục của .db}\Lịch sử\{tên .db không đuôi}</c>.</summary>
    public static string FolderFor(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(databasePath)!, FolderName, Path.GetFileNameWithoutExtension(databasePath));

    /// <summary>Chép <paramref name="databasePath"/> vào thư mục lịch sử của nó (giữ giờ sửa file). Trả đường dẫn bản chép,
    /// null nếu chưa có file. Trùng tên (cùng giây) thì ghi đè - cùng 1 lần đọc.</summary>
    public static string? Save(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }
        var folder = FolderFor(databasePath);
        Directory.CreateDirectory(folder);
        var readAt = File.GetLastWriteTime(databasePath);
        var target = Path.Combine(folder, readAt.ToString(TimeFormat) + ".db");
        File.Copy(databasePath, target, overwrite: true); // File.Copy giữ nguyên giờ sửa file
        return target;
    }

    /// <summary>Các bản cũ của 1 file .db, mới nhất lên trước. Thời điểm lấy từ tên file (đổi giờ sửa file không ảnh hưởng);
    /// file đặt tên khác thì lấy giờ sửa file.</summary>
    public static List<HistoryEntry> List(string databasePath)
    {
        var folder = FolderFor(databasePath);
        if (!Directory.Exists(folder))
        {
            return [];
        }
        return Directory.GetFiles(folder, "*.db")
            .Select(path => new HistoryEntry(path, ParseReadAt(path)))
            .OrderByDescending(e => e.ReadAt)
            .ToList();
    }

    /// <summary>Bản cũ mới nhất (null = chưa có).</summary>
    public static HistoryEntry? Latest(string databasePath) => List(databasePath).FirstOrDefault();

    /// <summary>Chỉ giữ <paramref name="keep"/> bản mới nhất (0 = xoá hết). Trả số file đã xoá; file đang bị khoá thì bỏ qua.</summary>
    public static int Prune(string databasePath, int keep)
    {
        var removed = 0;
        foreach (var entry in List(databasePath).Skip(Math.Max(keep, 0)))
        {
            try
            {
                File.Delete(entry.Path);
                removed++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }

    private static DateTime ParseReadAt(string path) =>
        DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), TimeFormat, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var readAt)
            ? readAt
            : File.GetLastWriteTime(path);
}
