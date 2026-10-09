using System;
using System.Collections.Generic;
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
    public async Task<(List<DbTableRecord> Tables, List<DbColumnRecord> Columns, List<DbForeignKeyRecord> ForeignKeys)> ImportAsync(
        SqlServerConnectionSettings settings, AppOptions options, Action<string> log, CancellationToken cancellationToken = default)
    {
        var sourceLabel = $"{BuildDataSource(settings)}/{settings.Database}";
        log($"Dang ket noi {sourceLabel} (toi da {options.EffectiveConnectTimeoutSeconds} giay)...");
        await using var connection = CreateConnection(settings, options);
        await DatabaseConnectException.OpenAsync(connection, log, cancellationToken);

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
                   CAST(ep.value AS nvarchar(max))
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
                log($"Loi doc thong tin cot: {ex.Message}");
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
        catch (Exception ex)
        {
            log($"Loi doc khoa ngoai: {ex.Message}");
            return [];
        }
    }
}
