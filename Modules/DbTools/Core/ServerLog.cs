using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DbTools.Core;

/// <summary>Loại 1 dòng nhật ký server (ô lọc trên trang Nhật ký server).</summary>
public enum LogKind
{
    Select,
    Insert,
    Update,
    Delete,
    Ddl,

    /// <summary>Câu SQL khác: BEGIN / COMMIT / SET / SHOW / CALL / DO / COPY / EXPLAIN...</summary>
    OtherSql,

    /// <summary>ERROR / FATAL / PANIC / WARNING.</summary>
    Error,

    /// <summary>Kết nối / ngắt kết nối (log_connections, log_disconnections).</summary>
    Connection,

    /// <summary>Thông báo của server: khởi động, tắt, checkpoint...</summary>
    Server,
}

/// <summary>1 mục nhật ký (1 dòng log + các dòng nối tiếp, DETAIL / STATEMENT / duration đi kèm).</summary>
public sealed class LogEntry
{
    public string Time { get; set; } = string.Empty;
    public int? Pid { get; set; }
    public string User { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string App { get; set; } = string.Empty;
    public string SqlState { get; set; } = string.Empty;

    /// <summary>LOG / ERROR / FATAL / WARNING...</summary>
    public string Level { get; set; } = string.Empty;

    public LogKind Kind { get; set; } = LogKind.Server;

    /// <summary>Nội dung sau "LEVEL:  " (cả dòng nối tiếp).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Câu SQL ("statement: ...", "execute ...: ...", hoặc STATEMENT đi kèm lỗi); rỗng nếu không có.</summary>
    public string Sql { get; set; } = string.Empty;

    public double? DurationMs { get; set; }

    /// <summary>Các dòng DETAIL / HINT / CONTEXT / QUERY / LOCATION đi kèm (vd tham số $1 của câu do ứng dụng gửi).</summary>
    public string Detail { get; set; } = string.Empty;

    public List<string> RawLines { get; } = [];

    /// <summary>Câu SQL, không có thì thông báo.</summary>
    public string Text => Sql.Length > 0 ? Sql : Message;
}

/// <summary>Kết quả đọc 1 đoạn log: các mục + số dòng không nhận ra định dạng (khác log_line_prefix / lc_messages).</summary>
public sealed record LogParseResult(List<LogEntry> Entries, int UnknownLines);

/// <summary>
/// Biến <c>log_line_prefix</c> của server (vd "%m [%p] %q%u@%d ") thành regex để tách giờ / pid / user / database / máy /
/// ứng dụng / mã lỗi ở đầu mỗi dòng log. Giá trị tự do (user, app...) khớp ngắn nhất tới phần chữ cố định phía sau.
/// %q: tiến trình nền của server không in phần sau %q → phần đó là tuỳ chọn.
/// </summary>
public sealed class LogLinePrefix
{
    private const string Levels = "LOG|ERROR|FATAL|PANIC|WARNING|NOTICE|INFO|DEBUG[1-5]?|DETAIL|HINT|CONTEXT|STATEMENT|QUERY|LOCATION";
    private const string Stamp = @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}";

    private readonly Regex _regex;
    private readonly Dictionary<string, char> _groups = [];

    public string Prefix { get; }

