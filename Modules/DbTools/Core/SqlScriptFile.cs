using System.Text;

namespace DbTools.Core;

public enum IssueLevel
{
    Info,
    Warning,
    /// <summary>Không chạy được - phải sửa file / bỏ file khỏi danh sách.</summary>
    Error,
}

/// <summary>1 điều cần báo về file. <paramref name="Line"/> = 0 khi không gắn với dòng nào.</summary>
public sealed record ScriptIssue(IssueLevel Level, int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"Dòng {Line}: {Message}" : Message;
}

/// <summary>1 file .sql đã đọc: nội dung, các câu (đã nhận loại), lệnh psql, các điều cần báo.</summary>
public sealed class SqlScriptFile
{
    public required string FullPath { get; init; }

    /// <summary>Đường dẫn so với thư mục gốc (vd OBJECT\MSTA_ARAP_DATA.sql) - dùng làm khoá lưu thứ tự.</summary>
    public required string RelativePath { get; init; }

    public required string EncodingName { get; init; }
    public required IReadOnlyList<StatementInfo> Statements { get; init; }
    public required IReadOnlyList<PsqlMetaCommand> MetaCommands { get; init; }
    public required IReadOnlyList<ScriptIssue> Issues { get; init; }

    public bool HasErrors => Issues.Any(i => i.Level == IssueLevel.Error);

    /// <summary>Đối tượng file này tạo ra (tên không schema).</summary>
    public IReadOnlySet<string> CreatedNames => _created ??= Statements.Where(s => s.CreatesObject && s.Object is not null)
        .Select(s => s.Object!.Key).ToHashSet(StringComparer.Ordinal);

    private IReadOnlySet<string>? _created;

    /// <summary>Loại chính của file (để sắp thứ tự): theo câu đầu tiên có ý nghĩa (bỏ DROP / SET / GRANT đứng trước).</summary>
    public int Rank => RunOrder.RankOf(Statements);

    /// <summary>Tóm tắt loại lệnh trong file, vd "Hàm, Trigger".</summary>
    public string KindSummary
    {
        get
        {
            var labels = Statements.Select(s => KindLabel(s.Kind)).Where(l => l.Length > 0).Distinct().ToList();
            return labels.Count == 0 ? "(trống)" : labels.Count <= 3 ? string.Join(", ", labels) : string.Join(", ", labels.Take(3)) + "…";
        }
    }

    /// <summary>Tóm tắt câu xoá / sửa dữ liệu, vd "DELETE ×2, DROP".</summary>
    public string DangerSummary => string.Join(", ", Statements.Where(s => s.ChangesData)
        .GroupBy(s => s.Verb.Split(' ')[0])
        .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ×{g.Count()}"));

    public static string KindLabel(StatementKind kind) => kind switch
    {
        StatementKind.CreateSchema => "Schema",
        StatementKind.CreateType => "Kiểu",
        StatementKind.CreateDomain => "Domain",
        StatementKind.CreateSequence => "Sequence",
        StatementKind.CreateTable => "Bảng",
        StatementKind.CreateView => "View",
        StatementKind.CreateFunction => "Hàm",
        StatementKind.CreateProcedure => "Procedure",
        StatementKind.CreateTrigger => "Trigger",
        StatementKind.CreateIndex => "Index",
        StatementKind.AddConstraint => "Ràng buộc",
        StatementKind.AlterOther => "Sửa đối tượng",
        StatementKind.Comment => "Comment",
        StatementKind.Insert => "Thêm dữ liệu",
        StatementKind.Update => "Sửa dữ liệu",
        StatementKind.Delete => "Xoá dữ liệu",
        StatementKind.Truncate => "Xoá dữ liệu",
        StatementKind.Drop => "Xoá đối tượng",
        _ => "Khác",
    };

    /// <summary>Đọc file. Encoding: có BOM → theo BOM; đọc được UTF-8 → UTF-8; còn lại theo \encoding (SJIS...) hoặc
    /// Shift-JIS (kèm cảnh báo, vì PostgreSQL nhận theo client_encoding UTF-8).</summary>
    public static SqlScriptFile Load(string fullPath, string rootFolder)
    {
        var bytes = File.ReadAllBytes(fullPath);
        var relative = Path.GetRelativePath(rootFolder, fullPath);
        return Parse(bytes, fullPath, relative);
    }

    public static SqlScriptFile Parse(byte[] bytes, string fullPath, string relativePath)
    {
        var issues = new List<ScriptIssue>();
        var (text, encodingName, encodingIssue) = Decode(bytes);
        if (encodingIssue is not null)
        {
            issues.Add(new ScriptIssue(IssueLevel.Warning, 0, encodingIssue));
        }

        var split = SqlSplitter.Split(text);
        foreach (var meta in split.MetaCommands)
        {
            if (meta.Name == "encoding")
            {
                continue; // đã dùng khi đọc file
            }
            issues.Add(new ScriptIssue(IssueLevel.Error, meta.Line,
                $"Lệnh psql \"{meta.Text}\" không chạy được trong DbTools (chỉ hỗ trợ \\encoding) - chạy file này bằng psql hoặc bỏ dòng này."));
        }

        var statements = split.Statements.Select(SqlAnalyzer.Analyze).ToList();
        if (statements.Count == 0)
        {
            issues.Add(new ScriptIssue(IssueLevel.Warning, 0, "File không có câu SQL nào."));
        }
        foreach (var s in statements)
        {
            switch (s.Kind)
            {
                case StatementKind.TransactionControl:
                    issues.Add(new ScriptIssue(IssueLevel.Error, s.Statement.StartLine,
                        $"{s.Verb}: file tự mở / đóng transaction - DbTools chạy cả lô trong 1 transaction nên không chạy được. Bỏ câu này khỏi file."));
                    break;
                case StatementKind.NotInTransaction:
                    issues.Add(new ScriptIssue(IssueLevel.Error, s.Statement.StartLine,
                        $"{s.Verb}: PostgreSQL không cho chạy câu này trong transaction - chạy riêng bằng psql."));
                    break;
                case var _ when s.ChangesData:
                    issues.Add(new ScriptIssue(IssueLevel.Warning, s.Statement.StartLine,
                        $"{s.Verb}{(s.Table is null ? string.Empty : " " + s.Table)}: xoá / sửa dữ liệu hoặc đối tượng."));
                    break;
            }
        }

        return new SqlScriptFile
        {
            FullPath = fullPath,
            RelativePath = relativePath,
            EncodingName = encodingName,
            Statements = statements,
            MetaCommands = split.MetaCommands,
            Issues = issues,
        };
    }

    internal static (string Text, string EncodingName, string? Issue) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8 (BOM)", null);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16", null);
        }
        try
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return (strict.GetString(bytes), "UTF-8", null);
        }
        catch (DecoderFallbackException)
        {
            // Không phải UTF-8.
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding(932);
        var text = sjis.GetString(bytes);
        var declared = SqlSplitter.Split(text).MetaCommands.FirstOrDefault(m => m.Name == "encoding")?.Argument.ToUpperInvariant();
        var isSjis = declared is "SJIS" or "SHIFT_JIS" or "SHIFT-JIS" or "SJIS-WIN" or "WIN932" or "CP932";
        return (text, "Shift-JIS", isSjis
            ? null
            : "File không phải UTF-8 - đọc theo Shift-JIS. Mở file kiểm tra chữ Nhật / Việt (nên lưu lại bằng UTF-8).");
    }
}
