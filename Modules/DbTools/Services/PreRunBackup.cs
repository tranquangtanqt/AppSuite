using System.Text;
using DbTools.Core;
using DbTools.Models;
using Npgsql;

namespace DbTools.Services;

/// <summary>Kết quả sao lưu trước khi chạy. <paramref name="Folder"/> rỗng = không có gì cần sao lưu (thư mục chỉ tạo đối tượng mới).</summary>
public sealed record BackupResult(string Folder, int DataTables, int StructureRelations, int Functions, int Triggers)
{
    public bool Skipped => Folder.Length == 0;

    public string Describe() => Skipped
        ? "không có đối tượng nào đang có trong DB bị đụng tới"
        : $"{DataTables} bảng (cấu trúc + dữ liệu), {StructureRelations} bảng / view / sequence (cấu trúc), {Functions} hàm, {Triggers} trigger";
}

/// <summary>
/// Trước khi Chạy: dump những đối tượng đang có trong DB mà thư mục script sẽ đổi (xem <see cref="BackupPlan"/>) vào
/// &lt;thư mục dump&gt;\TruocKhiChay\yyyyMMdd_HHmmss_&lt;db&gt;\:
/// bang-du-lieu.dump (pg_dump -t, cả dữ liệu), cau-truc.dump (pg_dump -s -t), ham-trigger.sql (CREATE OR REPLACE hiện tại),
/// doc-ky.txt (có gì + cách lấy lại). Lỗi bất kỳ → ném lỗi, không chạy script.
/// </summary>
public static class PreRunBackup
{
    public const string SubFolder = "TruocKhiChay";

