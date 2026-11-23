using System.Text;
using System.Text.RegularExpressions;

namespace DbTools.Core;

/// <summary>Chỗ ghi bản UTF-8 (trang Chạy → Chuyển UTF-8; nhớ lựa chọn trong settings.json).</summary>
public enum Utf8Target
{
    /// <summary>%TEMP%\AppSuite\DbTools\UTF8\&lt;thư mục&gt;_&lt;giờ&gt;.</summary>
    Temp,

    /// <summary>&lt;thư mục dump&gt;\UTF8\&lt;thư mục&gt;_&lt;giờ&gt;.</summary>
    DumpFolder,

    /// <summary>Ghi đè file gốc (chỉ file không phải UTF-8).</summary>
    Overwrite,
}

/// <summary>1 file đã xử lý: chuyển (Shift-JIS → UTF-8), chép nguyên (đã là UTF-8), hoặc lỗi.</summary>
public sealed record Utf8Result(string RelativePath, string From, string Target, bool Converted, string? Error);

/// <summary>
/// Tạo bản UTF-8 (không BOM - psql đọc BOM thành ký tự lạ ở đầu câu) của các file script không phải UTF-8 để mở / chạy bằng
/// psql, DBeaver... DbTools tự chạy file Shift-JIS được, không cần bước này. Dòng <c>\encoding SJIS</c> trong file đổi thành
/// <c>\encoding UTF8</c> (để nguyên thì psql lại đọc bản UTF-8 theo Shift-JIS). Lưu ra thư mục khác: chép cả các file đã là
/// UTF-8 (giữ cấu trúc thư mục con) để thư mục mới đủ bộ chạy được; ghi đè: chỉ đụng file không phải UTF-8.
/// </summary>
public static class Utf8Convert
{
    private static readonly Regex EncodingLine = new(@"(?im)^([ \t]*\\encoding[ \t]+)\S+", RegexOptions.CultureInvariant);

    /// <summary>File cần chuyển: đọc theo Shift-JIS / UTF-16 (UTF-8 có hay không BOM, kể cả có byte lỗi, thì giữ).</summary>
    public static bool NeedsConversion(string encodingName) => !encodingName.StartsWith("UTF-8", StringComparison.Ordinal);

    /// <summary>Nội dung UTF-8 của file: giải mã như khi chạy, đổi \encoding sang UTF8.</summary>
    public static byte[] ToUtf8(byte[] bytes) =>
        new UTF8Encoding(false).GetBytes(EncodingLine.Replace(SqlScriptFile.Decode(bytes).Text, "${1}UTF8"));

    /// <summary>Thư mục đích khi không ghi đè: &lt;gốc&gt;\&lt;tên thư mục script&gt;_yyyyMMdd_HHmmss.</summary>
    public static string TargetFolder(string baseFolder, string scriptFolder, DateTime now) =>
        Path.Combine(baseFolder, $"{new DirectoryInfo(scriptFolder.TrimEnd('\\', '/')).Name}_{now:yyyyMMdd_HHmmss}");

    /// <param name="files">Đường dẫn đầy đủ + tương đối (so với thư mục script) của các file trong danh sách.</param>
    /// <param name="targetFolder">Thư mục đích; null = ghi đè file gốc.</param>
    public static List<Utf8Result> Run(IEnumerable<(string FullPath, string RelativePath)> files, string? targetFolder)
    {
        var results = new List<Utf8Result>();
        foreach (var (fullPath, relative) in files)
        {
            var target = targetFolder is null ? fullPath : Path.Combine(targetFolder, relative);
            string from = string.Empty;
            try
            {
                var bytes = File.ReadAllBytes(fullPath);
                from = SqlScriptFile.Decode(bytes).EncodingName;
                var convert = NeedsConversion(from);
                if (targetFolder is null && !convert)
                {
                    continue; // ghi đè: file đã là UTF-8 thì không đụng
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var output = convert ? ToUtf8(bytes) : bytes;
                if (targetFolder is null)
                {
                    // Ghi file tạm cạnh file gốc rồi thay: đang ghi mà lỗi thì file gốc còn nguyên.
                    var temp = target + ".utf8tmp";
                    File.WriteAllBytes(temp, output);
                    File.Move(temp, target, overwrite: true);
                }
                else
                {
                    File.WriteAllBytes(target, output);
                }
                results.Add(new Utf8Result(relative, from, target, convert, null));
            }
            catch (Exception ex)
            {
                results.Add(new Utf8Result(relative, from, target, false, ex.Message));
            }
        }
        return results;
    }
}
