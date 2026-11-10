using System.Text;
using System.Text.Json;
using CsvEditor.Models;

namespace CsvEditor.Services;

/// <summary>Bản khôi phục khi app sắp tắt vì lỗi mà còn thay đổi chưa lưu: chép toàn bộ bảng ra
/// Data\Recovery\&lt;tên file&gt;.khoi-phuc-&lt;thời gian&gt;.csv cạnh exe (file gốc không bị đụng tới), kèm 1 file .json cùng tên ghi
/// đường dẫn gốc, bảng mã, dấu phân cách, có dòng tiêu đề không. Lần mở sau app hỏi các bản còn .json (<see cref="Pending"/>);
/// mở hoặc xoá thì bỏ .json (<see cref="Dismiss"/>), bản .csv đã mở giữ thêm <see cref="KeepDays"/> ngày rồi tự dọn (phòng khi
/// người dùng lỡ không lưu lại).</summary>
public sealed class RecoveryStore(string? folder = null)
{
    public const int KeepDays = 30;
    private const string Marker = ".khoi-phuc-";

    public string Folder { get; } = folder ?? Path.Combine(AppContext.BaseDirectory, "Data", "Recovery");

    /// <summary>Ghi bản khôi phục, trả đường dẫn .csv; bảng chưa có cột nào thì không ghi (null). Chờ ghi xong mới trả về:
    /// dùng lúc app đang lỗi, sắp tắt.</summary>
    public string? Save(CsvDocument document, DateTime now)
    {
        if (document.Columns.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(Folder);
        var original = string.IsNullOrEmpty(document.FilePath) ? null : document.FilePath;
        var baseName = original is null ? "bang-moi" : Path.GetFileNameWithoutExtension(original);
        var extension = original is null ? ".csv" : Path.GetExtension(original);
        var path = Path.Combine(Folder, $"{baseName}{Marker}{now:yyyyMMdd-HHmmss}{extension}");
        for (var i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(Folder, $"{baseName}{Marker}{now:yyyyMMdd-HHmmss}-{i}{extension}");
        }

        // CsvSerializer chỉ có bản async: chạy trên luồng nền rồi chờ - chờ thẳng trên luồng giao diện có thể treo (phần tiếp
        // theo sau await xếp hàng vào chính luồng đang chờ).
        Task.Run(async () =>
        {
            await using var writer = new StreamWriter(path, append: false, document.Encoding);
            await CsvSerializer.WriteAsync(writer, document, CancellationToken.None);
        }).GetAwaiter().GetResult();

        var info = new RecoveryInfo(original, document.Encoding.CodePage, document.Encoding.GetPreamble().Length > 0,
            document.Delimiter, document.HasHeader, now, document.Rows.Count, document.Columns.Count);
        File.WriteAllText(InfoPath(path), JsonSerializer.Serialize(info));
        return path;
    }

    /// <summary>Các bản chưa mở / chưa xoá, mới nhất trước. File .json hỏng hoặc mất .csv thì bỏ qua.</summary>
    public IReadOnlyList<RecoveryEntry> Pending()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var entries = new List<RecoveryEntry>();
        foreach (var infoPath in Directory.EnumerateFiles(Folder, $"*{Marker}*.json"))
        {
            var csvPath = infoPath[..^".json".Length];
            try
            {
                if (File.Exists(csvPath) && JsonSerializer.Deserialize<RecoveryInfo>(File.ReadAllText(infoPath)) is { } info)
                {
                    entries.Add(new RecoveryEntry(csvPath, info));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        return entries.OrderByDescending(entry => entry.Info.SavedAt).ToList();
    }

    /// <summary>Thôi hỏi về bản này: bỏ .json; <paramref name="deleteFile"/> thì xoá luôn .csv.</summary>
    public void Dismiss(RecoveryEntry entry, bool deleteFile)
    {
        File.Delete(InfoPath(entry.Path));
        if (deleteFile)
        {
            File.Delete(entry.Path);
        }
    }

    /// <summary>Xoá bản khôi phục đã mở / bỏ hỏi (không còn .json) cũ hơn <see cref="KeepDays"/> ngày. Lỗi thì thôi.</summary>
    public void Cleanup(DateTime now)
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(Folder, $"*{Marker}*"))
            {
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !File.Exists(InfoPath(file))
                    && File.GetLastWriteTime(file) < now.AddDays(-KeepDays))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string InfoPath(string csvPath) => csvPath + ".json";
}

/// <summary>Thông tin đi kèm bản khôi phục. <see cref="OriginalPath"/> null = bảng mới chưa lưu lần nào.</summary>
public sealed record RecoveryInfo(
    string? OriginalPath, int CodePage, bool HasPreamble, char Delimiter, bool HasHeader, DateTime SavedAt, int RowCount, int ColumnCount)
{
    /// <summary>Bảng mã để mở lại đúng như lúc ghi (UTF-8 có / không BOM, Shift-JIS...).</summary>
    public Encoding GetEncoding() => CodePage == Encoding.UTF8.CodePage
        ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: HasPreamble)
        : Encoding.GetEncoding(CodePage);
}

public sealed record RecoveryEntry(string Path, RecoveryInfo Info);
