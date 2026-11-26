namespace DbTools.Services;

/// <summary>1 lần bấm Chạy ở trang Chạy SQL.</summary>
public sealed class QueryRecord
{
    public DateTime Time { get; set; }
    public string Sql { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;

    /// <summary>host:port/database.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Chạy ở chế độ cho phép ghi (COMMIT).</summary>
    public bool Write { get; set; }
    public bool Success { get; set; }

    /// <summary>Số dòng của bảng kết quả (đã đọc), null = không có bảng.</summary>
    public long? Rows { get; set; }
    public long DurationMs { get; set; }
    public string Error { get; set; } = string.Empty;
}

/// <summary>Lịch sử câu của trang Chạy SQL: Data\History\queries.json, mới nhất ở đầu, tối đa <see cref="MaxRecords"/> câu.
/// Chạy lại đúng câu đó trên cùng server thì đưa lên đầu (không nhân bản).</summary>
public sealed class QueryHistoryStore(string? folder = null)
{
    public const int MaxRecords = 200;

    public string FilePath { get; } = Path.Combine(folder ?? DataPaths.History, "queries.json");

    public List<QueryRecord> Load() => JsonFile.Read<List<QueryRecord>>(FilePath);

    public List<QueryRecord> Add(QueryRecord record)
    {
        var list = Load();
        list.RemoveAll(r => r.Target == record.Target && Same(r.Sql, record.Sql));
        list.Insert(0, record);
        var kept = list.Take(MaxRecords).ToList();
        JsonFile.Write(FilePath, kept);
        return kept;
    }

    public void Clear() => JsonFile.Write(FilePath, new List<QueryRecord>());

    /// <summary>Lọc theo chữ trong câu / hồ sơ / server (không phân biệt hoa thường; nhiều từ thì phải có đủ).</summary>
    public static IEnumerable<QueryRecord> Filter(IEnumerable<QueryRecord> records, string? text)
    {
        var words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? records
            : records.Where(r => words.All(w => r.Sql.Contains(w, StringComparison.OrdinalIgnoreCase)
                || r.Profile.Contains(w, StringComparison.OrdinalIgnoreCase)
                || r.Target.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Cùng câu nếu chỉ khác khoảng trắng / xuống dòng ở 2 đầu và kiểu xuống dòng.</summary>
    private static bool Same(string a, string b) => a.ReplaceLineEndings("\n").Trim() == b.ReplaceLineEndings("\n").Trim();
}
