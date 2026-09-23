using CsvEditor.Models;

namespace CsvEditor.Services;

public interface IDelimiterDetector
{
    DelimiterDetectionResult Detect(IReadOnlyList<string> sampleLines, string filePath);
}
