using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rdbms.HtmlGenerator.Models;
using Npgsql;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Reads table/column/foreign-key metadata straight out of PostgreSQL's system catalogs (pg_catalog
/// for tables/columns/comments - avoids one obj_description() round trip per object;
/// information_schema for constraints, which is portable and already row-per-column) into the same
/// DbTableRecord/DbColumnRecord/DbForeignKeyRecord shape Mcf.DbDef.HtmlGenerator produces from Excel, so
/// RdbmsHtmlGeneratorDatabase and HtmlReportGenerator can be reused unmodified.
/// </summary>
public sealed class PostgresSchemaImporter
{
    public async Task<(List<DbTableRecord> Tables, List<DbColumnRecord> Columns, List<DbForeignKeyRecord> ForeignKeys)> ImportAsync(
        PostgresConnectionSettings settings, AppOptions options, Action<string> log, CancellationToken cancellationToken = default)
    {
        log($"Dang ket noi {settings.Host}:{settings.Port}/{settings.Database} (toi da {options.EffectiveConnectTimeoutSeconds} giay)...");
        await using var connection = CreateConnection(settings, options);
        await DatabaseConnectException.OpenAsync(connection, log, cancellationToken);

        var sourceLabel = $"{settings.Host}:{settings.Port}/{settings.Database}";
        var schemaFilter = string.IsNullOrWhiteSpace(settings.Schema) ? null : settings.Schema.Trim();

        var tables = await ReadTablesAsync(connection, schemaFilter, sourceLabel, log, cancellationToken);
        var primaryKeys = await ReadPrimaryKeysAsync(connection, schemaFilter, cancellationToken);
        var columns = await ReadColumnsAsync(connection, schemaFilter, primaryKeys, log, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, schemaFilter, log, cancellationToken);

        log($"Da doc {tables.Count} bang, {columns.Count} cot, {foreignKeys.Count} khoa ngoai tu {sourceLabel}.");
        return (tables, columns, foreignKeys);
    }

    /// <summary>"Thu ket noi": opens a connection and counts the tables/views an import would read.</summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(
        PostgresConnectionSettings settings, AppOptions options, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(settings, options);
        await DatabaseConnectException.OpenAsync(connection, _ => { }, cancellationToken);

