using System.Text;
using DbTools.Core;

namespace DbTools.Tests;

/// <summary>Trang Chạy → Chuyển UTF-8: bản UTF-8 của file Shift-JIS (thư mục khác / ghi đè) - 2026-11-23.</summary>
public class Utf8ConvertTests
{
    private static readonly Encoding Sjis = GetSjis();

    private static Encoding GetSjis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    private static List<(string, string)> Files(TempFolder src)
    {
        src.Write("TABLE_受入明細.sql", "\\encoding SJIS\nCOMMENT ON TABLE t IS '受入明細';\n", Sjis);
        src.Write(Path.Combine("sub", "ALTER 木型マスタ.sql"), "ALTER TABLE m ADD COLUMN c text; -- 木型\r\n", Sjis);
        src.Write("utf8.sql", "SELECT 'đã là UTF-8 受注';\n", new UTF8Encoding(false));
        src.Write("bom.sql", "SELECT 1;\n", new UTF8Encoding(true));
        return Directory.EnumerateFiles(src.Path, "*.sql", SearchOption.AllDirectories)
            .Select(p => (p, Path.GetRelativePath(src.Path, p))).ToList();
    }

    [Fact]
    public void Copy_to_folder_converts_shift_jis_copies_utf8_and_keeps_subfolders()
    {
        using var src = new TempFolder();
        using var dest = new TempFolder();
        var target = Utf8Convert.TargetFolder(dest.Path, src.Path + "\\", new DateTime(2026, 11, 23, 13, 5, 9));
        Assert.EndsWith("_20261123_130509", target);

        var results = Utf8Convert.Run(Files(src), target);

        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.Null(r.Error));
        Assert.Equal(2, results.Count(r => r.Converted));
        var table = File.ReadAllBytes(Path.Combine(target, "TABLE_受入明細.sql"));
        Assert.NotEqual(0xEF, table[0]); // không BOM
        Assert.Equal("\\encoding UTF8\nCOMMENT ON TABLE t IS '受入明細';\n", new UTF8Encoding(false, true).GetString(table));
        Assert.Equal("ALTER TABLE m ADD COLUMN c text; -- 木型\r\n", File.ReadAllText(Path.Combine(target, "sub", "ALTER 木型マスタ.sql"), Encoding.UTF8));
        Assert.Equal(File.ReadAllBytes(Path.Combine(src.Path, "bom.sql")), File.ReadAllBytes(Path.Combine(target, "bom.sql"))); // chép nguyên
        // File gốc không đổi.
        Assert.Equal("Shift-JIS", SqlScriptFile.Load(Path.Combine(src.Path, "TABLE_受入明細.sql"), src.Path).EncodingName);
        // Bản mới đọc lại là UTF-8, cùng câu lệnh.
        var reread = SqlScriptFile.Load(Path.Combine(target, "TABLE_受入明細.sql"), target);
        Assert.Equal("UTF-8", reread.EncodingName);
        Assert.Contains("受入明細", reread.Statements[0].Statement.Text);
    }

    [Fact]
    public void Overwrite_only_touches_non_utf8_files()
    {
        using var src = new TempFolder();
        var files = Files(src);
        var utf8Before = File.GetLastWriteTimeUtc(Path.Combine(src.Path, "utf8.sql"));

        var results = Utf8Convert.Run(files, null);

        Assert.Equal(new[] { "TABLE_受入明細.sql", Path.Combine("sub", "ALTER 木型マスタ.sql") }.Order(StringComparer.Ordinal),
            results.Select(r => r.RelativePath).Order(StringComparer.Ordinal));
        Assert.All(results, r => Assert.True(r.Converted));
        Assert.All(files, f => Assert.StartsWith("UTF-8", SqlScriptFile.Load(f.Item1, src.Path).EncodingName));
        Assert.Equal(utf8Before, File.GetLastWriteTimeUtc(Path.Combine(src.Path, "utf8.sql")));
        Assert.Empty(Directory.EnumerateFiles(src.Path, "*.utf8tmp", SearchOption.AllDirectories));

        // Chạy lại: không còn gì để chuyển.
        Assert.Empty(Utf8Convert.Run(files, null));
    }

    [Fact]
    public void Missing_file_is_reported_not_thrown()
    {
        using var dest = new TempFolder();
        var result = Assert.Single(Utf8Convert.Run([(Path.Combine(dest.Path, "khong_co.sql"), "khong_co.sql")], dest.Path));
        Assert.NotNull(result.Error);
        Assert.False(result.Converted);
    }
}
