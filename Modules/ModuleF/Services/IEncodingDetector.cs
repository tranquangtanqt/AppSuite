using ModuleF.Models;

namespace ModuleF.Services;

public interface IEncodingDetector
{
    Task<EncodingDetectionResult> DetectAsync(string filePath, CancellationToken cancellationToken);
}