    public LogLinePrefix(string prefix)
    {
        Prefix = prefix;
        var head = new StringBuilder("^");
        var optionalFrom = -1;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (prefix[i] != '%' || i + 1 >= prefix.Length)
            {
                head.Append(Regex.Escape(prefix[i].ToString()));
                continue;
            }
            var j = i + 1;
            while (j < prefix.Length && (prefix[j] == '-' || char.IsDigit(prefix[j])))
            {
                j++; // %-10u: canh lề bằng khoảng trắng
            }
            if (j >= prefix.Length)
            {
                break;
            }
            var padded = j > i + 1;
            var escape = prefix[j];
            i = j;
            if (escape == '%')
            {
                head.Append('%');
                continue;
            }
            if (escape == 'q')
            {
                optionalFrom = head.Length;
                continue;
            }
            var name = "g" + _groups.Count;
            _groups[name] = escape;
            var pattern = escape switch
            {
                'p' => @"\d+",
                'P' or 'l' or 'x' => @"\d*",
                'e' => "[0-9A-Z]{5}",
                'c' => @"[0-9a-f]+\.[0-9a-f]+",
                'n' => @"\d+(?:\.\d+)?",
                'Q' => @"-?\d+",
                't' or 's' => Stamp + @"(?: \S+)?",
                'm' => Stamp + @"\.\d{3}(?: \S+)?",
                _ => ".*?",
            };
            head.Append(padded ? $@"\s*(?<{name}>{pattern})\s*" : $"(?<{name}>{pattern})");
        }
        var text = head.ToString();
        if (optionalFrom >= 0)
        {
            text = text[..optionalFrom] + "(?:" + text[optionalFrom..] + ")?";
        }
        _regex = new Regex(text + $"(?<level>{Levels}):  (?<msg>.*)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    /// <summary>Có trong prefix không (vd 'u' = user) - thiếu thì cột đó trống.</summary>
    public bool Has(char escape) => _groups.ContainsValue(escape);

    /// <summary>Tách 1 dòng; không phải dòng đầu của 1 mục (dòng nối tiếp, định dạng khác) → null.</summary>
    public LogEntry? Match(string line)
    {
        var m = _regex.Match(line);
        if (!m.Success)
        {
            return null;
        }
        var e = new LogEntry { Level = m.Groups["level"].Value, Message = m.Groups["msg"].Value };
        foreach (var (name, escape) in _groups)
        {
            var g = m.Groups[name];
            if (!g.Success)
            {
                continue;
            }
            var v = g.Value.Trim();
            switch (escape)
            {
                case 't' or 'm' when e.Time.Length == 0:
                    e.Time = v;
                    break;
                case 'p' when e.Pid is null && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var pid):
                    e.Pid = pid;
                    break;
                case 'u':
                    e.User = v;
                    break;
                case 'd':
                    e.Database = v;
                    break;
                case 'r' or 'h' when e.Client.Length == 0:
                    e.Client = v;
                    break;
                case 'a':
                    e.App = v;
                    break;
                case 'e':
                    e.SqlState = v;
                    break;
            }
        }
        return e;
    }
}

/// <summary>
/// Đọc nội dung file log dạng stderr của PostgreSQL thành các mục: ghép dòng nối tiếp (bắt đầu bằng Tab - câu SQL nhiều dòng),
/// gắn DETAIL / HINT / STATEMENT... vào mục trước cùng pid, gắn dòng "duration: N ms" (log_duration) vào câu trước cùng pid;
/// phân loại SELECT / INSERT / UPDATE / DELETE / DDL / lỗi / kết nối. Mật khẩu trong câu (PASSWORD '...') luôn bị che.
/// </summary>
public static class ServerLogParser
{
    /// <summary>application_name DbTools dùng khi kết nối (Pg.ConnectionString) - mặc định ẩn các câu của chính DbTools.</summary>
    public const string OwnApp = "AppSuite DbTools";

