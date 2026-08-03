using ModuleF.Models;

namespace ModuleF.Services;

/// <summary>
/// Picks the delimiter whose per-line field count is both large (actually splits something) and
/// stable across the sampled lines. See PLAN.md for the score formula.
/// </summary>
public sealed class DelimiterDetector : IDelimiterDetector
{
    private static readonly char[] Candidates = [',', '\t', ';', '|'];

    public DelimiterDetectionResult Detect(IReadOnlyList<string> sampleLines, string filePath)
    {
        var nonEmptyLines = sampleLines.Where(l => l.Length > 0).ToList();
        if (nonEmptyLines.Count == 0)
        {
            return new DelimiterDetectionResult(',', DetectionConfidence.Low, "File rỗng hoặc không đọc được dòng mẫu nào, dùng dấu phẩy mặc định.");
        }

        var isTsvExtension = Path.GetExtension(filePath).Equals(".tsv", StringComparison.OrdinalIgnoreCase);
        if (isTsvExtension)
        {
            var tabScore = Score(nonEmptyLines, '\t');
            if (tabScore > 0)
            {
                return new DelimiterDetectionResult('\t', DetectionConfidence.High, null);
            }
        }

        var scored = Candidates.Select(c => (Delimiter: c, Score: Score(nonEmptyLines, c))).OrderByDescending(x => x.Score).ToList();
        var best = scored[0];

        if (best.Score <= 0)
        {
            return new DelimiterDetectionResult(',', DetectionConfidence.Low, "Không tự nhận diện được delimiter, dùng dấu phẩy mặc định - kiểm tra lại nội dung file.");
        }

        var runnerUp = scored.Count > 1 ? scored[1].Score : 0;
        var confidence = runnerUp > 0 && best.Score - runnerUp < best.Score * 0.1
            ? DetectionConfidence.Medium
            : DetectionConfidence.High;

        var warning = confidence == DetectionConfidence.Medium
            ? $"Nhiều delimiter cho kết quả gần bằng nhau, đã chọn '{Describe(best.Delimiter)}' - kiểm tra lại nếu bảng hiển thị sai cột."
            : null;

        return new DelimiterDetectionResult(best.Delimiter, confidence, warning);
    }

    private static double Score(List<string> lines, char delimiter)
    {
        var counts = lines.Select(l => CsvParser.CountFields(l, delimiter)).ToList();
        var mode = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        if (mode <= 1)
        {
            return 0;
        }

        var stableLines = counts.Count(c => c == mode);
        var variance = 1.0 - stableLines / (double)counts.Count;
        return (mode - 1) * (1.0 - variance);
    }

    private static string Describe(char delimiter) => delimiter switch
    {
        ',' => "dấu phẩy (,)",
        '\t' => "Tab",
        ';' => "dấu chấm phẩy (;)",
        '|' => "dấu gạch đứng (|)",
        _ => delimiter.ToString(),
    };
}
