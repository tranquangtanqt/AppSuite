using System.Text;

namespace CsvEditor.Models;

public enum DetectionConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>Result of sniffing a file's encoding before parsing.</summary>
public sealed record EncodingDetectionResult(Encoding Encoding, DetectionConfidence Confidence, string? Warning);

/// <summary>Result of sniffing a file's delimiter before parsing.</summary>
public sealed record DelimiterDetectionResult(char Delimiter, DetectionConfidence Confidence, string? Warning);
