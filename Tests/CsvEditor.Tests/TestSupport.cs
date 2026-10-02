using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CsvEditor.Models;
using CsvEditor.Services;

namespace CsvEditor.Tests;

internal static class TestSetup
{
    /// <summary>Sort/Filter/Statistics parse số bằng culture hiện tại (double.TryParse) - cố định culture
    /// để kết quả test không đổi theo máy (vd máy tiếng Việt dùng dấu phẩy thập phân).</summary>
    [ModuleInitializer]
    internal static void FixCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}

/// <summary>Thư mục tạm riêng cho mỗi test, tự xoá khi xong.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CsvEditor.Tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Ghi <paramref name="text"/> ra file với encoding cho trước (có/không BOM theo encoding).</summary>
    public string Write(string name, string text, Encoding encoding)
    {
        var path = File(name);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        System.IO.File.WriteAllBytes(path, [.. preamble, .. body]);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* file còn bị giữ - bỏ qua */ }
    }
}

internal static class Csv
{
    public static readonly UTF8Encoding Utf8NoBom = new(false);
    public static readonly UTF8Encoding Utf8Bom = new(true);

    public static CsvFileService FileService() =>
        new(new EncodingDetector(), new DelimiterDetector(), new ValidationService());

    public static Task<CsvOpenResult> OpenAsync(string path, Encoding? encoding = null, char? delimiter = null, bool hasHeader = true) =>
        FileService().OpenAsync(path, encoding, delimiter, _ => Task.FromResult(true), null, CancellationToken.None, hasHeader);

    /// <summary>Tài liệu trong bộ nhớ: dòng đầu là header.</summary>
    public static CsvDocument Doc(params string[][] headerThenRows)
    {
        var document = new CsvDocument();
        document.ReplaceAll(
            headerThenRows[0].Select(h => new CsvColumn(h)).ToList(),
            headerThenRows.Skip(1).Select(r => new CsvRow(r)).ToList());
        return document;
    }

    public static string[] Cells(CsvRow row, int count) => Enumerable.Range(0, count).Select(row.GetCell).ToArray();

    public static string[][] AllRows(CsvDocument document) =>
        document.Rows.Select(r => Cells(r, document.Columns.Count)).ToArray();

    public static string[] Header(CsvDocument document) => document.Columns.Select(c => c.Name).ToArray();
}
