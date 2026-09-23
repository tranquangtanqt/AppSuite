using System.Text;
using CsvEditor.Models;

namespace CsvEditor.Services;

/// <summary>
/// Sniffs a file's text encoding from its byte preamble (BOM), falling back to trying a strict UTF-8
/// decode of the first block when no BOM is present. See PLAN.md for the exact heuristic.
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

        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer, 0, read);
            return new EncodingDetectionResult(new UTF8Encoding(false), DetectionConfidence.High, null);
        }
        catch (DecoderFallbackException)
        {
            return new EncodingDetectionResult(
                new UTF8Encoding(false),
                DetectionConfidence.Low,
                "Không xác định được encoding (không phải UTF-8/UTF-16 hợp lệ), dùng UTF-8 mặc định - chọn lại thủ công nếu văn bản hiển thị sai.");
        }
    }

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
