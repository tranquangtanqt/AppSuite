using System.Globalization;
using System.Text;
using MeRon.Core.Data;

namespace MeRon.Core.Import;

public sealed record ExportResult(string Folder, string Name, IReadOnlyDictionary<string, int> RowCounts);

/// <summary>
/// Xuất 8 bảng ra bộ backup TSV đúng định dạng của điện thoại (BackupService.exportToLocal) để điện thoại nhập lại được
/// (Cài đặt → Sao lưu → Nhập dữ liệu từ tệp sao lưu) và bản máy tính khác nhập được:
/// thư mục &lt;yyyyMMdd_HHmmss&gt;, file &lt;bảng viết thường&gt;_&lt;yyyyMMdd_HHmmss&gt;.tsv, UTF-8 không BOM, dòng 1 = tên cột theo thứ tự
/// cột của bảng, tab ngăn cách, "\n" xuống dòng (không có xuống dòng cuối file), null → ô trống, các dòng theo thứ tự rowid.
/// Khác điện thoại có chủ đích:
/// - tab / xuống dòng trong dữ liệu đổi thành dấu cách, gồm cả "\r" (ghi chú gõ nhiều dòng trên Windows) - điện thoại đọc
///   bằng readAsLines nên "\r" sẽ cắt đôi dòng;
/// - bảng trống ghi dòng tiêu đề (điện thoại ghi file rỗng, và chính màn Nhập của điện thoại báo lỗi "File rỗng" với file đó).
/// </summary>
public static class BackupExporter
{
    public static string StampOf(DateTime time) => time.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

    /// <summary>Tạo thư mục &lt;parent&gt;\&lt;stamp&gt; và ghi 8 file. Ghi vào thư mục tạm rồi đổi tên - lỗi giữa chừng không để lại bộ dở.</summary>
    public static ExportResult Export(MeRonDatabase database, string parentFolder, DateTime now)
    {
        var stamp = StampOf(now);
        var folder = Path.Combine(parentFolder, stamp);
        if (Directory.Exists(folder))
        {
            throw new IOException($"Thư mục {folder} đã có - đợi 1 giây rồi xuất lại.");
        }
        var temp = folder + ".partial";
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
        Directory.CreateDirectory(temp);

        var counts = new Dictionary<string, int>();
        try
        {
            using var connection = database.Open();
            foreach (var table in Schema.BackupTables)
            {
                var builder = new StringBuilder();
                builder.Append(string.Join('\t', table.Columns.Select(c => c.Name)));
                var rows = 0;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT {string.Join(", ", table.Columns.Select(c => c.Name))} FROM {table.Name} ORDER BY rowid";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        builder.Append('\n');
                        for (var i = 0; i < table.Columns.Count; i++)
                        {
                            if (i > 0)
                            {
                                builder.Append('\t');
                            }
                            builder.Append(Cell(reader.IsDBNull(i) ? null : reader.GetValue(i)));
                        }
                        rows++;
                    }
                }
                counts[table.Name] = rows;
                File.WriteAllText(Path.Combine(temp, $"{table.FilePrefix}_{stamp}.tsv"), builder.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
            }
            throw;
        }

        Directory.Move(temp, folder);
        return new ExportResult(folder, stamp, counts);
    }

    /// <summary>Giá trị 1 ô: số nguyên in thường (không nhóm nghìn), chữ giữ nguyên trừ tab / xuống dòng → dấu cách.</summary>
    internal static string Cell(object? value) => value switch
    {
        null => string.Empty,
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '),
    };
}