    private static readonly Regex StatementRegex = new(
        @"^(?:duration: (?<dur>[\d.]+) ms\s+)?(?<what>statement|execute [^:]*|parse [^:]*|bind [^:]*): (?<sql>.*)$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DurationOnly = new(@"^duration: (?<dur>[\d.]+) ms$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PasswordRegex = new(@"(?i)(\bPASSWORD\s+)'(?:[^']|'')*'", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Comments = new(@"^\s*(?:--[^\n]*\n|/\*.*?\*/|\(|\s)*", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DataVerb = new(@"\b(INSERT|UPDATE|DELETE|MERGE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private enum Part { Message, Sql, Detail }

    /// <param name="text">Nội dung (hoặc đoạn cuối) file log.</param>
    /// <param name="partialStart">Đoạn đọc bắt đầu giữa file: bỏ dòng đầu (có thể bị cắt) và các dòng nối tiếp mồ côi.</param>
    public static LogParseResult Parse(string text, LogLinePrefix prefix, bool partialStart = false)
    {
        var entries = new List<LogEntry>();
        var unknown = 0;
        LogEntry? current = null;
        var part = Part.Message;
        var lines = text.Split('\n');
        var first = partialStart ? 1 : 0;
        var last = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length; // dòng rỗng sau \n cuối file
        for (var i = first; i < last; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var e = prefix.Match(line);
            if (e is null)
            {
                if (current is null)
                {
                    if (!partialStart && line.Length > 0)
                    {
                        unknown++;
                        entries.Add(current = new LogEntry { Message = line, Kind = LogKind.Server });
                        current.RawLines.Add(line);
                        part = Part.Message;
                    }
                    continue;
                }
                if (!line.StartsWith('\t') && line.Length > 0)
                {
                    unknown++;
                }
                current.RawLines.Add(line);
                var more = "\n" + (line.StartsWith('\t') ? line[1..] : line);
                switch (part)
                {
                    case Part.Sql:
                        current.Sql += more;
                        break;
                    case Part.Detail:
                        current.Detail += more;
                        break;
                    default:
                        current.Message += more;
                        break;
                }
                continue;
            }

            if (current is not null && e.Pid == current.Pid && e.Level is "DETAIL" or "HINT" or "CONTEXT" or "STATEMENT" or "QUERY" or "LOCATION")
            {
                current.RawLines.Add(line);
                if (e.Level == "STATEMENT" && current.Sql.Length == 0)
                {
                    current.Sql = e.Message;
                    part = Part.Sql;
                }
                else
                {
                    current.Detail += (current.Detail.Length > 0 ? "\n" : string.Empty) + e.Level + ": " + e.Message;
                    part = Part.Detail;
                }
                continue;
            }

            if (DurationOnly.Match(e.Message) is { Success: true } d && FindStatement(entries, e.Pid) is { } owner)
            {
                owner.DurationMs = ParseMs(d.Groups["dur"].Value);
                owner.RawLines.Add(line);
                current = owner;
                part = Part.Detail;
                continue;
            }

            current = e;
            current.RawLines.Add(line);
            part = Part.Message;
            if (e.Level == "LOG" && StatementRegex.Match(e.Message) is { Success: true } s)
            {
                current.Sql = s.Groups["sql"].Value;
                current.Message = s.Groups["what"].Value;
                if (s.Groups["dur"].Success)
                {
                    current.DurationMs = ParseMs(s.Groups["dur"].Value);
                }
                part = Part.Sql;
            }
            entries.Add(current);
        }

        foreach (var entry in entries)
        {
            Finish(entry);
        }
        return new LogParseResult(entries, unknown);
    }

    /// <summary>Câu gần nhất cùng pid chưa có thời gian chạy (dòng duration đứng ngay sau câu, có thể xen dòng của pid khác).</summary>
    private static LogEntry? FindStatement(List<LogEntry> entries, int? pid)
    {
        for (var i = entries.Count - 1; i >= Math.Max(0, entries.Count - 200); i--)
        {
            var e = entries[i];
            if (e.Pid == pid)
            {
                return e.Sql.Length > 0 && e.DurationMs is null && e.Level == "LOG" ? e : null;
            }
        }
        return null;
    }

    private static double? ParseMs(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) ? ms : null;

    private static void Finish(LogEntry e)
    {
        e.Sql = MaskPasswords(e.Sql);
        e.Message = MaskPasswords(e.Message);
        e.Detail = MaskPasswords(e.Detail);
        for (var i = 0; i < e.RawLines.Count; i++)
        {
            e.RawLines[i] = MaskPasswords(e.RawLines[i]);
        }
        e.Kind = e.Level switch
        {
            "ERROR" or "FATAL" or "PANIC" or "WARNING" => LogKind.Error,
            "LOG" when e.Sql.Length > 0 => KindOf(e.Sql),
            _ when e.Message.StartsWith("connection ", StringComparison.Ordinal)
                   || e.Message.StartsWith("disconnection:", StringComparison.Ordinal) => LogKind.Connection,
            _ => e.Kind,
        };
    }

    /// <summary>Che mật khẩu: ALTER ROLE x PASSWORD 'bí mật' → PASSWORD '***'.</summary>
    public static string MaskPasswords(string text) =>
        text.Contains("password", StringComparison.OrdinalIgnoreCase) ? PasswordRegex.Replace(text, "$1'***'") : text;

    /// <summary>Loại câu theo từ khoá đầu (bỏ chú thích, dấu mở ngoặc). WITH ... → theo INSERT / UPDATE / DELETE bên trong,
    /// không có thì SELECT. TRUNCATE tính là DELETE (xoá dữ liệu), MERGE là UPDATE.</summary>
    public static LogKind KindOf(string sql)
    {
        var rest = sql[Comments.Match(sql).Length..];
        var end = 0;
        while (end < rest.Length && char.IsLetter(rest[end]))
        {
            end++;
        }
        return rest[..end].ToUpperInvariant() switch
        {
            "SELECT" or "VALUES" or "TABLE" => LogKind.Select,
            "INSERT" => LogKind.Insert,
            "UPDATE" or "MERGE" => LogKind.Update,
            "DELETE" or "TRUNCATE" => LogKind.Delete,
            "CREATE" or "ALTER" or "DROP" or "COMMENT" or "GRANT" or "REVOKE" => LogKind.Ddl,
            "WITH" => DataVerb.Match(rest) is { Success: true } m
                ? m.Value.ToUpperInvariant() switch
                {
                    "INSERT" => LogKind.Insert,
                    "DELETE" => LogKind.Delete,
                    _ => LogKind.Update,
                }
                : LogKind.Select,
            _ => LogKind.OtherSql,
        };
    }

    public static string KindText(LogKind kind) => kind switch
    {
        LogKind.Select => "SELECT",
        LogKind.Insert => "INSERT",
        LogKind.Update => "UPDATE",
        LogKind.Delete => "DELETE",
        LogKind.Ddl => "DDL",
        LogKind.OtherSql => "SQL khác",
        LogKind.Error => "Lỗi",
        LogKind.Connection => "Kết nối",
        _ => "Server",
    };
}

/// <summary>Bộ lọc trang Nhật ký server: loại, chữ trong SQL / thông báo, user, database, máy / IP / ứng dụng (đều chứa,
/// không phân biệt hoa thường), ẩn câu của chính DbTools.</summary>
public sealed record LogFilter(IReadOnlySet<LogKind> Kinds, string Text = "", string User = "", string Database = "", string Client = "",
    bool HideOwn = true)
{
    public bool Matches(LogEntry e) =>
        Kinds.Contains(e.Kind)
        && !(HideOwn && e.App == ServerLogParser.OwnApp)
        && Has(e.User, User)
        && Has(e.Database, Database)
        && (Has(e.Client, Client) || Has(e.App, Client))
        && (Has(e.Sql, Text) || Has(e.Message, Text) || Has(e.Detail, Text));

    private static bool Has(string value, string part) =>
        part.Trim().Length == 0 || value.Contains(part.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Xuất các mục đang lọc: CSV (mở bằng CsvEditor / Excel) hoặc TXT (đúng các dòng log gốc).</summary>
public static class ServerLogExport
{
    public static readonly string[] Header =
        ["Giờ", "PID", "User", "Database", "Máy", "Ứng dụng", "Loại", "Mức", "Mã lỗi", "Thời gian (ms)", "Câu SQL / thông báo", "Chi tiết"];

    public static string ToCsv(IEnumerable<LogEntry> entries)
    {
        var w = new StringWriter();
        MockOutput.WriteCsv(w, Header, entries.Select(e => new string?[]
        {
            e.Time, e.Pid?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, e.User, e.Database, e.Client, e.App,
            ServerLogParser.KindText(e.Kind), e.Level, e.SqlState,
            e.DurationMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty, e.Text, e.Detail,
        }), new CsvOptions());
        return w.ToString();
    }

    public static string ToText(IEnumerable<LogEntry> entries) =>
        string.Concat(entries.SelectMany(e => e.RawLines).Select(l => l + "\r\n"));
}

/// <summary>Cấu hình log của server + quyền của user (đọc bằng SQL).</summary>
public sealed record LogServerInfo(IReadOnlyDictionary<string, string> Settings, bool Superuser, bool Monitor, bool ReadFiles)
{
    public string Get(string name) => Settings.TryGetValue(name, out var v) ? v : string.Empty;
}

/// <summary>1 dòng ở thẻ Cấu hình log (cảnh báo tô màu).</summary>
public sealed record LogConfigLine(string Text, bool Warning);

/// <summary>Giải thích cấu hình log của server bằng lời: ghi những gì, giữ bao lâu, thiếu gì, có đủ quyền đọc không.</summary>
public static class LogConfig
{
    /// <summary>Thông số log đọc từ pg_settings.</summary>
    public static readonly string[] Names =
    [
        "logging_collector", "log_destination", "log_directory", "log_filename", "log_statement", "log_min_duration_statement",
        "log_duration", "log_line_prefix", "log_connections", "log_disconnections", "log_rotation_age", "log_rotation_size",
        "log_truncate_on_rotation", "lc_messages", "data_directory",
    ];

    public static List<LogConfigLine> Describe(LogServerInfo s, int fileCount, long totalBytes)
    {
        var lines = new List<LogConfigLine>();
        if (s.Get("logging_collector") != "on")
        {
            lines.Add(new("Không ghi ra file (logging_collector = off): log chỉ ở stderr / Event Log của máy server - DbTools không đọc được qua SQL.", true));
        }
        else
        {
            lines.Add(new($"Ghi file vào {s.Get("log_directory")} (trên máy server), tên {s.Get("log_filename")}.", false));
        }

        var statement = s.Get("log_statement");
        var minDuration = int.TryParse(s.Get("log_min_duration_statement"), out var md) ? md : -1;
        var what = statement switch
        {
            "all" => "mọi câu lệnh (cả SELECT)",
            "mod" => "câu DDL + INSERT / UPDATE / DELETE / TRUNCATE (không có SELECT)",
            "ddl" => "chỉ câu DDL (CREATE / ALTER / DROP...)",
            _ => null,
        };
        var slow = minDuration switch
        {
            0 => "mọi câu kèm thời gian chạy",
            > 0 => $"câu chạy lâu hơn {minDuration:N0} ms",
            _ => null,
        };
        if (what is null && slow is null)
        {
            lines.Add(new("Không ghi câu lệnh (log_statement = none, log_min_duration_statement = -1): chỉ có lỗi, khởi động / tắt server. "
                + "Muốn có lịch sử câu lệnh phải nhờ DBA bật log_statement.", true));
        }
        else
        {
            lines.Add(new("Ghi " + string.Join("; thêm ", new[] { what, slow }.Where(x => x is not null)) + ".", false));
        }

        var prefix = new LogLinePrefix(s.Get("log_line_prefix"));
        var missing = new List<string>();
        if (!prefix.Has('t') && !prefix.Has('m'))
        {
            missing.Add("giờ (%t / %m)");
        }
        if (!prefix.Has('u'))
        {
            missing.Add("user (%u)");
        }
        if (!prefix.Has('d'))
        {
            missing.Add("database (%d)");
        }
        if (!prefix.Has('r') && !prefix.Has('h'))
        {
            missing.Add("máy / IP (%r / %h)");
        }
        if (!prefix.Has('p'))
        {
            missing.Add("pid (%p - ghép thời gian chạy, chi tiết lỗi với câu)");
        }
        lines.Add(missing.Count == 0
            ? new($"Đầu dòng log (log_line_prefix = '{s.Get("log_line_prefix")}') có đủ giờ, user, database, máy, ứng dụng.", false)
            : new($"log_line_prefix = '{s.Get("log_line_prefix")}' thiếu {string.Join(", ", missing)} - cột đó sẽ trống.", true));

        lines.Add(Retention(s, fileCount, totalBytes));

        if (s.Get("lc_messages") is { Length: > 0 } lc && !lc.StartsWith('C') && !lc.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            && !lc.Equals("POSIX", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new($"Thông báo log không phải tiếng Anh (lc_messages = {lc}): DbTools có thể không nhận ra loại dòng.", true));
        }
        if (s.Get("log_destination").Contains("csvlog") || s.Get("log_destination").Contains("jsonlog"))
        {
            lines.Add(new("Server ghi thêm file .csv / .json (csvlog / jsonlog): bản này chỉ đọc file .log.", false));
        }
        if (!s.Superuser && !s.Monitor)
        {
            lines.Add(new("User không phải superuser / pg_monitor: không xem được danh sách file log (pg_ls_logdir).", true));
        }
        if (!s.Superuser && !s.ReadFiles)
        {
            lines.Add(new("User không phải superuser / pg_read_server_files: không đọc được nội dung file log.", true));
        }
        return lines;
    }

    /// <summary>Giữ log bao lâu: tên file theo thứ (%a) / ngày (%d) / giờ (%H) + ghi đè khi xoay vòng → tự giới hạn; còn lại lớn dần.</summary>
    private static LogConfigLine Retention(LogServerInfo s, int fileCount, long totalBytes)
    {
        var name = s.Get("log_filename");
        var size = $"hiện {fileCount} file, {Size(totalBytes)}";
        if (s.Get("log_truncate_on_rotation") == "on")
        {
            if (name.Contains("%a"))
            {
                return new($"Chỉ giữ 7 ngày: mỗi thứ trong tuần 1 file, tuần sau ghi đè ({size}). Cần xem lâu hơn thì lưu file về máy (Xuất).", true);
            }
            if (name.Contains("%d"))
            {
                return new($"Giữ khoảng 1 tháng: file theo ngày trong tháng, tháng sau ghi đè ({size}).", false);
            }
            if (name.Contains("%H"))
            {
                return new($"Chỉ giữ 24 giờ: file theo giờ, hôm sau ghi đè ({size}).", true);
            }
        }
        return new($"Không tự xoá file cũ ({size}) - thư mục log lớn dần, cần dọn định kỳ.", totalBytes > 1L << 30);
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} byte",
    };
}
