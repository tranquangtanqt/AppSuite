using DbTools.Core;
using DbTools.Models;
using Npgsql;

namespace DbTools.Services;

public sealed record DbOverview(string Database, string Version, long SizeBytes, string Encoding, string Collate, string Ctype, string Owner,
    DateTime? ServerStart, int Sessions, string Comment);

public sealed record SchemaStat(string Schema, string Owner, int Tables, int Views, int Functions, int Triggers, int Sequences, long SizeBytes);

public sealed record TableItem(uint Oid, string Schema, string Name, string Kind, long RowEstimate, long TotalBytes, long TableBytes,
    long IndexBytes, int Columns, bool HasPrimaryKey, string Comment, DateTime? LastVacuum, DateTime? LastAnalyze, bool IsPartition);

/// <summary>1 đối tượng ở tab Đối tượng khác. <paramref name="Kind"/>: view, matview, function, procedure, trigger, sequence, extension.
/// <paramref name="Definition"/> có sẵn (extension, sequence) hoặc rỗng = đọc khi chọn.</summary>
public sealed record DbObjectItem(uint Oid, string Kind, string Schema, string Name, string Info, string Comment, string Definition = "");

/// <summary>
/// Trang Thông tin DB: đọc catalog của 1 database (chỉ SELECT, không thay đổi gì). Bỏ schema hệ thống (pg_catalog,
/// information_schema, pg_toast, pg_temp). Số dòng là ước lượng (thống kê của server); đếm chính xác chỉ khi người dùng bấm.
/// </summary>
public static class PgInfo
{
    private const string UserSchemas =
        "n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg\\_toast%' AND n.nspname NOT LIKE 'pg\\_temp%'";

    public static Task<NpgsqlConnection> OpenAsync(ConnectionProfile p, string database, CancellationToken ct) =>
        Pg.OpenAsync(PgAdmin.On(p, database), ct);

    public static async Task<DbOverview> OverviewAsync(NpgsqlConnection c, CancellationToken ct)
    {
        const string sql = """
            SELECT current_database(), current_setting('server_version'), pg_database_size(d.oid), pg_encoding_to_char(d.encoding),
                   d.datcollate::text, d.datctype::text, pg_get_userbyid(d.datdba), pg_postmaster_start_time(),
                   (SELECT count(*)::int FROM pg_stat_activity a WHERE a.datname = d.datname AND a.backend_type = 'client backend'),
                   coalesce(shobj_description(d.oid, 'pg_database'), '')
              FROM pg_database d WHERE d.datname = current_database()
            """;
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return new DbOverview(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
            r.IsDBNull(7) ? null : r.GetDateTime(7).ToLocalTime(), r.GetInt32(8), r.GetString(9));
    }

