using ModuleF.Models;

namespace ModuleF.Services;

public interface IDelimiterDetector
{
    DelimiterDetectionResult Detect(IReadOnlyList<string> sampleLines, string filePath);
}
