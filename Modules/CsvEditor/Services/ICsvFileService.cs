using System.Text;
using CsvEditor.Models;

namespace CsvEditor.Services;

public sealed record CsvOpenResult(
    CsvDocument Document,
    EncodingDetectionResult EncodingResult,
    DelimiterDetectionResult DelimiterResult,
    IReadOnlyList<ValidationIssue> Issues);

public interface ICsvFileService
{
    /// <summary>
    /// Opens and parses <paramref name="filePath"/> off the UI thread. <paramref name="confirmLargeFile"/>
    /// is awaited with the estimated row count before the full read starts if that estimate exceeds
    /// <see cref="CsvFileService.LargeFileRowThreshold"/> - return false to abort (the method then
    /// throws <see cref="OperationCanceledException"/>). Pass explicit <paramref name="encodingOverride"/>/
    /// <paramref name="delimiterOverride"/> to skip auto-detection (e.g. the user picked one manually).
    /// </summary>
    Task<CsvOpenResult> OpenAsync(
        string filePath,
        Encoding? encodingOverride,
        char? delimiterOverride,
        Func<long, Task<bool>> confirmLargeFile,
        IProgress<int>? progress,
        CancellationToken cancellationToken);

    Task SaveAsync(CsvDocument document, IProgress<int>? progress, CancellationToken cancellationToken);

    Task SaveAsAsync(CsvDocument document, string filePath, IProgress<int>? progress, CancellationToken cancellationToken);
}