    public static async Task<List<SchemaStat>> SchemasAsync(NpgsqlConnection c, CancellationToken ct)
    {
        var sql = $"""
            SELECT n.nspname, pg_get_userbyid(n.nspowner),
                   (SELECT count(*)::int FROM pg_class c WHERE c.relnamespace = n.oid AND c.relkind IN ('r', 'p', 'f')),
                   (SELECT count(*)::int FROM pg_class c WHERE c.relnamespace = n.oid AND c.relkind IN ('v', 'm')),
                   (SELECT count(*)::int FROM pg_proc p WHERE p.pronamespace = n.oid AND p.prokind IN ('f', 'p')),
                   (SELECT count(*)::int FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid WHERE c.relnamespace = n.oid AND NOT t.tgisinternal),
                   (SELECT count(*)::int FROM pg_class c WHERE c.relnamespace = n.oid AND c.relkind = 'S'),
                   (SELECT coalesce(sum(pg_total_relation_size(c.oid)), 0)::bigint FROM pg_class c WHERE c.relnamespace = n.oid AND c.relkind IN ('r', 'm'))
              FROM pg_namespace n WHERE {UserSchemas} ORDER BY 1
            """;
        var result = new List<SchemaStat>();
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new SchemaStat(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt64(7)));
        }
        return result;
    }

    public static async Task<List<TableItem>> TablesAsync(NpgsqlConnection c, CancellationToken ct)
    {
        var sql = $"""
            SELECT c.oid, n.nspname, c.relname, c.relkind::text,
                   greatest(coalesce(s.n_live_tup, 0), c.reltuples::bigint, 0),
                   pg_total_relation_size(c.oid), pg_relation_size(c.oid), pg_indexes_size(c.oid),
                   (SELECT count(*)::int FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped),
                   EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conrelid = c.oid AND k.contype = 'p'),
                   coalesce(obj_description(c.oid, 'pg_class'), ''),
                   greatest(s.last_vacuum, s.last_autovacuum), greatest(s.last_analyze, s.last_autoanalyze), c.relispartition
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
              LEFT JOIN pg_stat_all_tables s ON s.relid = c.oid
             WHERE c.relkind IN ('r', 'p', 'f') AND {UserSchemas}
             ORDER BY 2, 3
            """;
        var result = new List<TableItem>();
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new TableItem(r.GetFieldValue<uint>(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetInt64(5),
                r.GetInt64(6), r.GetInt64(7), r.GetInt32(8), r.GetBoolean(9), r.GetString(10),
                r.IsDBNull(11) ? null : r.GetDateTime(11).ToLocalTime(), r.IsDBNull(12) ? null : r.GetDateTime(12).ToLocalTime(), r.GetBoolean(13)));
        }
        return result;
    }

    public static async Task<long> CountRowsAsync(NpgsqlConnection c, string schema, string table, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {PgSql.QuoteIdent(schema)}.{PgSql.QuoteIdent(table)}", c) { CommandTimeout = 0 };
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public static async Task<List<DbObjectItem>> ObjectsAsync(NpgsqlConnection c, CancellationToken ct)
    {
        var result = new List<DbObjectItem>();
        async Task Read(string sql, Func<NpgsqlDataReader, DbObjectItem> map)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                result.Add(map(r));
            }
        }

        await Read($"""
            SELECT c.oid, n.nspname, c.relname, c.relkind::text, coalesce(obj_description(c.oid, 'pg_class'), '')
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE c.relkind IN ('v', 'm') AND {UserSchemas} ORDER BY 2, 3
            """, r => new DbObjectItem(r.GetFieldValue<uint>(0), r.GetString(3) == "m" ? "matview" : "view", r.GetString(1), r.GetString(2),
                r.GetString(3) == "m" ? "materialized view" : "view", r.GetString(4)));

        await Read($"""
            SELECT p.oid, n.nspname, p.proname, pg_get_function_identity_arguments(p.oid),
                   CASE WHEN p.prokind = 'p' THEN '' ELSE pg_get_function_result(p.oid) END, l.lanname, p.prokind::text,
                   coalesce(obj_description(p.oid, 'pg_proc'), '')
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace JOIN pg_language l ON l.oid = p.prolang
             WHERE p.prokind IN ('f', 'p') AND {UserSchemas} ORDER BY 2, 3, 4
            """, r => new DbObjectItem(r.GetFieldValue<uint>(0), r.GetString(6) == "p" ? "procedure" : "function", r.GetString(1), r.GetString(2),
                $"({r.GetString(3)}){(r.GetString(4).Length > 0 ? " → " + r.GetString(4) : " · procedure")} · {r.GetString(5)}", r.GetString(7)));

        await Read($"""
            SELECT t.oid, n.nspname, t.tgname, c.relname, t.tgenabled::text, t.tgfoid::regproc::text, coalesce(obj_description(t.oid, 'pg_trigger'), '')
              FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE NOT t.tgisinternal AND {UserSchemas} ORDER BY 2, 4, 3
            """, r => new DbObjectItem(r.GetFieldValue<uint>(0), "trigger", r.GetString(1), r.GetString(2),
                $"trên {r.GetString(3)} → {r.GetString(5)}{(r.GetString(4) == "D" ? " · ĐANG TẮT" : string.Empty)}", r.GetString(6)));

        await Read($"""
            SELECT c.oid, n.nspname, c.relname, s.data_type::text, s.start_value, s.min_value, s.max_value, s.increment_by, s.cycle,
                   s.cache_size, s.last_value, coalesce(obj_description(c.oid, 'pg_class'), '')
              FROM pg_sequences s JOIN pg_namespace n ON n.nspname = s.schemaname
              JOIN pg_class c ON c.relname = s.sequencename AND c.relnamespace = n.oid
             WHERE {UserSchemas} ORDER BY 2, 3
            """, r =>
        {
            var name = $"{PgSql.QuoteIdentIfNeeded(r.GetString(1))}.{PgSql.QuoteIdentIfNeeded(r.GetString(2))}";
            var last = r.IsDBNull(10) ? "chưa dùng / không đủ quyền đọc" : r.GetInt64(10).ToString("N0");
            var ddl = $"CREATE SEQUENCE {name}\n    AS {r.GetString(3)}\n    START WITH {r.GetInt64(4)}\n    INCREMENT BY {r.GetInt64(7)}\n"
                + $"    MINVALUE {r.GetInt64(5)}\n    MAXVALUE {r.GetInt64(6)}\n    CACHE {r.GetInt64(9)}{(r.GetBoolean(8) ? "\n    CYCLE" : string.Empty)};\n"
                + $"-- giá trị hiện tại (last_value): {last}";
            return new DbObjectItem(r.GetFieldValue<uint>(0), "sequence", r.GetString(1), r.GetString(2), $"hiện tại: {last}", r.GetString(11), ddl);
        });

        await Read("""
            SELECT e.oid, n.nspname, e.extname, e.extversion, coalesce(obj_description(e.oid, 'pg_extension'), '')
              FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace ORDER BY 3
            """, r => new DbObjectItem(r.GetFieldValue<uint>(0), "extension", r.GetString(1), r.GetString(2), $"bản {r.GetString(3)}", r.GetString(4),
                $"CREATE EXTENSION IF NOT EXISTS {PgSql.QuoteIdentIfNeeded(r.GetString(2))} WITH SCHEMA {PgSql.QuoteIdentIfNeeded(r.GetString(1))} VERSION {PgSql.Literal(r.GetString(3))};"));
        return result;
    }

    /// <summary>Định nghĩa (CREATE ...) của view / hàm / trigger; sequence / extension đã có sẵn trong <see cref="DbObjectItem.Definition"/>.</summary>
    public static async Task<string> DefinitionAsync(NpgsqlConnection c, DbObjectItem item, CancellationToken ct)
    {
        if (item.Definition.Length > 0)
        {
            return item.Definition;
        }
        var name = $"{PgSql.QuoteIdentIfNeeded(item.Schema)}.{PgSql.QuoteIdentIfNeeded(item.Name)}";
        var sql = item.Kind switch
        {
            "view" or "matview" => "SELECT pg_get_viewdef($1::oid, true)",
            "function" or "procedure" => "SELECT pg_get_functiondef($1::oid)",
            "trigger" => "SELECT pg_get_triggerdef($1::oid, true)",
            _ => throw new InvalidOperationException("Không có định nghĩa cho " + item.Kind),
        };
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = (long)item.Oid });
        var body = (await cmd.ExecuteScalarAsync(ct) as string ?? string.Empty).TrimEnd();
        var comment = item.Comment.Length == 0 ? string.Empty : item.Kind switch
        {
            "view" => $"\n\nCOMMENT ON VIEW {name} IS {PgSql.Literal(item.Comment)};",
            "matview" => $"\n\nCOMMENT ON MATERIALIZED VIEW {name} IS {PgSql.Literal(item.Comment)};",
            _ => string.Empty,
        };
        return item.Kind switch
        {
            "view" => $"CREATE OR REPLACE VIEW {name} AS\n{body}{comment}",
            "matview" => $"CREATE MATERIALIZED VIEW {name} AS\n{body}{comment}",
            _ => body.EndsWith(';') ? body : body + ";",
        };
    }

    public static async Task<TableDetail> TableDetailAsync(NpgsqlConnection c, uint oid, CancellationToken ct)
    {
        var id = new NpgsqlParameter { Value = (long)oid };
        async Task<NpgsqlDataReader> Query(string sql)
        {
            var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.Add(id.Clone());
            return await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.Default, ct);
        }

        string schema, name, kind, owner, comment, partKey, partOf, inherits;
        bool unlogged;
        await using (var r = await Query("""
            SELECT n.nspname, c.relname, c.relkind::text, pg_get_userbyid(c.relowner), coalesce(obj_description(c.oid, 'pg_class'), ''),
                   CASE WHEN c.relkind = 'p' THEN 'PARTITION BY ' || pg_get_partkeydef(c.oid) ELSE '' END,
                   CASE WHEN c.relispartition THEN 'PARTITION OF ' || (SELECT i.inhparent::regclass::text FROM pg_inherits i WHERE i.inhrelid = c.oid)
                        || ' ' || pg_get_expr(c.relpartbound, c.oid) ELSE '' END,
                   coalesce((SELECT string_agg(i.inhparent::regclass::text, ', ' ORDER BY i.inhseqno) FROM pg_inherits i
                              WHERE i.inhrelid = c.oid AND NOT c.relispartition), ''),
                   c.relpersistence = 'u'
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.oid = $1::oid
            """))
        {
            if (!await r.ReadAsync(ct))
            {
                throw new InvalidOperationException("Bảng không còn trong DB (đã bị xoá?) - bấm Đọc để tải lại.");
            }
            (schema, name, kind, owner, comment, partKey, partOf, inherits, unlogged) =
                (r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetBoolean(8));
        }

        var columns = new List<ColumnInfo>();
        await using (var r = await Query("""
            SELECT a.attnum, a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull,
                   coalesce(CASE WHEN a.attgenerated = '' THEN pg_get_expr(d.adbin, d.adrelid) END, ''),
                   a.attidentity::text,
                   coalesce(CASE WHEN a.attgenerated <> '' THEN pg_get_expr(d.adbin, d.adrelid) END, ''),
                   coalesce(CASE WHEN a.attcollation <> t.typcollation AND a.attcollation <> 0 THEN quote_ident(co.collname) END, ''),
                   coalesce(col_description(a.attrelid, a.attnum), '')
              FROM pg_attribute a
              JOIN pg_type t ON t.oid = a.atttypid
              LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
              LEFT JOIN pg_collation co ON co.oid = a.attcollation
             WHERE a.attrelid = $1::oid AND a.attnum > 0 AND NOT a.attisdropped
             ORDER BY a.attnum
            """))
        {
            while (await r.ReadAsync(ct))
            {
                columns.Add(new ColumnInfo(r.GetInt16(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetString(4), r.GetString(5),
                    r.GetString(6), r.GetString(7), r.GetString(8)));
            }
        }

        var constraints = new List<ConstraintInfo>();
        await using (var r = await Query("SELECT conname, contype::text, pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = $1::oid ORDER BY contype, conname"))
        {
            while (await r.ReadAsync(ct))
            {
                constraints.Add(new ConstraintInfo(r.GetString(0), r.GetString(1), r.GetString(2)));
            }
        }

        var indexes = new List<IndexInfo>();
        await using (var r = await Query("""
            SELECT ic.relname, pg_get_indexdef(i.indexrelid), i.indisunique, i.indisprimary,
                   EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conindid = i.indexrelid AND k.conrelid = i.indrelid AND k.contype IN ('p', 'u', 'x')),
                   pg_relation_size(i.indexrelid)
              FROM pg_index i JOIN pg_class ic ON ic.oid = i.indexrelid WHERE i.indrelid = $1::oid ORDER BY 1
            """))
        {
            while (await r.ReadAsync(ct))
            {
                indexes.Add(new IndexInfo(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetBoolean(3), r.GetBoolean(4), r.GetInt64(5)));
            }
        }

        var triggers = new List<TriggerInfo>();
        await using (var r = await Query("""
            SELECT t.tgname, pg_get_triggerdef(t.oid, true), t.tgenabled::text, t.tgfoid::regproc::text
              FROM pg_trigger t WHERE t.tgrelid = $1::oid AND NOT t.tgisinternal ORDER BY 1
            """))
        {
            while (await r.ReadAsync(ct))
            {
                triggers.Add(new TriggerInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
            }
        }

        var pk = new HashSet<string>(StringComparer.Ordinal);
        await using (var r = await Query("""
            SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
             WHERE i.indrelid = $1::oid AND i.indisprimary
            """))
        {
            while (await r.ReadAsync(ct))
            {
                pk.Add(r.GetString(0));
            }
        }

        var fk = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var r = await Query("""
            SELECT a.attname, k.confrelid::regclass::text || '(' || ra.attname || ')'
              FROM pg_constraint k CROSS JOIN LATERAL unnest(k.conkey, k.confkey) AS u(col, refcol)
              JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = u.col
              JOIN pg_attribute ra ON ra.attrelid = k.confrelid AND ra.attnum = u.refcol
             WHERE k.conrelid = $1::oid AND k.contype = 'f'
            """))
        {
            while (await r.ReadAsync(ct))
            {
                fk.TryAdd(r.GetString(0), r.GetString(1));
            }
        }

        return new TableDetail
        {
            Schema = schema,
            Name = name,
            Kind = kind,
            Owner = owner,
            Comment = comment,
            PartitionKey = partKey,
            PartitionOf = partOf,
            Inherits = inherits,
            Unlogged = unlogged,
            Columns = columns,
            Constraints = constraints,
            Indexes = indexes,
            Triggers = triggers,
            PrimaryKeyColumns = pk,
            ForeignKeyColumns = fk,
        };
    }
}
