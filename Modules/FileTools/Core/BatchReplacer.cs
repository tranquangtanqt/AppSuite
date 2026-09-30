using System.Text.RegularExpressions;

namespace FileTools.Core;

public sealed record ReplaceOptions
{
    public required string Find { get; init; }
    public string Replacement { get; init; } = string.Empty;
    /// <summary>Regex: <see cref="Replacement"/> dùng được $1, ${ten}...</summary>
    public bool IsRegex { get; init; }
    public bool MatchCase { get; init; }
    /// <summary>Chỉ khớp nguyên từ (không nằm giữa chữ / số khác).</summary>
    public bool WholeWord { get; init; }
}

/// <param name="Samples">Vài dòng đầu có chỗ thay: (số dòng, dòng trước, dòng sau).</param>
public sealed record ReplacePreview(string Path, long Matches, long Lines, IReadOnlyList<(long Line, string Before, string After)> Samples, string? Error);

/// <summary>
/// Tìm và thay thế hàng loạt trên nhiều file: bước <see cref="Preview"/> chỉ đếm (không ghi gì), bước ghi dùng
/// <see cref="FileRewriter"/> (giữ encoding + xuống dòng từng file, ghi đè thì giữ .bak, file không có chỗ thay thì không
/// đụng tới). Thay theo từng dòng - mẫu không khớp vắt qua xuống dòng.
/// </summary>
public static class BatchReplacer
{
    private const int MaxSamples = 5;

    public static Regex BuildRegex(ReplaceOptions o)
    {
        if (string.IsNullOrEmpty(o.Find))
        {
            throw new ArgumentException("Chưa nhập chữ cần tìm.");
        }
        string pattern = o.IsRegex ? o.Find : Regex.Escape(o.Find);
        if (o.WholeWord)
        {
            pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        }
        var flags = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (!o.MatchCase)
        {
            flags |= RegexOptions.IgnoreCase;
        }
        try
        {
            return new Regex(pattern, flags, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException("Regex không hợp lệ: " + ex.Message, ex);
        }
    }

    /// <summary>Hàm thay cho <see cref="RewriteOptions.Transform"/>. Chữ thường: chuỗi thay được lấy nguyên văn ("$" không
    /// có nghĩa đặc biệt).</summary>
    public static Func<string, (string Text, int Changes)> BuildTransform(ReplaceOptions o)
    {
        var regex = BuildRegex(o);
        string replacement = o.IsRegex ? o.Replacement : o.Replacement.Replace("$", "$$");
        return line =>
        {
            int count = 0;
            var result = regex.Replace(line, m =>
            {
                count++;
                return m.Result(replacement);
            });
            return (result, count);
        };
    }

    public static IReadOnlyList<ReplacePreview> Preview(IReadOnlyList<string> files, ReplaceOptions o, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var transform = BuildTransform(o);
        var results = new List<ReplacePreview>();
        var throttle = new ProgressThrottle(progress);
        for (int f = 0; f < files.Count; f++)
        {
            ct.ThrowIfCancellationRequested();
            throttle.Report(f / (double)Math.Max(1, files.Count), Path.GetFileName(files[f]));
            try
            {
                long matches = 0, lines = 0;
                var samples = new List<(long, string, string)>();
                using var reader = LineReader.Open(files[f]);
                while (reader.TryRead(out var line))
                {
                    if ((reader.LinesRead & 0x3FF) == 0)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                    var (after, changes) = transform(line.Text);
                    if (changes == 0)
                    {
                        continue;
                    }
                    matches += changes;
                    lines++;
                    if (samples.Count < MaxSamples)
                    {
                        samples.Add((reader.LinesRead, line.Text, after));
                    }
                }
                results.Add(new ReplacePreview(files[f], matches, lines, samples, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new ReplacePreview(files[f], 0, 0, [], ex.Message));
            }
        }
        throttle.Report(1, null, force: true);
        return results;
    }

    public static IReadOnlyList<RewriteItem> Apply(IReadOnlyList<string> files, ReplaceOptions o, string? outputFolder, IProgress<JobProgress>? progress = null, CancellationToken ct = default) =>
        FileRewriter.Rewrite(new RewriteOptions
        {
            Files = files,
            OutputFolder = outputFolder,
            Encoding = OutputEncoding.SameAsSource,
            Newline = NewlineMode.Keep,
            Transform = BuildTransform(o),
        }, progress, ct);
}
