using System.Diagnostics;
using FileTools.Core;

namespace FileTools.Services;

/// <summary>Mở kết quả. File CSV/TSV: dùng CsvEditor nếu nó nằm cùng bộ deploy (..\CsvEditor\CsvEditor.exe - quy ước
/// thư mục deploy, không đọc modules.json); còn lại mở bằng ứng dụng mặc định của Windows.</summary>
public static class Shell
{
    /// <summary>Thư mục file tạm (trích dòng, kết quả tìm...) - không bao giờ đụng thư mục của module khác.</summary>
    public static string TempFolder => Path.Combine(Path.GetTempPath(), "AppSuite", "FileTools");

    public static void Open(string path)
    {
        if (Directory.Exists(path))
        {
            Start("explorer.exe", $"\"{path}\"");
            return;
        }
        var csvEditor = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "CsvEditor", "CsvEditor.exe"));
        if (Csv.IsCsvPath(path) && File.Exists(csvEditor))
        {
            Start(csvEditor, $"\"{path}\"");
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Mở thư mục chứa và chọn sẵn file.</summary>
    public static void Reveal(string path)
    {
        if (File.Exists(path))
        {
            Start("explorer.exe", $"/select,\"{path}\"");
        }
        else if (Directory.Exists(path))
        {
            Start("explorer.exe", $"\"{path}\"");
        }
    }

    private static void Start(string file, string args) =>
        Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
}