    public static async Task<BackupResult> RunAsync(ConnectionProfile profile, PgClient? client, BackupPlan plan, string dumpRoot,
        string scriptFolder, Action<string> log, CancellationToken ct)
    {
        if (plan.IsEmpty)
        {
            return new BackupResult(string.Empty, 0, 0, 0, 0);
        }
        List<(QualifiedName Name, char Kind)> data, structure;
        List<(string Signature, string Definition)> functions;
        List<(string Name, string Table, string Definition)> triggers;
        await using (var c = await Pg.OpenAsync(profile, ct))
        {
            data = await ResolveRelationsAsync(c, plan.DataRelations, ct);
            structure = await ResolveRelationsAsync(c, plan.StructureRelations, ct);
            functions = await FunctionDefsAsync(c, plan.Functions, ct);
            triggers = await TriggerDefsAsync(c, plan.Triggers, ct);
        }
        // Bảng chỉ có dữ liệu khi là bảng thật; view / sequence trong nhóm dữ liệu → nhóm cấu trúc. Trùng thì nhóm dữ liệu thắng.
        var dataTables = data.Where(d => d.Kind is 'r' or 'p').Select(d => d.Name).ToList();
        var dataKeys = dataTables.Select(n => n.Sql).ToHashSet(StringComparer.Ordinal);
        var structureOnly = structure.Concat(data.Where(d => d.Kind is not ('r' or 'p')))
            .Select(s => s.Name).Where(n => !dataKeys.Contains(n.Sql))
            .GroupBy(n => n.Sql).Select(g => g.First()).ToList();
        if (dataTables.Count + structureOnly.Count + functions.Count + triggers.Count == 0)
        {
            return new BackupResult(string.Empty, 0, 0, 0, 0);
        }
        if ((dataTables.Count > 0 || structureOnly.Count > 0) && client is null)
        {
            throw new InvalidOperationException("Không tìm thấy pg_dump.exe để sao lưu bảng - cài PostgreSQL (bản client) / chọn thư mục bin ở trang Sao lưu / Khôi phục, hoặc bỏ tích \"Sao lưu trước khi chạy\".");
        }

        var root = Path.Combine(dumpRoot, SubFolder);
        Directory.CreateDirectory(root);
        var name = $"{DateTime.Now:yyyyMMdd_HHmmss}_{PgArgs.SafeFileName(profile.Database)}"
            + (profile.SchemaOrNull is { } schema ? "_" + PgArgs.SafeFileName(schema) : string.Empty);
        var folder = PgArgs.UniquePath(root, name);
        Directory.CreateDirectory(folder);
        var target = PgAdmin.Target(profile);
        try
        {
            if (dataTables.Count > 0)
            {
                log($"Sao lưu {dataTables.Count} bảng (cấu trúc + dữ liệu): {string.Join(", ", dataTables.Select(n => n.Sql))}");
                await DumpAsync(client!, PgArgs.DumpRelations(target, dataTables, schemaOnly: false, Path.Combine(folder, "bang-du-lieu.dump")),
                    profile.Password, log, ct);
            }
            if (structureOnly.Count > 0)
            {
                log($"Sao lưu cấu trúc {structureOnly.Count} bảng / view / sequence: {string.Join(", ", structureOnly.Select(n => n.Sql))}");
                await DumpAsync(client!, PgArgs.DumpRelations(target, structureOnly, schemaOnly: true, Path.Combine(folder, "cau-truc.dump")),
                    profile.Password, log, ct);
            }
            if (functions.Count + triggers.Count > 0)
            {
                log($"Sao lưu định nghĩa {functions.Count} hàm, {triggers.Count} trigger → ham-trigger.sql");
                var sql = new StringBuilder()
                    .AppendLine($"-- Định nghĩa hàm / trigger đang có trong {target.Display} lúc {DateTime.Now:yyyy-MM-dd HH:mm:ss}, trước khi chạy {scriptFolder}")
                    .AppendLine("-- Chạy lại file này (vd DbTools hoặc psql -f) để trả hàm / trigger về như cũ.")
                    .AppendLine();
                foreach (var (signature, definition) in functions)
                {
                    sql.AppendLine($"-- {signature}").AppendLine(definition.TrimEnd() + ";").AppendLine();
                }
                foreach (var (trigger, table, definition) in triggers)
                {
                    sql.AppendLine($"-- trigger {trigger} trên {table}")
                       .AppendLine($"DROP TRIGGER IF EXISTS {Pg.QuoteIdent(trigger)} ON {table};")
                       .AppendLine(definition.TrimEnd() + ";").AppendLine();
                }
                await File.WriteAllTextAsync(Path.Combine(folder, "ham-trigger.sql"), sql.ToString(), new UTF8Encoding(false), ct);
            }
            var result = new BackupResult(folder, dataTables.Count, structureOnly.Count, functions.Count, triggers.Count);
            await File.WriteAllTextAsync(Path.Combine(folder, "doc-ky.txt"), ReadMe(target, scriptFolder, result, dataTables, structureOnly, client),
                new UTF8Encoding(true), ct);
            return result;
        }
        catch
        {
            TryDelete(folder); // bản sao lưu dở dang dễ bị tưởng là dùng được
            throw;
        }
    }

