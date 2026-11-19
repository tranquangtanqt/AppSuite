using DbTools.Core;
using DbTools.Models;
using Npgsql;

namespace DbTools.Services;

/// <summary>1 cột của nguồn mẫu: kiểu, NOT NULL, khoá, DB tự sinh (identity / serial / cột tính), giá trị lớn nhất đang có (cột không
/// trùng), giá trị khoá ngoại (bảng được tham chiếu), mô tả khoá ngoại.</summary>
public sealed record MockSourceColumn(string Name, string TypeText, MockType Type, bool NotNull, bool Unique, bool DbGenerated,
    string? ExistingMax, IReadOnlyList<string>? ForeignValues, string ForeignRef);

/// <summary>Nguồn mẫu: bảng trong DB (biết kiểu, khoá) hoặc file CSV (đoán kiểu).</summary>
public sealed record MockSourceData(string TableSql, string Title, List<MockSourceColumn> Columns, List<string?[]> Samples,
    List<IReadOnlyList<string>> UniqueSets);

public static class MockSource
{
    /// <summary>Số giá trị khoá ngoại tối đa đọc từ bảng được tham chiếu.</summary>
    public const int ForeignLimit = 5000;

    public static async Task<MockSourceData> FromTableAsync(ConnectionProfile p, string database, string table, int sampleRows, CancellationToken ct)
    {
        await using var c = await PgInfo.OpenAsync(p, database, ct);
        uint oid;
        await using (var cmd = new NpgsqlCommand("SELECT to_regclass($1)::oid", c))
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = table.Trim() });
            oid = await cmd.ExecuteScalarAsync(ct) is uint o ? o
                : throw new MockError($"Không thấy bảng {table} trong database {database} (ghi dạng schema.bảng, vd public.app_order).");
        }
        var d = await PgInfo.TableDetailAsync(c, oid, ct);
        var name = d.QualifiedName;
        var cols = d.Columns;

        // Dòng mẫu: mọi cột ép về text để giữ đúng cách PostgreSQL in (ngày, số lẻ...).
        var samples = new List<string?[]>();
        var select = string.Join(", ", cols.Select(x => $"{PgSql.QuoteIdent(x.Name)}::text"));
        await using (var cmd = new NpgsqlCommand($"SELECT {select} FROM {name} LIMIT {Math.Clamp(sampleRows, 1, 1000)}", c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                samples.Add(Enumerable.Range(0, cols.Count).Select(i => r.IsDBNull(i) ? null : r.GetString(i)).ToArray());
            }
        }

        // Khoá chính / duy nhất (danh sách cột).
        var uniqueSets = new List<IReadOnlyList<string>>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT array_agg(a.attname ORDER BY u.ord)
              FROM pg_constraint k CROSS JOIN LATERAL unnest(k.conkey) WITH ORDINALITY AS u(col, ord)
              JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = u.col
             WHERE k.conrelid = $1::oid AND k.contype IN ('p', 'u')
             GROUP BY k.oid
            """, c))
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = (long)oid });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                uniqueSets.Add(r.GetFieldValue<string[]>(0));
            }
        }
        var singleUnique = uniqueSets.Where(s => s.Count == 1).Select(s => s[0]).ToHashSet(StringComparer.Ordinal);

        // Khoá ngoại 1 cột: giá trị đang có ở bảng được tham chiếu.
        var foreign = new Dictionary<string, (string Ref, List<string> Values)>(StringComparer.Ordinal);
        var refs = new List<(string Col, string RefTable, string RefCol)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT a.attname, k.confrelid::regclass::text, ra.attname
              FROM pg_constraint k
              JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = k.conkey[1]
              JOIN pg_attribute ra ON ra.attrelid = k.confrelid AND ra.attnum = k.confkey[1]
             WHERE k.conrelid = $1::oid AND k.contype = 'f' AND array_length(k.conkey, 1) = 1
            """, c))
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = (long)oid });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                refs.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
            }
        }
        foreach (var (col, refTable, refCol) in refs)
        {
            var values = new List<string>();
            await using var cmd = new NpgsqlCommand($"SELECT DISTINCT {PgSql.QuoteIdent(refCol)}::text FROM {refTable} WHERE {PgSql.QuoteIdent(refCol)} IS NOT NULL LIMIT {ForeignLimit}", c);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                values.Add(r.GetString(0));
            }
            foreign[col] = ($"{refTable}({refCol})", values);
        }

        // Giá trị lớn nhất đang có của cột không trùng (Tăng dần bắt đầu sau đó). Chuỗi: max theo thứ tự chữ - đúng khi mã cùng độ dài.
        var result = new List<MockSourceColumn>();
        foreach (var x in cols)
        {
            var type = MockType.Parse(x.Type);
            var generated = x.Identity.Length > 0 || x.Generated.Length > 0 || x.Default.StartsWith("nextval(", StringComparison.Ordinal);
            var unique = singleUnique.Contains(x.Name);
            string? max = null;
            if (unique && !generated && type.Kind is MockKind.Integer or MockKind.Text)
            {
                await using var cmd = new NpgsqlCommand($"SELECT max({PgSql.QuoteIdent(x.Name)})::text FROM {name}", c);
                max = await cmd.ExecuteScalarAsync(ct) as string;
            }
            var fk = foreign.TryGetValue(x.Name, out var f) ? f : default;
            result.Add(new MockSourceColumn(x.Name, x.Type, type, x.NotNull, unique, generated, max, fk.Values, fk.Ref ?? string.Empty));
        }
        return new MockSourceData(name, $"{database}: {d.Schema}.{d.Name}", result, samples, uniqueSets);
    }

    /// <summary>File CSV mẫu: dòng đầu là tên cột; kiểu đoán từ giá trị; không biết khoá → không cột nào bắt buộc không trùng.</summary>
    public static MockSourceData FromCsv(string path, string tableName)
    {
        var (header, rows) = MockOutput.ReadCsv(File.ReadAllBytes(path));
        if (header.Count == 0 || header.All(h => h.Length == 0))
        {
            throw new MockError("File CSV không có dòng tên cột.");
        }
        var cols = header.Select((h, i) =>
        {
            var values = rows.Select(r => r[i]).ToList();
            var type = MockType.Infer(values);
            return new MockSourceColumn(h, type.Display, type, NotNull: values.Count > 0 && values.All(v => v is not null), Unique: false,
                DbGenerated: false, ExistingMax: null, ForeignValues: null, ForeignRef: string.Empty);
        }).ToList();
        var table = tableName.Trim().Length > 0 ? tableName.Trim() : PgSql.QuoteIdentIfNeeded(Path.GetFileNameWithoutExtension(path).ToLowerInvariant());
        return new MockSourceData(table, Path.GetFileName(path), cols, rows, []);
    }
}
