namespace FileTools.Core;

public sealed record RewriteOptions
{
    public required IReadOnlyList<string> Files { get; init; }
    /// <summary>Thư mục ghi kết quả (giữ tên file). Null = ghi đè file gốc, file cũ đổi thành "&lt;tên&gt;.bak".</summary>
    public string? OutputFolder { get; init; }
    public OutputEncoding Encoding { get; init; } = OutputEncoding.SameAsSource;
    public NewlineMode Newline { get; init; } = NewlineMode.Keep;
    /// <summary>Ép encoding đọc file nguồn (null = tự nhận) - dùng khi tự nhận sai.</summary>
    public System.Text.Encoding? SourceEncoding { get; init; }
    /// <summary>Biến đổi nội dung từng dòng (không gồm ký tự xuống dòng) → dòng mới + số chỗ đã thay. Dùng cho "Thay thế
    /// hàng loạt"; null = giữ nguyên nội dung.</summary>
    public Func<string, (string Text, int Changes)>? Transform { get; init; }
}

public enum RewriteStatus
{
    Written,
    /// <summary>Không có gì thay đổi (cùng encoding, không đổi xuống dòng nào) - không ghi / không tạo .bak.</summary>
    Unchanged,
    Failed,
}

public sealed record RewriteItem(string Source, string? Output, RewriteStatus Status, string From, string To, long EndingsChanged, long LostChars, string? Error, long Replacements = 0);

/// <summary>
/// Ghi lại hàng loạt file với encoding / kiểu xuống dòng / nội dung mới - lõi chung của "Đổi encoding", "Đổi xuống dòng" và
/// "Thay thế hàng loạt".
/// Ghi đè tại chỗ: file mới ghi xong (.partial) mới đổi tên, bản gốc giữ thành .bak. File không có gì thay đổi thì bỏ
/// qua (không tạo .bak thừa). 1 file lỗi không dừng cả lô.
/// </summary>
public static class FileRewriter
{
    public static IReadOnlyList<RewriteItem> Rewrite(RewriteOptions o, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var results = new List<RewriteItem>();
        long total = Math.Max(1, o.Files.Sum(f => File.Exists(f) ? new FileInfo(f).Length : 0));
        long done = 0;
        var throttle = new ProgressThrottle(progress);
        foreach (var source in o.Files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                results.Add(RewriteOne(source, o, p => throttle.Report((done + p) / (double)total, Path.GetFileName(source)), ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new RewriteItem(source, null, RewriteStatus.Failed, "", "", 0, 0, ex.Message));
            }
            done += File.Exists(source) ? new FileInfo(source).Length : 0;
        }
        throttle.Report(1, null, force: true);
        return results;
    }

    private static RewriteItem RewriteOne(string source, RewriteOptions o, Action<long> progress, CancellationToken ct)
    {
        var sourceEncoding = o.SourceEncoding ?? EncodingSniffer.Detect(source).Encoding;
        var target = TextEncodings.Resolve(o.Encoding, sourceEncoding);
        string from = TextEncodings.Describe(sourceEncoding), to = TextEncodings.Describe(target);
        string output = o.OutputFolder is null ? source : Path.Combine(o.OutputFolder, Path.GetFileName(source));
        if (o.OutputFolder is not null && string.Equals(Path.GetFullPath(output), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Thư mục đầu ra trùng thư mục nguồn - chọn thư mục khác hoặc dùng \"ghi đè file gốc\".");
        }

        bool sameEncoding = to == from && target.CodePage == sourceEncoding.CodePage;
        long endingsChanged, lost, replacements = 0;
        // Ghi vào file tạm cạnh đích; chỉ đổi tên khi thật sự có thay đổi.
        var temp = output + ".rewrite";
        using (var reader = LineReader.Open(source, sourceEncoding))
        using (var writer = new OutputFile(temp, target, o.Newline))
        {
            while (reader.TryRead(out var line))
            {
                if ((reader.LinesRead & 0x3FF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    progress(reader.ConsumedBytes);
                }
                if (o.Transform is { } transform)
                {
                    var (text, changes) = transform(line.Text);
                    replacements += changes;
                    writer.WriteLine(changes > 0 ? line with { Text = text } : line);
                    continue;
                }
                writer.WriteLine(line);
            }
            ct.ThrowIfCancellationRequested();
            endingsChanged = writer.EndingsChanged;
            if (sameEncoding && endingsChanged == 0 && replacements == 0 && o.OutputFolder is null)
            {
                return new RewriteItem(source, null, RewriteStatus.Unchanged, from, to, 0, 0, null);
            }
            writer.Commit();
            // Chỉ đủ sau Commit: ký tự được mã hoá khi StreamWriter xả buffer.
            lost = writer.LostChars;
        }

        if (o.OutputFolder is null)
        {
            File.Move(source, source + ".bak", overwrite: true);
        }
        File.Move(temp, output, overwrite: true);
        return new RewriteItem(source, output, RewriteStatus.Written, from, to, endingsChanged, lost, null, replacements);
    }
}
