using System.Text;

namespace DbTools.Core;

/// <summary>Câu SQL quản trị dùng chung cho lúc chạy thật (PgAdmin) và lúc Xem lệnh - 2 bên luôn khớp nhau.</summary>
public static class PgSql
{
    public static string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    public static string Literal(string text) => "'" + text.Replace("'", "''") + "'";

    /// <summary>Như quote_ident của PostgreSQL: chỉ thêm "..." khi cần (chữ hoa, ký tự lạ, bắt đầu bằng số, từ khoá hay gặp).</summary>
    public static string QuoteIdentIfNeeded(string name) =>
        name.Length > 0 && (char.IsAsciiLetterLower(name[0]) || name[0] == '_')
        && name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '$')
        && !ReservedWords.Contains(name)
            ? name
            : QuoteIdent(name);

    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "all", "analyse", "analyze", "and", "any", "array", "as", "asc", "asymmetric", "both", "case", "cast", "check", "collate",
        "column", "constraint", "create", "current_catalog", "current_date", "current_role", "current_time", "current_timestamp",
        "current_user", "default", "deferrable", "desc", "distinct", "do", "else", "end", "except", "false", "fetch", "for", "foreign",
        "from", "grant", "group", "having", "in", "initially", "intersect", "into", "lateral", "leading", "limit", "localtime",
        "localtimestamp", "not", "null", "offset", "on", "only", "or", "order", "placing", "primary", "references", "returning",
        "select", "session_user", "some", "symmetric", "table", "then", "to", "trailing", "true", "union", "unique", "user", "using",
        "variadic", "when", "where", "window", "with",
    };

    public static string CreateDatabase(string database) => $"CREATE DATABASE {QuoteIdent(database)} TEMPLATE template0";

    public static string CopyDatabase(string source, string target) => $"CREATE DATABASE {QuoteIdent(target)} TEMPLATE {QuoteIdent(source)}";

    public const string ServerVersion = "SELECT current_setting('server_version_num')::int";

    public static string DatabaseExists(string database) => $"SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = {Literal(database)})";

    public static string Sessions(string database) =>
        $"SELECT pid, usename, application_name, client_addr, state FROM pg_stat_activity WHERE backend_type = 'client backend' AND datname = {Literal(database)}";

    public static string TerminateDatabase(string database) =>
        $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE backend_type = 'client backend' AND datname = {Literal(database)} AND pid <> pg_backend_pid()";
}

/// <summary>
/// "Xem lệnh" ở trang Sao lưu / Khôi phục: các bước sẽ chạy khi bấm Dump / Restore / Copy, viết thành script .bat chạy được trong
/// cmd (câu SQL viết thành psql -c). Mật khẩu không ghi ra - dòng set PGPASSWORD để chỗ trống cho người dùng tự điền.
/// </summary>
public sealed class CommandPreview
{
    private readonly StringBuilder _sb = new();
    private readonly PgClient? _client;
    private int _step;

    public CommandPreview(string title, PgClient? client)
    {
        _client = client;
        _sb.AppendLine("@echo off")
           .AppendLine("chcp 65001 > nul")
           .AppendLine($"REM DbTools - {title}: các lệnh sẽ chạy (xem trước, chưa chạy gì)")
           .AppendLine("REM App truyền mật khẩu qua biến môi trường PGPASSWORD (lấy từ hồ sơ kết nối) - không ghi ra đây.");
        if (client is null)
        {
            _sb.AppendLine("REM Chưa tìm thấy PostgreSQL client trên máy - đường dẫn pg_dump / pg_restore / psql dưới đây để trống.");
        }
    }

    public string Exe(string tool) => _client is null ? tool + ".exe" : _client.Exe(tool);

    public CommandPreview Password(string profileName)
    {
        _sb.AppendLine().AppendLine($"set PGPASSWORD=<mật khẩu của hồ sơ \"{profileName}\">");
        return this;
    }

    public CommandPreview Note(string text)
    {
        _sb.AppendLine($"REM {text}");
        return this;
    }

    public CommandPreview Command(string description, string tool, IEnumerable<string> args)
    {
        _sb.AppendLine().AppendLine($"REM {++_step}. {description}").AppendLine(Line(Exe(tool), args));
        return this;
    }

    /// <summary>Câu SQL app chạy qua Npgsql trên <paramref name="database"/> - viết thành psql -c cho chạy tay được.</summary>
    public CommandPreview Sql(string description, PgTarget server, string database, string sql)
    {
        var args = PgArgs.Connection(server with { Database = database });
        args.AddRange(["-At", "-c", sql]);
        return Command(description, "psql", args);
    }

    public CommandPreview Raw(string description, string line)
    {
        _sb.AppendLine().AppendLine($"REM {++_step}. {description}").AppendLine(line);
        return this;
    }

    public override string ToString() => _sb.ToString();

