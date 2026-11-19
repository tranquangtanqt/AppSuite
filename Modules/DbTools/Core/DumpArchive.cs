using System.Text;
using System.Text.RegularExpressions;

namespace DbTools.Core;

/// <summary>
/// Thông tin 1 file dump để xem trước khi Restore: file custom / tar đọc từ `pg_restore -l` (phần đầu "; Archive created at...",
/// "; dbname:", "; Dumped from database version:" + mỗi dòng 1 mục "id; oid oid LOẠI schema tên owner"); file SQL thường đọc
/// vài dòng chú thích đầu ("-- Dumped from database version 17.2").
/// </summary>
public sealed partial class DumpArchiveInfo
{
    /// <summary>CUSTOM / TAR / DIRECTORY / SQL thường.</summary>
    public string Format { get; set; } = string.Empty;
    public bool IsPlainSql { get; set; }
    public string DbName { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string ServerVersion { get; set; } = string.Empty;
    public string DumpVersion { get; set; } = string.Empty;
    public int TocEntries { get; set; }

    /// <summary>Số mục theo loại: TABLE, TABLE DATA, VIEW, FUNCTION, SEQUENCE, INDEX, TRIGGER, CONSTRAINT...</summary>
    public SortedDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Schemas { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Owners { get; } = new(StringComparer.Ordinal);

    public int Count(string type) => Counts.GetValueOrDefault(type);

    /// <summary>Loại nhiều chữ - so dài trước ("TABLE DATA" trước "TABLE").</summary>
    private static readonly string[] MultiWordTypes =
    [
        "MATERIALIZED VIEW DATA", "MATERIALIZED VIEW", "TABLE DATA", "SEQUENCE SET", "SEQUENCE OWNED BY", "FK CONSTRAINT",
        "CHECK CONSTRAINT", "DEFAULT ACL", "INDEX ATTACH", "LARGE OBJECT", "EVENT TRIGGER", "FOREIGN TABLE", "FOREIGN DATA WRAPPER",
        "SERVER", "USER MAPPING", "TEXT SEARCH CONFIGURATION", "TEXT SEARCH DICTIONARY", "TEXT SEARCH PARSER", "TEXT SEARCH TEMPLATE",
        "PUBLICATION TABLE", "ROW SECURITY", "PROCEDURAL LANGUAGE", "OPERATOR CLASS", "OPERATOR FAMILY", "BLOB COMMENTS",
    ];

    [GeneratedRegex(@"^\d+;\s+\d+\s+\d+\s+(.+)$")]
    private static partial Regex EntryRegex();

    [GeneratedRegex(@"^;\s+([^:]+):\s*(.*)$")]
    private static partial Regex HeaderRegex();

    public static DumpArchiveInfo ParseList(string text)
    {
        var info = new DumpArchiveInfo();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(';'))
            {
                if (line.StartsWith("; Archive created at ", StringComparison.Ordinal))
                {
                    info.CreatedAt = line["; Archive created at ".Length..].Trim();
                }
                else if (HeaderRegex().Match(line) is { Success: true } h)
                {
                    var value = h.Groups[2].Value.Trim();
                    switch (h.Groups[1].Value.Trim())
                    {
                        case "dbname": info.DbName = value; break;
                        case "TOC Entries": info.TocEntries = int.TryParse(value, out var n) ? n : 0; break;
                        case "Format": info.Format = value; break;
                        case "Dumped from database version": info.ServerVersion = value; break;
                        case "Dumped by pg_dump version": info.DumpVersion = value; break;
                    }
                }
                continue;
            }
            if (EntryRegex().Match(line) is not { Success: true } m)
            {
                continue;
            }
            var rest = m.Groups[1].Value;
            var type = MultiWordTypes.FirstOrDefault(t => rest.StartsWith(t + " ", StringComparison.Ordinal))
                ?? rest.Split(' ', 2)[0];
            info.Counts[type] = info.Count(type) + 1;
            var parts = rest[type.Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                if (parts[0] != "-" && type is not ("COMMENT" or "ACL"))
                {
                    info.Schemas.Add(parts[0]);
                }
                if (type is not ("COMMENT" or "ACL" or "ENCODING" or "STDSTRINGS" or "SEARCHPATH"))
                {
                    info.Owners.Add(parts[^1]);
                }
            }
        }
        return info;
    }

    [GeneratedRegex(@"^--\s*Dumped (from database|by pg_dump) version\s+(.+)$")]
    private static partial Regex PlainVersionRegex();

    /// <summary>Phần đầu file SQL thường (vài KB đầu) - chỉ có bản PostgreSQL; đếm sơ CREATE TABLE / FUNCTION trong phần đã đọc.</summary>
    public static DumpArchiveInfo ParsePlainHeader(string head)
    {
        var info = new DumpArchiveInfo { Format = "SQL thường", IsPlainSql = true };
        foreach (var raw in head.Split('\n').Take(60))
        {
            if (PlainVersionRegex().Match(raw.TrimEnd('\r')) is { Success: true } m)
            {
                if (m.Groups[1].Value.StartsWith("from", StringComparison.Ordinal))
                {
                    info.ServerVersion = m.Groups[2].Value.Trim();
                }
                else
                {
                    info.DumpVersion = m.Groups[2].Value.Trim();
                }
            }
        }
        return info;
    }

    /// <summary>File custom của pg_dump bắt đầu bằng "PGDMP"; tar có "ustar" ở byte 257.</summary>
    public static bool IsArchive(ReadOnlySpan<byte> head) =>
        head.StartsWith("PGDMP"u8) || (head.Length >= 262 && head[257..262].SequenceEqual("ustar"u8));

    /// <summary>Tóm tắt nhiều dòng để hiện dưới ô chọn file.</summary>
    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append(IsPlainSql ? "File SQL thường (pg_dump -Fp) - chạy bằng psql" : $"File dump {Format.ToLowerInvariant()} (pg_restore)");
        if (DbName.Length > 0)
        {
            sb.Append($" · DB gốc: {DbName}");
        }
        if (CreatedAt.Length > 0)
        {
            sb.Append($" · tạo lúc {CreatedAt}");
        }
        if (ServerVersion.Length > 0)
        {
            sb.Append($" · từ PostgreSQL {ServerVersion}");
        }
        if (!IsPlainSql)
        {
            var parts = new List<string>();
            void Add(string label, params string[] types)
            {
                var n = types.Sum(Count);
                if (n > 0)
                {
                    parts.Add($"{n} {label}");
                }
            }
            Add("bảng", "TABLE");
            Add("bảng có dữ liệu", "TABLE DATA");
            Add("view", "VIEW", "MATERIALIZED VIEW");
            Add("hàm", "FUNCTION", "PROCEDURE");
            Add("sequence", "SEQUENCE");
            Add("index", "INDEX");
            Add("ràng buộc", "CONSTRAINT", "FK CONSTRAINT", "CHECK CONSTRAINT");
            Add("trigger", "TRIGGER");
            sb.AppendLine();
            sb.Append(parts.Count > 0 ? string.Join(", ", parts) : "Không có đối tượng nào");
            if (Schemas.Count > 0)
            {
                sb.Append($" · schema: {string.Join(", ", Schemas.Take(8))}{(Schemas.Count > 8 ? "..." : string.Empty)}");
            }
            if (Owners.Count > 0)
            {
                sb.Append($" · owner: {string.Join(", ", Owners.Take(5))}{(Owners.Count > 5 ? "..." : string.Empty)}");
            }
        }
        return sb.ToString();
    }
}
