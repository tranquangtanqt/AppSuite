using System.Text.RegularExpressions;

namespace DbTools.Core;

/// <summary>1 bộ công cụ PostgreSQL client trên máy (thư mục bin có pg_dump.exe / pg_restore.exe / psql.exe).</summary>
public sealed record PgClient(string BinFolder, Version Version)
{
    public string Exe(string name) => Path.Combine(BinFolder, name + ".exe");

    public string Display => $"PostgreSQL {Version.Major}.{Math.Max(0, Version.Minor)}  ({BinFolder})";

    public override string ToString() => Display;
}

/// <summary>Đọc số phiên bản PostgreSQL và chọn bộ client hợp với server.</summary>
public static partial class PgVersion
{
    [GeneratedRegex(@"(\d+)(?:\.(\d+))?")]
    private static partial Regex NumberRegex();

    /// <summary>"pg_dump (PostgreSQL) 17.2" / "17.2 (Ubuntu 17.2-1)" / "12.9" / "18beta1" → Version; không có số → null.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var afterName = text.Contains(')') ? text[(text.IndexOf(')') + 1)..] : text; // bỏ "(PostgreSQL)"
        var m = NumberRegex().Match(afterName);
        if (!m.Success)
        {
            m = NumberRegex().Match(text);
        }
        return m.Success ? new Version(int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0) : null;
    }

    /// <summary>Tự động = bản mới nhất: pg_dump / pg_restore / psql mới đọc được server và file dump cũ hơn, còn bản cũ thì
    /// không dump được server mới hơn ("server version mismatch").</summary>
    public static PgClient? Choose(IEnumerable<PgClient> clients) =>
        clients.OrderByDescending(c => c.Version).FirstOrDefault();

    /// <summary>Cảnh báo khi bộ client cũ hơn server (pg_dump sẽ từ chối chạy). Null = dùng được.</summary>
    public static string? Mismatch(PgClient client, int serverMajor) => client.Version.Major < serverMajor
        ? $"pg_dump {client.Version.Major} cũ hơn server PostgreSQL {serverMajor} - pg_dump sẽ từ chối chạy. Cài / chọn bộ client PostgreSQL {serverMajor} trở lên."
        : null;
}

/// <summary>Kiểu file dump: custom (-Fc, nén, restore chọn lọc được bằng pg_restore) hoặc SQL thường (chạy bằng psql).</summary>
public enum DumpFormat
{
    Custom,
    Plain,
}

public sealed record DumpOptions(DumpFormat Format, bool SchemaOnly = false, string? Schema = null);

/// <param name="Clean">--clean --if-exists: xoá đối tượng trùng tên trong DB đích trước khi tạo lại.</param>
/// <param name="NoOwner">--no-owner --no-privileges: không đặt chủ sở hữu / GRANT theo DB gốc (user đích khác user nguồn).</param>
/// <param name="SingleTransaction">--single-transaction --exit-on-error: lỗi là huỷ hết, DB đích không dở dang.</param>
public sealed record RestoreOptions(bool Clean = false, bool NoOwner = false, bool SingleTransaction = true);

/// <summary>Đích của 1 lệnh: server + user + database (mật khẩu truyền qua biến môi trường PGPASSWORD, không qua dòng lệnh).</summary>
public sealed record PgTarget(string Host, int Port, string User, string Database)
{
    public string Display => $"{User}@{Host}:{Port}/{Database}";
}

/// <summary>
/// Dựng tham số dòng lệnh cho pg_dump / pg_restore / psql (mỗi phần tử 1 tham số - ProcessStartInfo.ArgumentList tự đặt
/// dấu nháy). Luôn -w (không hỏi mật khẩu: sai / thiếu mật khẩu thì báo lỗi ngay thay vì treo chờ gõ).
/// </summary>
public static class PgArgs
{
    public static List<string> Connection(PgTarget t) =>
        ["-h", t.Host, "-p", t.Port.ToString(), "-U", t.User, "-d", t.Database, "-w"];

    /// <summary>Tên trong mẫu tên (pattern) của pg_dump: luôn trong "..." để khớp đúng nguyên văn (hoa / thường, $, ., *).</summary>
    public static string Pattern(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    public static string Pattern(QualifiedName name) =>
        (name.Schema is null ? string.Empty : Pattern(name.Schema) + ".") + Pattern(name.Name);

    public static List<string> Dump(PgTarget t, DumpOptions o, string outFile)
    {
        var args = Connection(t);
        args.AddRange(["-v", "-F", o.Format == DumpFormat.Custom ? "c" : "p"]);
        if (o.SchemaOnly)
        {
            args.Add("-s");
        }
        if (!string.IsNullOrWhiteSpace(o.Schema))
        {
            args.AddRange(["-n", Pattern(o.Schema.Trim())]);
        }
        args.AddRange(["-f", outFile]);
        return args;
    }

    /// <summary>Dump vài bảng / view / sequence (dạng custom) - sao lưu trước khi chạy script.</summary>
    public static List<string> DumpRelations(PgTarget t, IEnumerable<QualifiedName> relations, bool schemaOnly, string outFile)
    {
        var args = Connection(t);
        args.AddRange(["-v", "-F", "c"]);
        if (schemaOnly)
        {
            args.Add("-s");
        }
        foreach (var r in relations)
        {
            args.AddRange(["-t", Pattern(r)]);
        }
        args.AddRange(["-f", outFile]);
        return args;
    }

    public static List<string> Restore(PgTarget t, RestoreOptions o, string file)
    {
        var args = Connection(t);
        args.Add("-v");
        if (o.Clean)
        {
            args.AddRange(["--clean", "--if-exists"]);
        }
        if (o.NoOwner)
        {
            args.AddRange(["--no-owner", "--no-privileges"]);
        }
        if (o.SingleTransaction)
        {
            args.AddRange(["--single-transaction", "--exit-on-error"]);
        }
        args.Add(file);
        return args;
    }

    /// <summary>Chạy file SQL thường (dump -Fp) bằng psql: dừng ở lỗi đầu tiên; -q bỏ các dòng "CREATE TABLE", "COPY 10"...</summary>
    public static List<string> Psql(PgTarget t, bool singleTransaction, string file)
    {
        var args = Connection(t);
        args.AddRange(["-q", "-v", "ON_ERROR_STOP=1"]);
        if (singleTransaction)
        {
            args.Add("--single-transaction");
        }
        args.AddRange(["-f", file]);
        return args;
    }

    /// <summary>Tên file dump mặc định: &lt;db&gt;_yyyyMMdd_HHmm.dump (SQL thường: .sql), bỏ ký tự không hợp lệ trong tên file.</summary>
    public static string DumpFileName(string database, DumpFormat format, DateTime time, string? schema = null)
    {
        var name = SafeFileName(database) + (string.IsNullOrWhiteSpace(schema) ? string.Empty : "_" + SafeFileName(schema.Trim()));
        return $"{name}_{time:yyyyMMdd_HHmm}{(format == DumpFormat.Custom ? ".dump" : ".sql")}";
    }

    public static string SafeFileName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(text.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return safe.Length == 0 ? "db" : safe;
    }

    /// <summary>Đường dẫn chưa có: thêm _2, _3... trước đuôi file nếu trùng.</summary>
    public static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var n = 2; File.Exists(path) || Directory.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{stem}_{n}{ext}");
        }
        return path;
    }
}
