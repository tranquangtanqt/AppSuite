using System.Text;

namespace FileTools.Core;

/// <summary>Encoding ghi file đầu ra - mặc định UTF-8 (không BOM), chọn lại được trên từng trang.</summary>
public enum OutputEncoding
{
    Utf8,
    Utf8Bom,
    ShiftJis,
    Utf16Le,
    /// <summary>Giữ encoding của file nguồn (nối nhiều file: theo file đầu tiên).</summary>
    SameAsSource,
}

public static class TextEncodings
{
    public const int ShiftJisCodePage = 932;

    /// <summary>.NET không còn code page cũ (Shift-JIS) trong bảng mặc định - phải đăng ký 1 lần trước khi dùng.
    /// Gọi nhiều lần không sao.</summary>
    public static void Register() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding ShiftJis => Encoding.GetEncoding(ShiftJisCodePage);

    public static Encoding Resolve(OutputEncoding choice, Encoding? source) => choice switch
    {
        OutputEncoding.Utf8 => new UTF8Encoding(false),
        OutputEncoding.Utf8Bom => new UTF8Encoding(true),
        OutputEncoding.ShiftJis => ShiftJis,
        OutputEncoding.Utf16Le => new UnicodeEncoding(false, true),
        _ => source ?? new UTF8Encoding(false),
    };

    public static string DisplayName(OutputEncoding choice) => choice switch
    {
        OutputEncoding.Utf8 => "UTF-8",
        OutputEncoding.Utf8Bom => "UTF-8 có BOM",
        OutputEncoding.ShiftJis => "Shift-JIS",
        OutputEncoding.Utf16Le => "UTF-16 LE",
        _ => "Giữ như file nguồn",
    };

    /// <summary>Tên ngắn gọn để hiện cho người dùng (UTF-8 có / không BOM, Shift-JIS...).</summary>
    public static string Describe(Encoding encoding) => encoding switch
    {
        UTF8Encoding => encoding.GetPreamble().Length > 0 ? "UTF-8 có BOM" : "UTF-8",
        UnicodeEncoding u when u.CodePage == 1201 => "UTF-16 BE",
        UnicodeEncoding => "UTF-16 LE",
        _ when encoding.CodePage == ShiftJisCodePage => "Shift-JIS",
        _ => encoding.WebName,
    };
}
