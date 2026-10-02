using System.Text;
using CsvEditor.Models;

namespace CsvEditor.Services;

/// <summary>
/// Open/Save/SaveAs. Reads run on a background thread with progress + cancellation, but the full
/// parsed result is always fully materialized in memory afterwards - edit/sort/filter/undo need
/// random access to every row, so "don't load everything unless needed" here means "don't block the
/// UI thread while reading", not a disk-backed paging model. See PLAN.md for the reasoning.
/// </summary>
public sealed class CsvFileService : ICsvFileService
{
    /// <summary>Above this estimated row count, OpenAsync asks the caller to confirm before reading
    /// the whole file into RAM.</summary>
    public const long LargeFileRowThreshold = 3_000_000;

    private const int SampleLineCount = 200;
    private const int DelimiterSampleLineCount = 20;

    private readonly IEncodingDetector _encodingDetector;
    private readonly IDelimiterDetector _delimiterDetector;
    private readonly IValidationService _validationService;

    public CsvFileService(IEncodingDetector encodingDetector, IDelimiterDetector delimiterDetector, IValidationService validationService)
    {
        _encodingDetector = encodingDetector;
        _delimiterDetector = delimiterDetector;
        _validationService = validationService;
    }

    public async Task<CsvOpenResult> OpenAsync(
        string filePath,
        Encoding? encodingOverride,
        char? delimiterOverride,
        Func<long, Task<bool>> confirmLargeFile,
        IProgress<int>? progress,
        CancellationToken cancellationToken,
        bool hasHeader = true)
    {
        var encodingResult = encodingOverride is not null
            ? new EncodingDetectionResult(encodingOverride, DetectionConfidence.High, null)
            : await _encodingDetector.DetectAsync(filePath, cancellationToken);

        var (sampleLines, sampleBytes) = await ReadSampleLinesAsync(filePath, encodingResult.Encoding, SampleLineCount, cancellationToken);

        var delimiterResult = delimiterOverride is not null
            ? new DelimiterDetectionResult(delimiterOverride.Value, DetectionConfidence.High, null)
            : _delimiterDetector.Detect(sampleLines.Take(DelimiterSampleLineCount).ToList(), filePath);

        var fileLength = new FileInfo(filePath).Length;
        var estimatedRows = EstimateRowCount(sampleLines.Count, sampleBytes, fileLength);
        if (estimatedRows > LargeFileRowThreshold)
        {
            var proceed = await confirmLargeFile(estimatedRows);
            if (!proceed)
            {
                throw new OperationCanceledException("Người dùng hủy mở file lớn.");
            }
        }

        var issues = new List<ValidationIssue>();
        if (encodingResult.Warning is not null)
        {
            issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, Message = encodingResult.Warning });
        }

        if (delimiterResult.Warning is not null)
        {
            issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, Message = delimiterResult.Warning });
        }

        var document = await Task.Run(
            () => ParseDocument(filePath, encodingResult.Encoding, delimiterResult.Delimiter, hasHeader, fileLength, issues, progress, cancellationToken),
            cancellationToken);

        document.FilePath = filePath;
        document.Encoding = encodingResult.Encoding;
        document.Delimiter = delimiterResult.Delimiter;

        issues.AddRange(_validationService.ValidateColumns(document));

        return new CsvOpenResult(document, encodingResult, delimiterResult, issues);
    }

    public Task SaveAsync(CsvDocument document, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(document.FilePath))
        {
            throw new InvalidOperationException("Chưa có đường dẫn file - dùng Save As.");
        }

        return SaveAsAsync(document, document.FilePath, progress, cancellationToken);
    }

    public async Task SaveAsAsync(CsvDocument document, string filePath, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, document.Encoding))
        {
            await CsvSerializer.WriteAsync(writer, document, cancellationToken);
        }

        progress?.Report(100);
        document.FilePath = filePath;
    }

    private static CsvDocument ParseDocument(
        string filePath,
        Encoding encoding,
        char delimiter,
        bool hasHeader,
        long fileLength,
        List<ValidationIssue> issues,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);

        var columns = new List<CsvColumn>();
        var rows = new List<CsvRow>();
        var rowIndex = 0;
        var lastReportedPercent = -1;

        foreach (var fields in CsvParser.ParseRows(reader, delimiter, issues))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasHeader)
            {
                rows.Add(new CsvRow(fields)); // số cột (dòng dài nhất) + cảnh báo lệch cột tính sau khi đọc hết
            }
            else if (rowIndex == 0)
            {
                columns.AddRange(fields.Select(f => new CsvColumn(f)));
            }
            else
            {
                if (fields.Length != columns.Count)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Dòng {rowIndex + 1}: có {fields.Length} field, header có {columns.Count} cột.",
                        RowIndex = rowIndex,
                        IsRecomputable = true,
                    });
                }

                rows.Add(new CsvRow(fields));
            }

            rowIndex++;

            if (rowIndex % 2000 == 0)
            {
                var percent = fileLength > 0 ? (int)Math.Clamp(stream.Position * 100L / fileLength, 0, 100) : 0;
                if (percent != lastReportedPercent)
                {
                    progress?.Report(percent);
                    lastReportedPercent = percent;
                }
            }
        }

        progress?.Report(100);

        if (!hasHeader)
        {
            // Không có dòng tên cột: số cột = dòng dài nhất (không cắt bớt field của dòng nào khi lưu lại), tên tự đặt.
            var columnCount = rows.Count == 0 ? 0 : rows.Max(r => r.CellCount);
            columns.AddRange(Enumerable.Range(1, columnCount).Select(i => new CsvColumn($"Cột {i}")));
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].CellCount != columnCount)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Dòng {i + 1}: có {rows[i].CellCount} field, bảng có {columnCount} cột.",
                        RowIndex = i,
                        IsRecomputable = true,
                    });
                }
            }
        }

        else if (rows.Count > 0 && rows.Max(r => r.CellCount) is var width && width > columns.Count)
        {
            // Có dòng dài hơn header: thêm cột cho phần dư - không thì Save chỉ ghi đủ số cột của header, field thừa mất
            // âm thầm. Tên "Cột N" được ghi vào dòng tiêu đề khi lưu (đổi được bằng Rename Column).
            var headerCount = columns.Count;
            var names = columns.Select(c => c.Name).ToHashSet();
            for (var i = headerCount + 1; i <= width; i++)
            {
                var name = $"Cột {i}";
                for (var n = 2; names.Contains(name); n++)
                {
                    name = $"Cột {i} ({n})";
                }
                names.Add(name);
                columns.Add(new CsvColumn(name));
            }
            var longRows = rows.Count(r => r.CellCount > headerCount);
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = $"{longRows:N0} dòng có nhiều field hơn header ({headerCount} cột) → thêm {width - headerCount} cột " +
                          $"\"{columns[headerCount].Name}\"… để không mất dữ liệu; khi lưu, dòng tiêu đề sẽ có thêm tên các cột này.",
            });
        }

        var document = new CsvDocument { HasHeader = hasHeader };
        document.ReplaceAll(columns, rows);
        return document;
    }

    private static async Task<(List<string> Lines, long BytesConsumed)> ReadSampleLinesAsync(
        string filePath, Encoding encoding, int maxLines, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);

        while (lines.Count < maxLines && !reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            lines.Add(line);
        }

        // Approximate: StreamReader buffers ahead of what it has handed back as lines, so
        // stream.Position over-counts slightly. Good enough for a row-count heuristic/warning.
        return (lines, stream.Position);
    }

    private static long EstimateRowCount(int sampleLineCount, long sampleBytes, long fileLength)
    {
        if (sampleLineCount == 0 || sampleBytes == 0)
        {
            return 0;
        }

        var avgBytesPerLine = sampleBytes / (double)sampleLineCount;
        return avgBytesPerLine > 0 ? (long)(fileLength / avgBytesPerLine) : 0;
    }
}
