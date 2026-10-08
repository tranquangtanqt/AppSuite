using System.Text;
using SkiaSharp;

namespace Common.Ocr;

/// <summary>Tiện ích nhỏ dùng chung của thư viện OCR (trước nằm trong Engine của ImageCompare).</summary>
public static class OcrText
{
    /// <summary>Định dạng pixel mọi ảnh trong AppSuite (BGRA premultiplied) - ảnh cắt ra để đọc cũng theo định dạng này.</summary>
    public const SKColorType ColorType = SKColorType.Bgra8888;
    public const SKAlphaType AlphaType = SKAlphaType.Premul;

    /// <summary>Nối các từ để hiển thị: chỉ chèn dấu cách giữa 2 chữ Latin / số (OCR tiếng Nhật trả từng ký tự là 1
    /// "từ" - "受 注 番 号" → "受注番号").</summary>
    public static string JoinWords(IEnumerable<string> words)
    {
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (sb.Length > 0 && IsLatin(sb[^1]) && IsLatin(word[0]))
            {
                sb.Append(' ');
            }
            sb.Append(word);
        }
        return sb.ToString();

        static bool IsLatin(char c) => c < 0x3000 && char.IsLetterOrDigit(c);
    }

    /// <summary>Nối các từ của 1 dòng OCR để hiển thị / copy: cách nhau 1 dấu cách, trừ khi 1 trong 2 bên là chữ CJK / toàn
    /// độ rộng (Windows OCR tiếng Nhật trả từng ký tự là 1 từ: "受 注 番 号" → "受注番号"). Khác <see cref="JoinWords"/>:
    /// giữ dấu cách sau dấu câu Latin ("Trạng thái: Đã giao").</summary>
    public static string JoinLine(IEnumerable<string> words)
    {
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (word.Length == 0)
            {
                continue;
            }
            if (sb.Length > 0 && !IsWide(sb[^1]) && !IsWide(word[0]))
            {
                sb.Append(' ');
            }
            sb.Append(word);
        }
        return sb.ToString();

        static bool IsWide(char c) => c >= 0x2E80;
    }

    /// <summary>Độ sáng (Rec. 601) của 1 pixel BGRA.</summary>
    public static float Luma(uint bgra) =>
        0.114f * (bgra & 0xFF) + 0.587f * ((bgra >> 8) & 0xFF) + 0.299f * ((bgra >> 16) & 0xFF);
}
