using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Reads table/column/foreign-key metadata out of SQL Server's catalog views (sys.objects / sys.columns /
/// sys.foreign_keys; descriptions from the MS_Description extended property, which is what SSMS writes)
/// into the same DbTableRecord/DbColumnRecord/DbForeignKeyRecord shape the other importers produce.
/// </summary>
public sealed class SqlServerSchemaImporter
{
    public async Task<SchemaImportResult> ImportAsync(
        SqlServerConnectionSettings settings, AppOptions options, Action<string> log, CancellationToken cancellationToken = default)
    {
        var sourceLabel = $"{BuildDataSource(settings)}/{settings.Database}";
        log($"Đang kết nối {sourceLabel} (tối đa {options.EffectiveConnectTimeoutSeconds} giây)...");
        await using var connection = CreateConnection(settings, options);
        await DatabaseConnectException.OpenAsync(connection, log, cancellationToken);

        var schemaFilter = string.IsNullOrWhiteSpace(settings.Schema) ? null : settings.Schema.Trim();
        var tables = await ReadTablesAsync(connection, schemaFilter, sourceLabel, log, cancellationToken);
        var primaryKeys = await ReadPrimaryKeysAsync(connection, schemaFilter, cancellationToken);
        var columns = await ReadColumnsAsync(connection, schemaFilter, primaryKeys, log, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, schemaFilter, log, cancellationToken);
        var indexes = await ReadIndexesAsync(connection, schemaFilter, log, cancellationToken);
        var constraints = await ReadConstraintsAsync(connection, schemaFilter, log, cancellationToken);

        log($"Đã đọc {tables.Count} bảng, {columns.Count} cột, {foreignKeys.Count} khoá ngoại, {indexes.Count} index, " +
            $"{constraints.Count} ràng buộc UNIQUE / CHECK từ {sourceLabel}.");
        return new SchemaImportResult(tables, columns, foreignKeys, indexes, constraints);
    }

    /// <summary>"Thu ket noi": opens a connection and counts the tables/views an import would read.</summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(
        SqlServerConnectionSettings settings, AppOptions options, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(settings, options);
        await DatabaseConnectException.OpenAsync(connection, _ => { }, cancellationToken);

