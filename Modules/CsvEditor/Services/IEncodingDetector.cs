using CsvEditor.Models;

namespace CsvEditor.Services;

public interface IEncodingDetector
{
    Task<EncodingDetectionResult> DetectAsync(string filePath, CancellationToken cancellationToken);
}
