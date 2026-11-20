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
        var sql = item.Kind switch
        {
            "view" or "matview" => "SELECT pg_get_viewdef($1::oid, true)",
            "function" or "procedure" => "SELECT pg_get_functiondef($1::oid)",
            "trigger" => "SELECT pg_get_triggerdef($1::oid, true)",
            _ => throw new InvalidOperationException("Không có định nghĩa cho " + item.Kind),
        };
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = (long)item.Oid });
        var body = await cmd.ExecuteScalarAsync(ct) as string ?? string.Empty;
        return FormatDefinition(item, body);
    }

    /// <summary>
    /// Định nghĩa đầy đủ của mọi view / materialized view / hàm / procedure (1 lần đọc) - để tìm chữ trong mã nguồn và dò hàm
    /// nhắc tới 1 bảng. <see cref="DbObjectItem.Definition"/> = đúng chữ hiện ở ô Định nghĩa (số dòng khớp nhau).
    /// </summary>
    public static async Task<List<DbObjectItem>> SourcesAsync(NpgsqlConnection c, CancellationToken ct)
    {
        var result = new List<DbObjectItem>();
        var sql = $"""
            SELECT c.oid, CASE c.relkind WHEN 'm' THEN 'matview' ELSE 'view' END, n.nspname, c.relname,
                   coalesce(obj_description(c.oid, 'pg_class'), ''), pg_get_viewdef(c.oid, true)
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE c.relkind IN ('v', 'm') AND {UserSchemas}
            UNION ALL
            SELECT p.oid, CASE p.prokind WHEN 'p' THEN 'procedure' ELSE 'function' END, n.nspname, p.proname,
                   '(' || pg_get_function_identity_arguments(p.oid) || ')', pg_get_functiondef(p.oid)
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE p.prokind IN ('f', 'p') AND {UserSchemas}
               AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_proc'::regclass AND d.objid = p.oid AND d.deptype = 'e')
             ORDER BY 3, 4
            """;
        await using var cmd = new NpgsqlCommand(sql, c) { CommandTimeout = 120 };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var kind = r.GetString(1);
            var isView = kind is "view" or "matview";
            // Hàm: Info = (tham số) để phân biệt hàm trùng tên (overload); view: Comment để FormatDefinition ghi COMMENT ON
            var item = new DbObjectItem(r.GetFieldValue<uint>(0), kind, r.GetString(2), r.GetString(3), isView ? kind : r.GetString(4), isView ? r.GetString(4) : string.Empty);
            result.Add(item with { Definition = FormatDefinition(item, r.IsDBNull(5) ? string.Empty : r.GetString(5)) });
        }
        return result;
    }

    /// <summary>Bảng đang có trong DB theo tên như ghi trong script (không schema → theo search_path của kết nối); null = chưa có.</summary>
    public static async Task<(uint Oid, string Schema, string Name)?> FindTableAsync(NpgsqlConnection c, string name, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT c.oid, n.nspname, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE c.oid = to_regclass($1) AND c.relkind IN ('r', 'p', 'f')
            """, c);
        cmd.Parameters.Add(new NpgsqlParameter { Value = name });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (r.GetFieldValue<uint>(0), r.GetString(1), r.GetString(2)) : null;
    }

    /// <summary>
    /// Đối tượng phụ thuộc vào bảng <paramref name="oid"/>: view (pg_depend, cả view dựng trên view - tối đa 10 lớp, kèm cột dùng),
    /// khoá ngoại từ bảng khác, trigger, sequence thuộc cột, hàm SQL chuẩn được PostgreSQL ghi nhận; hàm / procedure nhắc tên bảng
    /// trong mã nguồn (<see cref="SourceSearch.FindTable"/> trên <paramref name="sources"/>) + trigger bảng khác gọi các hàm đó.
    /// </summary>
    public static async Task<List<DependentItem>> DependentsAsync(NpgsqlConnection c, uint oid, string schema, string table,
        IReadOnlyList<DbObjectItem> sources, CancellationToken ct)
    {
        var id = new NpgsqlParameter { Value = (long)oid };
        var result = new List<DependentItem>();
        var definitions = sources.ToDictionary(s => (s.Kind is "view" or "matview" ? "v" : "f", s.Oid), s => s.Definition);
        async Task Read(string sql, Action<NpgsqlDataReader> map)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.Add(id.Clone());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                map(r);
            }
        }
        static List<string> Cols(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? [] : [.. r.GetFieldValue<string[]>(i)];

        // View: rule (pg_rewrite) của view phụ thuộc bảng; lặp để ra view dựng trên view
        await Read("""
            WITH RECURSIVE deps(oid, via, depth) AS (
                SELECT DISTINCT r.ev_class, 0::oid, 1
                  FROM pg_depend d JOIN pg_rewrite r ON r.oid = d.objid
                 WHERE d.classid = 'pg_rewrite'::regclass AND d.refclassid = 'pg_class'::regclass AND d.refobjid = $1::oid AND r.ev_class <> $1::oid
                UNION
                SELECT DISTINCT r.ev_class, deps.oid, deps.depth + 1
                  FROM deps JOIN pg_depend d ON d.refobjid = deps.oid AND d.refclassid = 'pg_class'::regclass AND d.classid = 'pg_rewrite'::regclass
                  JOIN pg_rewrite r ON r.oid = d.objid
                 WHERE r.ev_class <> deps.oid AND deps.depth < 10
            )
            SELECT DISTINCT ON (deps.oid) deps.oid, n.nspname || '.' || c.relname, c.relkind::text,
                   CASE WHEN deps.via = 0 THEN '' ELSE deps.via::regclass::text END, deps.depth,
                   (SELECT array_agg(DISTINCT a.attname::text ORDER BY a.attname::text)
                      FROM pg_depend d2 JOIN pg_rewrite r2 ON r2.oid = d2.objid
                      JOIN pg_attribute a ON a.attrelid = d2.refobjid AND a.attnum = d2.refobjsubid
                     WHERE r2.ev_class = deps.oid AND d2.classid = 'pg_rewrite'::regclass AND d2.refobjid = $1::oid AND d2.refobjsubid > 0)
              FROM deps JOIN pg_class c ON c.oid = deps.oid JOIN pg_namespace n ON n.oid = c.relnamespace
             ORDER BY deps.oid, deps.depth
            """, r =>
        {
            var columns = Cols(r, 5);
            var via = r.GetString(3);
            var relation = (r.GetString(2) == "m" ? "materialized view" : "view")
                + (via.Length > 0 ? $" · gián tiếp qua {via}" : columns.Count > 0 ? " · dùng cột " + string.Join(", ", columns) : string.Empty);
            result.Add(new DependentItem(DependentKind.View, r.GetString(1), relation, columns,
                definitions.GetValueOrDefault(("v", r.GetFieldValue<uint>(0))) ?? string.Empty, r.GetInt32(4)));
        });

        await Read("""
            SELECT k.conname, k.conrelid::regclass::text, pg_get_constraintdef(k.oid, true),
                   (SELECT array_agg(a.attname::text ORDER BY a.attnum) FROM pg_attribute a WHERE a.attrelid = k.confrelid AND a.attnum = ANY(k.confkey))
              FROM pg_constraint k WHERE k.confrelid = $1::oid AND k.contype = 'f' AND k.conrelid <> $1::oid ORDER BY 2, 1
            """, r =>
        {
            var columns = Cols(r, 3);
            result.Add(new DependentItem(DependentKind.ForeignKey, $"{r.GetString(1)} ({r.GetString(0)})",
                "khoá ngoại → cột " + string.Join(", ", columns), columns, $"ALTER TABLE {r.GetString(1)} ADD CONSTRAINT {PgSql.QuoteIdentIfNeeded(r.GetString(0))} {r.GetString(2)};"));
        });

        await Read("""
            SELECT t.tgname, t.tgfoid::regproc::text, pg_get_triggerdef(t.oid, true), t.tgenabled::text
              FROM pg_trigger t WHERE t.tgrelid = $1::oid AND NOT t.tgisinternal ORDER BY 1
            """, r => result.Add(new DependentItem(DependentKind.Trigger, r.GetString(0),
                $"trigger trên bảng này → {r.GetString(1)}{(r.GetString(3) == "D" ? " · ĐANG TẮT" : string.Empty)}", [], r.GetString(2) + ";")));

        await Read("""
            SELECT s.oid::regclass::text, a.attname::text
              FROM pg_depend d JOIN pg_class s ON s.oid = d.objid AND s.relkind = 'S'
              JOIN pg_attribute a ON a.attrelid = d.refobjid AND a.attnum = d.refobjsubid
             WHERE d.classid = 'pg_class'::regclass AND d.refclassid = 'pg_class'::regclass AND d.refobjid = $1::oid AND d.deptype IN ('a', 'i')
             ORDER BY 1
            """, r => result.Add(new DependentItem(DependentKind.Sequence, r.GetString(0), $"sequence của cột {r.GetString(1)}", [r.GetString(1)], string.Empty)));

        // Hàm có thân SQL chuẩn (BEGIN ATOMIC): PostgreSQL ghi phụ thuộc như view
        var tracked = new HashSet<uint>();
        await Read("""
            SELECT DISTINCT p.oid, n.nspname || '.' || p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')'
              FROM pg_depend d JOIN pg_proc p ON p.oid = d.objid JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE d.classid = 'pg_proc'::regclass AND d.refclassid = 'pg_class'::regclass AND d.refobjid = $1::oid AND d.deptype = 'n'
             ORDER BY 2
            """, r =>
        {
            tracked.Add(r.GetFieldValue<uint>(0));
            result.Add(new DependentItem(DependentKind.TrackedFunction, r.GetString(1), "hàm SQL chuẩn (BEGIN ATOMIC) - PostgreSQL ghi nhận", [],
                definitions.GetValueOrDefault(("f", r.GetFieldValue<uint>(0))) ?? string.Empty));
        });

        // Hàm / procedure nhắc tên bảng trong mã nguồn (dò chữ)
        var functionNames = new Dictionary<uint, string>();
        foreach (var f in sources.Where(s => s.Kind is "function" or "procedure" && !tracked.Contains(s.Oid)))
        {
            var matches = SourceSearch.FindTable(f.Definition, schema, table);
            if (matches.Count == 0)
            {
                continue;
            }
            var name = $"{f.Schema}.{f.Name}{f.Info}";
            functionNames[f.Oid] = name;
            var how = matches.All(m => m.Qualified) ? "ghi rõ schema" : matches.Any(m => m.Qualified) ? "có chỗ ghi rõ schema" : "theo tên (không ghi schema)";
            result.Add(new DependentItem(DependentKind.Function, name, $"{f.Kind} · {how} · {SourceSearch.Summary(matches)}", [],
                SourceSearch.Lines(matches) + "\n\n" + f.Definition));
        }
        if (functionNames.Count > 0)
        {
            await Read("""
                SELECT t.tgname, t.tgrelid::regclass::text, t.tgfoid, pg_get_triggerdef(t.oid, true)
                  FROM pg_trigger t WHERE NOT t.tgisinternal AND t.tgrelid <> $1::oid ORDER BY 2, 1
                """, r =>
            {
                if (functionNames.TryGetValue(r.GetFieldValue<uint>(2), out var fn))
                {
                    result.Add(new DependentItem(DependentKind.IndirectTrigger, $"{r.GetString(0)} trên {r.GetString(1)}", $"gọi hàm {fn}", [], r.GetString(3) + ";"));
                }
            });
        }
        return result;
    }

    /// <summary>Thân từ pg_get_viewdef / pg_get_functiondef / pg_get_triggerdef → câu CREATE đầy đủ (giống ô Định nghĩa).</summary>
    private static string FormatDefinition(DbObjectItem item, string rawBody)
    {
        var name = $"{PgSql.QuoteIdentIfNeeded(item.Schema)}.{PgSql.QuoteIdentIfNeeded(item.Name)}";
        var body = rawBody.TrimEnd();
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