    private static async Task DumpAsync(PgClient client, List<string> args, string password, Action<string> log, CancellationToken ct)
    {
        var tail = new List<string>();
        var code = await PgTools.RunAsync(client.Exe("pg_dump"), args, password, line =>
        {
            tail.Add(line);
            if (tail.Count > 20)
            {
                tail.RemoveAt(0);
            }
        }, ct);
        if (code != 0)
        {
            throw new InvalidOperationException($"pg_dump lỗi (mã {code}): " + string.Join(" | ", tail.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("fatal", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(tail.LastOrDefault() ?? string.Empty)));
        }
    }

    /// <summary>Tên → bảng / view / sequence có thật (theo search_path của kết nối nếu tên không ghi schema), ghi đủ schema.</summary>
    private static async Task<List<(QualifiedName, char)>> ResolveRelationsAsync(NpgsqlConnection c, IReadOnlyList<QualifiedName> names, CancellationToken ct)
    {
        var result = new List<(QualifiedName, char)>();
        if (names.Count == 0)
        {
            return result;
        }
        const string sql = """
            SELECT DISTINCT n.nspname, c.relname, c.relkind::text
              FROM unnest($1::text[]) AS x(name)
              JOIN pg_class c ON c.oid = to_regclass(x.name)
              JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE c.relkind IN ('r', 'p', 'v', 'm', 'S')
            """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = names.Select(n => n.Sql).ToArray() });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add((new QualifiedName(r.GetString(0), r.GetString(1), true), r.GetString(2)[0]));
        }
        return result;
    }

    private static async Task<List<(string, string)>> FunctionDefsAsync(NpgsqlConnection c, IReadOnlyList<QualifiedName> names, CancellationToken ct)
    {
        var result = new List<(string, string)>();
        if (names.Count == 0)
        {
            return result;
        }
        const string sql = """
            SELECT p.oid::regprocedure::text, pg_get_functiondef(p.oid)
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE p.prokind IN ('f', 'p') AND p.proname = ANY($1)
               AND (n.nspname = ANY(current_schemas(false)) OR n.nspname = ANY($2))
             ORDER BY 1
            """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = names.Select(n => n.Name).Distinct().ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter { Value = names.Where(n => n.Schema is not null).Select(n => n.Schema!).Distinct().ToArray() });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add((r.GetString(0), r.GetString(1)));
        }
        return result;
    }

    private static async Task<List<(string, string, string)>> TriggerDefsAsync(NpgsqlConnection c, IReadOnlyList<QualifiedName> names, CancellationToken ct)
    {
        var result = new List<(string, string, string)>();
        if (names.Count == 0)
        {
            return result;
        }
        const string sql = """
            SELECT t.tgname, t.tgrelid::regclass::text, pg_get_triggerdef(t.oid, true)
              FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE NOT t.tgisinternal AND t.tgname = ANY($1) AND n.nspname = ANY(current_schemas(false))
             ORDER BY 2, 1
            """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = names.Select(n => n.Name).Distinct().ToArray() });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        }
        return result;
    }

    private static string ReadMe(PgTarget target, string scriptFolder, BackupResult result, List<QualifiedName> data,
        List<QualifiedName> structure, PgClient? client)
    {
        var restore = client is null ? "pg_restore" : $"\"{client.Exe("pg_restore")}\"";
        var conn = $"-h {target.Host} -p {target.Port} -U {target.User} -d {target.Database}";
        var sb = new StringBuilder()
            .AppendLine("Bản sao lưu DbTools trước khi chạy thư mục script")
            .AppendLine($"Lúc: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Database: {target.Display}")
            .AppendLine($"Thư mục script: {scriptFolder}")
            .AppendLine($"Gồm: {result.Describe()}")
            .AppendLine();
        if (data.Count > 0)
        {
            sb.AppendLine("bang-du-lieu.dump - cấu trúc + dữ liệu của: " + string.Join(", ", data.Select(n => n.Sql)))
              .AppendLine($"  Lấy lại cả bảng (xoá bảng hiện tại rồi tạo lại từ bản sao lưu): {restore} {conn} --clean --if-exists --single-transaction bang-du-lieu.dump")
              .AppendLine($"  Chỉ lấy lại dữ liệu (bảng còn nguyên, đã TRUNCATE): {restore} {conn} --data-only --single-transaction bang-du-lieu.dump")
              .AppendLine("  Hoặc dùng trang Sao lưu / Khôi phục của DbTools (Restore → Ghi vào DB có sẵn).")
              .AppendLine();
        }
        if (structure.Count > 0)
        {
            sb.AppendLine("cau-truc.dump - chỉ cấu trúc (không dữ liệu) của: " + string.Join(", ", structure.Select(n => n.Sql)))
              .AppendLine($"  Xem nội dung: {restore} -f cau-truc.sql cau-truc.dump (ra file SQL để đọc / chép từng phần)")
              .AppendLine();
        }
        if (result.Functions + result.Triggers > 0)
        {
            sb.AppendLine("ham-trigger.sql - CREATE OR REPLACE FUNCTION / CREATE TRIGGER như trước khi chạy: chạy file này để trả về bản cũ.");
        }
        return sb.ToString();
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception)
        {
            // để lại thì thôi
        }
    }
}