        var schemaFilter = string.IsNullOrWhiteSpace(settings.Schema) ? null : settings.Schema.Trim();
        const string sql =
            """
            SELECT COUNT(*) FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0 AND {schemaClause}
            """;
        await using var command = CreateCommand(connection, sql, schemaFilter);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return new ConnectionTestResult($"SQL Server {connection.ServerVersion}", count,
            schemaFilter is null ? "mọi schema" : $"schema {schemaFilter}");
    }

    private static SqlConnection CreateConnection(SqlServerConnectionSettings settings, AppOptions options)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = BuildDataSource(settings),
            InitialCatalog = settings.Database,
            IntegratedSecurity = settings.UseWindowsAuthentication,
            ConnectTimeout = options.EffectiveConnectTimeoutSeconds,
            CommandTimeout = options.EffectiveCommandTimeoutSeconds,
            // SqlClient 4+ encrypts by default and rejects the self-signed certificate most internal servers
            // use - trust it, the same as ticking "Trust server certificate" in SSMS.
            TrustServerCertificate = true,
            ApplicationName = "Rdbms.HtmlGenerator",
        };
        if (!settings.UseWindowsAuthentication)
        {
            builder.UserID = settings.Username;
            builder.Password = settings.Password;
        }

        return new SqlConnection(builder.ConnectionString);
    }

    /// <summary>"host,port" - except a named instance ("server\SQLEXPRESS"), whose port SQL Browser resolves.</summary>
    public static string BuildDataSource(SqlServerConnectionSettings settings) =>
        settings.Host.Contains('\\') || settings.Port <= 0 ? settings.Host : $"{settings.Host},{settings.Port}";

    /// <summary>Returns the schema-filter SQL fragment and adds @schema only when a filter is set.
    /// No filter = every user schema (system objects are already excluded via is_ms_shipped).</summary>
    private static SqlCommand CreateCommand(SqlConnection connection, string sqlTemplate, string? schemaFilter)
    {
        var command = connection.CreateCommand();
        if (schemaFilter is null)
        {
            command.CommandText = sqlTemplate.Replace("{schemaClause}", "1 = 1");
        }
        else
        {
            command.CommandText = sqlTemplate.Replace("{schemaClause}", "s.name = @schema");
            command.Parameters.AddWithValue("@schema", schemaFilter);
        }

        return command;
    }

    private static async Task<List<DbTableRecord>> ReadTablesAsync(
        SqlConnection connection, string? schemaFilter, string sourceLabel, Action<string> log, CancellationToken ct)
    {
        const string sql =
            """
            SELECT s.name, o.name, CASE o.type WHEN 'V' THEN 'VIEW' ELSE 'BASE TABLE' END,
                   CAST(ep.value AS nvarchar(max)),
                   -- heap (0) or clustered index (1) rows = the table's rows; kept current by the engine, no COUNT(*)
                   CASE o.type WHEN 'U' THEN (SELECT SUM(p.rows) FROM sys.partitions p
                                              WHERE p.object_id = o.object_id AND p.index_id IN (0, 1)) END
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            LEFT JOIN sys.extended_properties ep
                ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
            WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0 AND {schemaClause}
            ORDER BY s.name, o.name
            """;

        var tables = new List<DbTableRecord>();
        await using var command = CreateCommand(connection, sql, schemaFilter);
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
                    EstimatedRows = reader.IsDBNull(4) ? null : Convert.ToInt64(reader.GetValue(4)),
                    SourceFile = sourceLabel,
                    SourceSheet = reader.GetString(0),
                });
            }
            catch (Exception ex)
            {
                log($"Lỗi đọc thông tin bảng: {ex.Message}");
            }
        }

        return tables;
    }

    private static async Task<HashSet<(string Schema, string Table, string Column)>> ReadPrimaryKeysAsync(
        SqlConnection connection, string? schemaFilter, CancellationToken ct)
    {
        const string sql =
            """
            SELECT s.name, o.name, c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.objects o ON o.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE i.is_primary_key = 1 AND {schemaClause}
            """;

        var result = new HashSet<(string, string, string)>();
        await using var command = CreateCommand(connection, sql, schemaFilter);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return result;
    }

    private static async Task<List<DbColumnRecord>> ReadColumnsAsync(
        SqlConnection connection,
        string? schemaFilter,
        HashSet<(string Schema, string Table, string Column)> primaryKeys,
        Action<string> log,
        CancellationToken ct)
    {
        const string sql =
            """
            SELECT s.name, o.name, c.name, c.column_id, ty.name,
                   c.max_length, c.precision, c.scale, c.is_nullable,
                   OBJECT_DEFINITION(c.default_object_id), CAST(ep.value AS nvarchar(max))
            FROM sys.columns c
            JOIN sys.objects o ON o.object_id = c.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.extended_properties ep
                ON ep.class = 1 AND ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
            WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0 AND {schemaClause}
            ORDER BY s.name, o.name, c.column_id
            """;

        var columns = new List<DbColumnRecord>();
        await using var command = CreateCommand(connection, sql, schemaFilter);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            try
            {
                var schemaName = reader.GetString(0);
                var tableName = reader.GetString(1);
                var columnName = reader.GetString(2);

                columns.Add(new DbColumnRecord
                {
                    TableName = tableName,
                    Schema = schemaName,
                    OrdinalPosition = reader.GetInt32(3),
                    Level = primaryKeys.Contains((schemaName, tableName, columnName)) ? 0 : 1,
                    ColumnName = columnName,
                    DataType = FormatDataType(reader.GetString(4), reader.GetInt16(5), reader.GetByte(6), reader.GetByte(7)),
                    Nullable = reader.GetBoolean(8) ? "Y" : "N",
                    DefaultValue = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                    Description = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                });
            }
            catch (Exception ex)
            {
                log($"Lỗi đọc thông tin cột: {ex.Message}");
            }
        }

        return columns;
    }

    /// <summary>Display type like SSMS shows it: "nvarchar(50)" (max_length is in bytes, so halved for
    /// n-types), "varchar(max)", "decimal(10,2)", "datetime2(7)"; everything else just the type name.</summary>
    private static string FormatDataType(string type, short maxLength, byte precision, byte scale)
    {
        switch (type)
        {
            case "varchar" or "char" or "varbinary" or "binary":
                return maxLength == -1 ? $"{type}(max)" : $"{type}({maxLength})";
            case "nvarchar" or "nchar":
                return maxLength == -1 ? $"{type}(max)" : $"{type}({maxLength / 2})";
            case "decimal" or "numeric":
                return $"{type}({precision},{scale})";
            case "datetime2" or "time" or "datetimeoffset":
                return $"{type}({scale})";
            default:
                return type;
        }
    }

    private static async Task<List<DbForeignKeyRecord>> ReadForeignKeysAsync(
        SqlConnection connection, string? schemaFilter, Action<string> log, CancellationToken ct)
    {
        try
        {
            const string sql =
                """
                SELECT tp.name, fk.name, cp.name, fkc.constraint_column_id, tr.name, cr.name, s.name, rs.name
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                JOIN sys.objects tp ON tp.object_id = fkc.parent_object_id
                JOIN sys.schemas s ON s.schema_id = tp.schema_id
                JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
                JOIN sys.objects tr ON tr.object_id = fkc.referenced_object_id
                JOIN sys.schemas rs ON rs.schema_id = tr.schema_id
                JOIN sys.columns cr ON cr.object_id = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id
                WHERE {schemaClause}
                ORDER BY s.name, tp.name, fk.name, fkc.constraint_column_id
                """;

            var rows = new List<ForeignKeyColumnRow>();
            await using var command = CreateCommand(connection, sql, schemaFilter);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new ForeignKeyColumnRow(reader.GetString(6), reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(7), reader.GetString(4), reader.GetString(5)));
            }

            return ForeignKeyGrouper.Group(rows);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"Lỗi đọc khoá ngoại: {ex.Message}");
            return [];
        }
    }

    /// <summary>One row per index column, grouped here (STRING_AGG needs SQL Server 2017+). Key columns in
    /// key_ordinal order (columnstore indexes have none - index_column_id order), INCLUDE columns and the filter
    /// of a filtered index go to Note.</summary>
    private static async Task<List<DbIndexRecord>> ReadIndexesAsync(
        SqlConnection connection, string? schemaFilter, Action<string> log, CancellationToken ct)
    {
        try
        {
            const string sql =
                """
                SELECT s.name, o.name, i.name, i.is_unique, i.is_primary_key, i.type_desc, i.filter_definition,
                       c.name, ic.is_included_column, ic.is_descending_key
                FROM sys.indexes i
                JOIN sys.objects o ON o.object_id = i.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.type > 0 AND i.is_hypothetical = 0 AND o.is_ms_shipped = 0 AND o.type IN ('U', 'V')
                  AND {schemaClause}
                ORDER BY s.name, o.name, i.is_primary_key DESC, i.name, ic.is_included_column,
                         CASE WHEN ic.key_ordinal = 0 THEN ic.index_column_id ELSE ic.key_ordinal END
                """;

            var rows = new List<(string Schema, string Table, string Index, bool Unique, bool Pk, string Type, string? Filter,
                string Column, bool Included, bool Descending)>();
            await using var command = CreateCommand(connection, sql, schemaFilter);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
                    reader.GetBoolean(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7), reader.GetBoolean(8), reader.GetBoolean(9)));
            }

            return rows
                .GroupBy(r => (r.Schema, r.Table, r.Index))
                .Select(g =>
                {
                    var first = g.First();
                    var keys = g.Where(r => !r.Included).Select(r => r.Descending ? $"{r.Column} DESC" : r.Column);
                    var included = g.Where(r => r.Included).Select(r => r.Column).ToList();
                    var notes = new List<string>();
                    if (included.Count > 0)
                    {
                        notes.Add($"INCLUDE ({string.Join(", ", included)})");
                    }

                    if (first.Filter is not null)
                    {
                        notes.Add($"WHERE {first.Filter}");
                    }

                    return new DbIndexRecord
                    {
                        Schema = g.Key.Schema,
                        TableName = g.Key.Table,
                        IndexName = g.Key.Index,
                        IsUnique = first.Unique,
                        IsPrimaryKey = first.Pk,
                        IndexType = first.Type,
                        Columns = string.Join(", ", keys),
                        Note = string.Join("  ", notes),
                    };
                })
                .ToList();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"Lỗi đọc index: {ex.Message}");
            return [];
        }
    }

    private static async Task<List<DbConstraintRecord>> ReadConstraintsAsync(
        SqlConnection connection, string? schemaFilter, Action<string> log, CancellationToken ct)
    {
        try
        {
            // UNIQUE: one row per column of the constraint's backing index; CHECK: one row with its definition.
            const string sql =
                """
                SELECT s.name, o.name, kc.name, 'UNIQUE', c.name, ic.key_ordinal
                FROM sys.key_constraints kc
                JOIN sys.objects o ON o.object_id = kc.parent_object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE kc.type = 'UQ' AND {schemaClause}
                UNION ALL
                SELECT s.name, o.name, cc.name, 'CHECK', cc.definition, 0
                FROM sys.check_constraints cc
                JOIN sys.objects o ON o.object_id = cc.parent_object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE {schemaClause}
                ORDER BY 1, 2, 4 DESC, 3, 6
                """;

            var rows = new List<(string Schema, string Table, string Name, string Type, string Part)>();
            await using var command = CreateCommand(connection, sql, schemaFilter);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
            }

            return rows
                .GroupBy(r => (r.Schema, r.Table, r.Name, r.Type))
                .Select(g => new DbConstraintRecord
                {
                    Schema = g.Key.Schema,
                    TableName = g.Key.Table,
                    ConstraintName = g.Key.Name,
                    ConstraintType = g.Key.Type,
                    Definition = g.Key.Type == "UNIQUE" ? $"({string.Join(", ", g.Select(r => r.Part))})" : g.First().Part,
                })
                .ToList();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"Lỗi đọc ràng buộc UNIQUE / CHECK: {ex.Message}");
            return [];
        }
    }
}
