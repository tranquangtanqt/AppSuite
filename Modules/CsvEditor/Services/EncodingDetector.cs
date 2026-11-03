using System.Text;
using CsvEditor.Models;

namespace CsvEditor.Services;

/// <summary>
/// Sniffs a file's text encoding from its byte preamble (BOM), falling back to trying a strict UTF-8
/// decode of the first block when no BOM is present, then a strict Shift-JIS decode. See PLAN.md for the exact heuristic.
/// </summary>
public sealed class EncodingDetector : IEncodingDetector
{
    private const int SampleBytes = 8192;

    public async Task<EncodingDetectionResult> DetectAsync(string filePath, CancellationToken cancellationToken)
    {
        var buffer = new byte[SampleBytes];
        int read;
        await using (var stream = File.OpenRead(filePath))
        {
            read = await stream.ReadAsync(buffer.AsMemory(0, SampleBytes), cancellationToken);
        }

        if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return new EncodingDetectionResult(new UTF8Encoding(true), DetectionConfidence.High, null);
        }

        if (read >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
        {
            return new EncodingDetectionResult(Encoding.Unicode, DetectionConfidence.High, null);
        }

        if (read >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
        {
            return new EncodingDetectionResult(Encoding.BigEndianUnicode, DetectionConfidence.High, null);
        }

        // No BOM: a UTF-16 file with mostly ASCII content has a very regular pattern of 0x00 bytes on
        // every other position. Check that before assuming UTF-8.
        if (read >= 4 && LooksLikeUtf16NoBom(buffer, read))
        {
            return new EncodingDetectionResult(
                Encoding.Unicode,
                DetectionConfidence.Medium,
                "Không có BOM, đoán là UTF-16 dựa trên mẫu byte 0x00 xen kẽ - kiểm tra lại nếu văn bản hiển thị sai.");
        }

        // Mẫu chỉ là 8 KB đầu: chữ nhiều byte bị cắt ở cuối mẫu không phải lỗi (trừ khi đã hết file).
        var wholeFile = read < SampleBytes;
        if (TryDecodeStrict(new UTF8Encoding(false), buffer, read, wholeFile, out _))
        {
            return new EncodingDetectionResult(new UTF8Encoding(false), DetectionConfidence.High, null);
        }

        // Không phải UTF-8: file từ hệ thống / Excel tiếng Nhật thường là Shift-JIS (CP932). Đọc được trọn vẹn theo
        // Shift-JIS thì dùng luôn; trông như tiếng Nhật thật thì chắc chắn, không thì vẫn dùng nhưng nhắc kiểm tra.
        var shiftJis = ShiftJis;
        if (TryDecodeStrict(shiftJis, buffer, read, wholeFile, out var sjisText))
        {
            return LooksJapanese(sjisText)
                ? new EncodingDetectionResult(shiftJis, DetectionConfidence.High, null)
                : new EncodingDetectionResult(
                    shiftJis,
                    DetectionConfidence.Medium,
                    "Không phải UTF-8, đoán là Shift-JIS (tiếng Nhật) - nếu chữ hiển thị sai, bấm nhãn bảng mã ở thanh trạng thái để chọn lại.");
        }

        return new EncodingDetectionResult(
            new UTF8Encoding(false),
            DetectionConfidence.Low,
            "Không xác định được encoding (không phải UTF-8 / UTF-16 / Shift-JIS hợp lệ), dùng UTF-8 mặc định - chọn lại thủ công nếu văn bản hiển thị sai.");
    }

    /// <summary>Shift-JIS của Windows (CP932, có ký tự mở rộng NEC / IBM như ①, ㈱). .NET mặc định không có code page
    /// cũ - đăng ký provider ở đây (gọi lại nhiều lần không sao) để cả project test, không qua App, cũng dùng được.</summary>
    private static Encoding ShiftJis
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932);
        }
    }

    /// <summary>Giải mã không thay thế byte lỗi. <paramref name="flush"/> = false: byte dở dang ở cuối mẫu (chữ bị cắt
    /// giữa chừng) được bỏ qua thay vì tính là lỗi.</summary>
    private static bool TryDecodeStrict(Encoding encoding, byte[] buffer, int length, bool flush, out string text)
    {
        var strict = (Encoding)encoding.Clone();
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        var chars = new char[strict.GetMaxCharCount(length)];
        try
        {
            var count = strict.GetDecoder().GetChars(buffer, 0, length, chars, 0, flush);
            text = new string(chars, 0, count);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    /// <summary>Có kana (cả kana nửa cỡ ｶﾅ hay gặp ở file hệ thống cũ) hoặc ít nhất 2 kanji / chữ toàn cỡ liền nhau (tên,
    /// địa chỉ chỉ có kanji như 山田太郎). File Latin-1 / Windows-1252 đọc nhầm thành Shift-JIS (é, ü… ghép với chữ cái
    /// kế tiếp thành 1 cặp hợp lệ) chỉ ra kanji lẻ loi giữa chữ Latin, không có kana.</summary>
    private static bool LooksJapanese(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (IsKana(text[i]) || (i > 0 && IsWideJapanese(text[i]) && IsWideJapanese(text[i - 1])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKana(char c) => c is (>= 'ぁ' and <= 'ヿ') or (>= 'ｦ' and <= 'ﾟ');

    private static bool IsWideJapanese(char c) =>
        c is (>= '　' and <= 'ヿ') or (>= '一' and <= '鿿') or (>= '！' and <= '｠');

    private static bool LooksLikeUtf16NoBom(byte[] buffer, int length)
    {
        var evenZero = 0;
        var oddZero = 0;
        var pairs = length / 2;
        if (pairs == 0)
        {
            return false;
        }

        for (var i = 0; i < pairs * 2; i += 2)
        {
            if (buffer[i] == 0x00) evenZero++;
            if (buffer[i + 1] == 0x00) oddZero++;
        }

        // Overwhelmingly one lane is zero (ASCII text stored as UTF-16) -> strong signal.
        return evenZero > pairs * 0.6 || oddZero > pairs * 0.6;
    }
}
