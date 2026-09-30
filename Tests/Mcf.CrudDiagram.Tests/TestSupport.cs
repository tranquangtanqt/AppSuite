using OfficeOpenXml;

namespace Mcf.CrudDiagram.Tests;

/// <summary>Thư mục tạm riêng cho mỗi test, tự xoá khi xong.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Mcf.CrudDiagram.Tests", Guid.NewGuid().ToString("N"));

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

/// <summary>Dựng sheet CRUD図 theo đúng bố cục tài liệu mcframe: khối header 3 dòng (nhãn + giá trị cùng dòng,
/// riêng 文書番号 / Version / Rev. có giá trị ngay bên dưới), dòng tiêu đề bảng, rồi các khối ID.</summary>
internal static class CrudSheets
{
    public static readonly string[] Header = ["ID", "名称", "使用オブジェクト", "C", "R", "U", "D", "種", "備考"];

    /// <param name="rows">Các dòng dữ liệu theo thứ tự cột <see cref="Header"/>; null = dòng trống ngăn khối.</param>
    public static ExcelWorksheet Add(ExcelPackage package, string sheetName, string logicName, IEnumerable<string[]?> rows,
        int headerRow = 5, int firstCol = 1)
    {
        var s = package.Workbook.Worksheets.Add(sheetName);
        s.Cells[1, 1].Value = "モジュールID";
        s.Cells[1, 2].Value = "MSB";
        s.Cells[1, 3].Value = "モジュール名";
        s.Cells[1, 4].Value = "販売管理";
        s.Cells[1, 5].Value = "文書番号";
        s.Cells[1, 6].Value = "Version";
        s.Cells[1, 7].Value = "Rev.";
        s.Cells[2, 1].Value = "ｻﾌﾞﾓｼﾞｭｰﾙID";
        s.Cells[2, 2].Value = "BB";
        s.Cells[2, 3].Value = "ｻﾌﾞﾓｼﾞｭｰﾙ名";
        s.Cells[2, 4].Value = "受注";
        s.Cells[2, 5].Value = "M7-DV-0221";
        s.Cells[2, 6].Value = "1.0";
        s.Cells[2, 7].Value = "02";
        s.Cells[3, 1].Value = "ロジック名";
        s.Cells[3, 2].Value = logicName;

        for (int c = 0; c < Header.Length; c++)
        {
            s.Cells[headerRow, firstCol + c].Value = Header[c];
        }
        int r = headerRow + 1;
        foreach (var row in rows)
        {
            if (row is not null)
            {
                for (int c = 0; c < row.Length; c++)
                {
                    if (row[c].Length > 0)
                    {
                        s.Cells[r, firstCol + c].Value = row[c];
                    }
                }
            }
            r++;
        }
        // Dòng trống cuối không có giá trị nào thì Dimension không tính tới - đặt 1 ô trống có style để giữ.
        s.Cells[r - 1, firstCol].Style.Font.Bold = true;
        return s;
    }

    public static string[] Block(string id, string name) => [id, name, "", "", "", "", "", "", ""];

    public static string[] Obj(string usedObject, string c = "", string r = "", string u = "", string d = "", string kind = "", string remark = "") =>
        ["", "", usedObject, c, r, u, d, kind, remark];
}