        var schemaFilter = string.IsNullOrWhiteSpace(settings.Schema) ? null : settings.Schema.Trim();
        await using var command = connection.CreateCommand();
        var schemaClause = ApplySchemaFilter(command, schemaFilter, "n.nspname");
        command.CommandText =
            $"""
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'v', 'm', 'p', 'f') AND {schemaClause}
            """;
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return new ConnectionTestResult($"PostgreSQL {connection.ServerVersion}", count,
            schemaFilter is null ? "mọi schema" : $"schema {schemaFilter}");
    }

    private static NpgsqlConnection CreateConnection(PostgresConnectionSettings settings, AppOptions options)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Database,
            Username = settings.Username,
            Password = settings.Password,
            Timeout = options.EffectiveConnectTimeoutSeconds,
            CommandTimeout = options.EffectiveCommandTimeoutSeconds,
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }

    /// <summary>Returns the schema-filter SQL fragment ("n.nspname = @schema" or an exclusion list
    /// for every non-system schema) and applies the parameter to <paramref name="command"/> only when
    /// actually needed - avoids passing DBNull as a typed parameter, which Npgsql can't always infer.</summary>
    private static string ApplySchemaFilter(NpgsqlCommand command, string? schemaFilter, string schemaColumn)
    {
        if (schemaFilter is null)
        {
            return $"{schemaColumn} NOT IN ('pg_catalog', 'information_schema')";
        }

        command.Parameters.AddWithValue("schema", schemaFilter);
        return $"{schemaColumn} = @schema";
    }

    private static async Task<List<DbTableRecord>> ReadTablesAsync(
        NpgsqlConnection connection, string? schemaFilter, string sourceLabel, Action<string> log, CancellationToken ct)
    {
        var tables = new List<DbTableRecord>();
        await using var command = connection.CreateCommand();
        var schemaClause = ApplySchemaFilter(command, schemaFilter, "n.nspname");
        command.CommandText =
            $"""
            SELECT n.nspname AS schema_name, c.relname AS table_name,
                   CASE c.relkind
                       WHEN 'r' THEN 'BASE TABLE' WHEN 'p' THEN 'BASE TABLE' WHEN 'f' THEN 'FOREIGN TABLE'
                       WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE c.relkind::text
                   END AS table_type,
                   d.description
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_description d ON d.objoid = c.oid AND d.objsubid = 0
            WHERE c.relkind IN ('r', 'v', 'm', 'p', 'f') AND {schemaClause}
            ORDER BY n.nspname, c.relname
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            try
            {
                tables.Add(new DbTableRecord
                {
                    TableName = reader.GetString(1),
                    Kind = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    SourceFile = sourceLabel,
                    SourceSheet = reader.GetString(0),
                });
            }
            catch (Exception ex)
            {
                log($"Loi doc thong tin bang: {ex.Message}");
            }
        }

        return tables;
    }

    private static async Task<HashSet<(string Schema, string Table, string Column)>> ReadPrimaryKeysAsync(
        NpgsqlConnection connection, string? schemaFilter, CancellationToken ct)
    {
        var result = new HashSet<(string, string, string)>();
        await using var command = connection.CreateCommand();
        var schemaClause = ApplySchemaFilter(command, schemaFilter, "tc.table_schema");
        command.CommandText =
            $"""
            SELECT tc.table_schema, tc.table_name, kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY' AND {schemaClause}
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return result;
    }

    private static async Task<List<DbColumnRecord>> ReadColumnsAsync(
        NpgsqlConnection connection,
        string? schemaFilter,
        HashSet<(string Schema, string Table, string Column)> primaryKeys,
        Action<string> log,
        CancellationToken ct)
    {
        var columns = new List<DbColumnRecord>();
        await using var command = connection.CreateCommand();
        var schemaClause = ApplySchemaFilter(command, schemaFilter, "n.nspname");
        command.CommandText =
            $"""
            SELECT n.nspname AS schema_name, c.relname AS table_name, a.attname AS column_name,
                   a.attnum AS ordinal, format_type(a.atttypid, a.atttypmod) AS data_type,
                   NOT a.attnotnull AS is_nullable,
                   pg_get_expr(ad.adbin, ad.adrelid) AS column_default,
                   d.description
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
            LEFT JOIN pg_description d ON d.objoid = c.oid AND d.objsubid = a.attnum
            WHERE c.relkind IN ('r', 'v', 'm', 'p', 'f') AND a.attnum > 0 AND NOT a.attisdropped
              AND {schemaClause}
            ORDER BY n.nspname, c.relname, a.attnum
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            try
            {
                var schemaName = reader.GetString(0);
                var tableName = reader.GetString(1);
                var columnName = reader.GetString(2);
                var isPrimaryKey = primaryKeys.Contains((schemaName, tableName, columnName));

                columns.Add(new DbColumnRecord
                {
                    TableName = tableName,
                    Schema = schemaName,
                    OrdinalPosition = reader.GetInt32(3),
                    Level = isPrimaryKey ? 0 : 1,
                    ColumnName = columnName,
                    DataType = reader.GetString(4),
                    Nullable = reader.GetBoolean(5) ? "Y" : "N",
                    DefaultValue = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Description = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                });
            }
            catch (Exception ex)
            {
                log($"Loi doc thong tin cot: {ex.Message}");
            }
        }

        return columns;
    }

    private static async Task<List<DbForeignKeyRecord>> ReadForeignKeysAsync(
        NpgsqlConnection connection, string? schemaFilter, Action<string> log, CancellationToken ct)
    {
        var foreignKeys = new List<DbForeignKeyRecord>();
        try
        {
            await using var command = connection.CreateCommand();
            var schemaClause = ApplySchemaFilter(command, schemaFilter, "n.nspname");
            // pg_constraint keeps the local / referenced column numbers as 2 parallel arrays (conkey / confkey):
            // unnest them together so column i pairs with referenced column i. (information_schema's
            // constraint_column_usage has no position, so joining it paired every column with every other one
            // for a composite key, and constraint names are only unique per table - not per schema.)
            command.CommandText =
                $"""
                SELECT n.nspname, cl.relname, con.conname, la.attname, k.ord,
                       rn.nspname, rcl.relname, ra.attname
                FROM pg_constraint con
                JOIN pg_class cl ON cl.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = cl.relnamespace
                JOIN pg_class rcl ON rcl.oid = con.confrelid
                JOIN pg_namespace rn ON rn.oid = rcl.relnamespace
                CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(lnum, rnum, ord)
                JOIN pg_attribute la ON la.attrelid = con.conrelid AND la.attnum = k.lnum
                JOIN pg_attribute ra ON ra.attrelid = con.confrelid AND ra.attnum = k.rnum
                WHERE con.contype = 'f' AND {schemaClause}
                ORDER BY n.nspname, cl.relname, con.conname, k.ord
                """;

            var rows = new List<ForeignKeyColumnRow>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new ForeignKeyColumnRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(5), reader.GetString(6), reader.GetString(7)));
            }

            foreignKeys = ForeignKeyGrouper.Group(rows);
        }
        catch (Exception ex)
        {
            log($"Loi doc khoa ngoai: {ex.Message}");
        }

        return foreignKeys;
    }
}