    /// <summary>1 dòng lệnh: exe + tham số, đặt dấu nháy như .NET truyền cho tiến trình (quy tắc của trình đọc tham số MSVC).</summary>
    public static string Line(string exe, IEnumerable<string> args) =>
        string.Join(' ', new[] { "\"" + exe + "\"" }.Concat(args.Select(Quote)));

    public static string Quote(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            return arg;
        }
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }
            backslashes = 0;
        }
        return sb.Append('\\', backslashes * 2).Append('"').ToString();
    }

    // ----- Các việc của trang Sao lưu / Khôi phục -----

    public static string ForDump(PgClient? client, string profileName, PgTarget target, DumpOptions options, string file)
    {
        var maintenance = target with { Database = "postgres" };
        return new CommandPreview("Dump", client)
            .Note($"Tên file theo giờ lúc bấm Dump; trùng tên thì thêm _2, _3... Lỗi thì app xoá file dở dang.")
            .Password(profileName)
            .Sql("Kiểm tra bản server (chỉ đọc) - pg_dump phải cùng bản hoặc mới hơn", maintenance, "postgres", PgSql.ServerVersion)
            .Command("Dump (nhật ký ghi vào file .log cùng tên)", "pg_dump", PgArgs.Dump(target, options, file))
            .ToString();
    }

    public static string ForRestore(PgClient? client, string profileName, PgTarget target, RestoreOptions options, string file,
        bool plainSql, bool toNew)
    {
        var p = new CommandPreview("Restore", client).Password(profileName);
        if (!plainSql)
        {
            p.Command("Đọc thông tin file dump (Xem nội dung, chỉ đọc file)", "pg_restore", ["-l", file]);
        }
        p.Sql($"Kiểm tra database {target.Database} đã có chưa (chỉ đọc){(toNew ? " - có rồi thì dừng" : " - chưa có thì dừng")}",
            target, "postgres", PgSql.DatabaseExists(target.Database));
        if (toNew)
        {
            p.Sql("Tạo database mới (trống)", target, "postgres", PgSql.CreateDatabase(target.Database));
        }
        return (plainSql
                ? p.Command("Chạy file SQL bằng psql", "psql", PgArgs.Psql(target, options.SingleTransaction, file))
                : p.Command("Restore", "pg_restore", PgArgs.Restore(target, options, file)))
            .ToString();
    }

    public static string ForCopySameServer(PgClient? client, string profileName, PgTarget server, string source, string target)
    {
        return new CommandPreview("Copy DB (cùng server)", client)
            .Note("App chạy các câu SQL này qua kết nối riêng (Npgsql), không gọi psql; dưới đây viết thành psql -c để chạy tay được.")
            .Password(profileName)
            .Sql($"Kiểm tra database nguồn {source} có không (chỉ đọc)", server, "postgres", PgSql.DatabaseExists(source))
            .Sql($"Kiểm tra database {target} chưa có (chỉ đọc)", server, "postgres", PgSql.DatabaseExists(target))
            .Sql("Xem phiên đang kết nối vào DB nguồn (chỉ đọc) - có thì app hỏi \"Ngắt phiên rồi copy\"", server, "postgres", PgSql.Sessions(source))
            .Sql("Chỉ khi bấm \"Ngắt phiên rồi copy\": ngắt các phiên đó", server, "postgres", PgSql.TerminateDatabase(source))
            .Sql("Copy (chép nguyên khối)", server, "postgres", PgSql.CopyDatabase(source, target))
            .ToString();
    }

    public static string ForCopyOtherServer(PgClient? client, string sourceProfile, PgTarget source, string targetProfile, PgTarget target,
        bool noOwner, string tempFile)
    {
        return new CommandPreview("Copy DB (khác server)", client)
            .Note("Câu SQL app chạy qua kết nối riêng (Npgsql); dưới đây viết thành psql -c để chạy tay được. Tên file tạm theo giờ lúc bấm Copy.")
            .Password(sourceProfile)
            .Sql($"Kiểm tra database nguồn {source.Database} có không (chỉ đọc)", source, "postgres", PgSql.DatabaseExists(source.Database))
            .Password(targetProfile)
            .Sql($"Kiểm tra database {target.Database} chưa có trên server đích (chỉ đọc)", target, "postgres", PgSql.DatabaseExists(target.Database))
            .Password(sourceProfile)
            .Sql("Kiểm tra bản server nguồn (chỉ đọc) - pg_dump phải cùng bản hoặc mới hơn", source, "postgres", PgSql.ServerVersion)
            .Command("Dump DB nguồn ra file tạm", "pg_dump", PgArgs.Dump(source, new DumpOptions(DumpFormat.Custom), tempFile))
            .Password(targetProfile)
            .Sql("Tạo database mới (trống) trên server đích", target, "postgres", PgSql.CreateDatabase(target.Database))
            .Command("Restore vào database mới", "pg_restore", PgArgs.Restore(target, new RestoreOptions(NoOwner: noOwner), tempFile))
            .Raw("Xoá file tạm", $"del {Quote(tempFile)}")
            .ToString();
    }
}
