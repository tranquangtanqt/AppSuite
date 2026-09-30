using System.Text;

namespace FileTools.Core;

public enum SniffConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>Kết quả nhận encoding. <see cref="Encoding"/> mang đúng preamble của file (UTF-8 có BOM →
/// <c>UTF8Encoding(true)</c>) để ghi lại "giữ như nguồn" ra đúng như cũ.</summary>
public sealed record SniffResult(Encoding Encoding, SniffConfidence Confidence, string? Warning)
{
    public string Name => TextEncodings.Describe(Encoding);
}

/// <summary>
/// Đoán encoding từ 64 KB đầu file: BOM → UTF-16 không BOM (mẫu byte 0x00 xen kẽ) → UTF-8 nghiêm ngặt →
/// Shift-JIS nghiêm ngặt → mặc định UTF-8 kèm cảnh báo. Dựa trên CsvEditor.Services.EncodingDetector (chép - module
/// không reference nhau), thêm bước Shift-JIS vì tài liệu mcframe hay dùng. Mẫu bị cắt giữa 1 ký tự nhiều byte ở
/// cuối không bị tính là lỗi (giải mã với flush = false).
/// </summary>
public static class EncodingSniffer
{
    public const int SampleBytes = 64 * 1024;

    public static SniffResult Detect(string path)
    {
        var buffer = new byte[SampleBytes];
        int read;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        }
        return Detect(buffer.AsSpan(0, read));
    }

    public static SniffResult Detect(ReadOnlySpan<byte> sample)
    {
        if (sample.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return new SniffResult(new UTF8Encoding(true), SniffConfidence.High, null);
        }
        if (sample.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return new SniffResult(new UnicodeEncoding(false, true), SniffConfidence.High, null);
        }
        if (sample.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return new SniffResult(new UnicodeEncoding(true, true), SniffConfidence.High, null);
        }
        if (LooksLikeUtf16NoBom(sample, out bool bigEndian))
        {
            return new SniffResult(new UnicodeEncoding(bigEndian, false), SniffConfidence.Medium,
                "Không có BOM, đoán là UTF-16 dựa trên mẫu byte 0x00 xen kẽ.");
        }
        if (IsAscii(sample))
        {
            return new SniffResult(new UTF8Encoding(false), SniffConfidence.High, null);
        }
        if (DecodesStrictly(new UTF8Encoding(false, throwOnInvalidBytes: true), sample))
        {
            return new SniffResult(new UTF8Encoding(false), SniffConfidence.High, null);
        }
        var shiftJis = Encoding.GetEncoding(TextEncodings.ShiftJisCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        if (DecodesStrictly(shiftJis, sample))
        {
            return new SniffResult(TextEncodings.ShiftJis, SniffConfidence.Medium, "Không phải UTF-8, đoán là Shift-JIS.");
        }
        return new SniffResult(new UTF8Encoding(false), SniffConfidence.Low,
            "Không xác định được encoding (không phải UTF-8 / UTF-16 / Shift-JIS hợp lệ), dùng UTF-8.");
    }

    private static bool IsAscii(ReadOnlySpan<byte> sample)
    {
        foreach (byte b in sample)
        {
            if (b >= 0x80)
            {
                return false;
            }
        }
        return true;
    }

    private static bool DecodesStrictly(Encoding encoding, ReadOnlySpan<byte> sample)
    {
        try
        {
            encoding.GetDecoder().GetCharCount(sample, flush: false);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool LooksLikeUtf16NoBom(ReadOnlySpan<byte> sample, out bool bigEndian)
    {
        bigEndian = false;
        int pairs = sample.Length / 2;
        if (pairs < 2)
        {
            return false;
        }
        int evenZero = 0, oddZero = 0;
        for (int i = 0; i < pairs * 2; i += 2)
        {
            if (sample[i] == 0) evenZero++;
            if (sample[i + 1] == 0) oddZero++;
        }
        // Chữ ASCII lưu UTF-16: 1 trong 2 làn gần như toàn 0x00.
        if (oddZero > pairs * 0.6)
        {
            return true;
        }
        if (evenZero > pairs * 0.6)
        {
            bigEndian = true;
            return true;
        }
        return false;
    }
}
