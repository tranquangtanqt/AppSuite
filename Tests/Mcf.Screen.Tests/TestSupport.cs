using OfficeOpenXml;

namespace Mcf.Screen.Tests;

/// <summary>Thư mục tạm riêng cho mỗi test, tự xoá khi xong.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Mcf.Screen.Tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string Sub(string name)
    {
        var dir = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Dựng sheet 画面説明書: mọi sheet mở đầu bằng khối header "mcframe 7" 3 dòng + dòng trống + dòng tiêu đề
/// lặp tên sheet (được bỏ qua khi render), nội dung bắt đầu từ <see cref="ContentRow"/>.</summary>
internal static class Sheets
{
    public const int ContentRow = 7;

    public static ExcelWorksheet Add(ExcelPackage package, string name, bool withDocHeader = true)
    {
        var s = package.Workbook.Worksheets.Add(name);
        if (withDocHeader)
        {
            Write(s, 1, "mcframe 7", "文書名", "画面説明書", "文書番号", "M7-US-1102");
            Write(s, 2, "", "モジュール", "販売管理", "Version", "1.0");
            Write(s, 3, "", "", "", "Rev.", "02");
            Write(s, 5, "", name);
        }
        return s;
    }

    /// <summary>Ghi từ cột A; chuỗi rỗng = bỏ trống ô đó.</summary>
    public static void Write(ExcelWorksheet s, int row, params string[] cells)
    {
        for (int c = 0; c < cells.Length; c++)
        {
            if (cells[c].Length > 0)
            {
                s.Cells[row, c + 1].Value = cells[c];
            }
        }
    }

    public static void Save(string path, Action<ExcelPackage> build)
    {
        using var p = new ExcelPackage();
        build(p);
        p.SaveAs(new FileInfo(path));
    }
}
