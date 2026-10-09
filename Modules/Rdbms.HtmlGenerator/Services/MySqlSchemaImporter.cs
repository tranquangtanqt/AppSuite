using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Reads table/column/foreign-key metadata of one MySQL / MariaDB database out of information_schema into the
/// same DbTableRecord/DbColumnRecord/DbForeignKeyRecord shape the other importers produce. In MySQL a schema is
/// the database itself, so <see cref="MySqlConnectionSettings.Database"/> doubles as the schema filter.
/// </summary>
public sealed class MySqlSchemaImporter
{
    public async Task<(List<DbTableRecord> Tables, List<DbColumnRecord> Columns, List<DbForeignKeyRecord> ForeignKeys)> ImportAsync(
        MySqlConnectionSettings settings, AppOptions options, Action<string> log, CancellationToken cancellationToken = default)
    {
        var sourceLabel = $"{settings.Host}:{settings.Port}/{settings.Database}";
        log($"Dang ket noi {sourceLabel} (toi da {options.EffectiveConnectTimeoutSeconds} giay)...");
        await using var connection = CreateConnection(settings, options);
        await DatabaseConnectException.OpenAsync(connection, log, cancellationToken);

        var schema = settings.Database.Trim();
        var tables = await ReadTablesAsync(connection, schema, sourceLabel, log, cancellationToken);
        var columns = await ReadColumnsAsync(connection, schema, log, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, schema, log, cancellationToken);

        log($"Da doc {tables.Count} bang, {columns.Count} cot, {foreignKeys.Count} khoa ngoai tu {sourceLabel}.");
        return (tables, columns, foreignKeys);
    }

    /// <summary>"Thu ket noi": opens a connection and counts the tables/views an import would read.</summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(
        MySqlConnectionSettings settings, AppOptions options, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(settings, options);
        await DatabaseConnectException.OpenAsync(connection, _ => { }, cancellationToken);

        var schema = settings.Database.Trim();
        await using var command = CreateCommand(connection,
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = @schema", schema);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return new ConnectionTestResult($"MySQL {connection.ServerVersion}", count, $"database {schema}");
    }

    private static MySqlConnection CreateConnection(MySqlConnectionSettings settings, AppOptions options)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            Port = (uint)Math.Clamp(settings.Port, 1, 65535),
            Database = settings.Database,
            UserID = settings.Username,
            Password = settings.Password,
            ConnectionTimeout = (uint)options.EffectiveConnectTimeoutSeconds,
            DefaultCommandTimeout = (uint)options.EffectiveCommandTimeoutSeconds,
        };
        return new MySqlConnection(builder.ConnectionString);
    }

    private static MySqlCommand CreateCommand(MySqlConnection connection, string sql, string schema)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@schema", schema);
        return command;
    }

    private static async Task<List<DbTableRecord>> ReadTablesAsync(
        MySqlConnection connection, string schema, string sourceLabel, Action<string> log, CancellationToken ct)
    {
        const string sql =
            """
            SELECT TABLE_NAME, TABLE_TYPE, TABLE_COMMENT
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @schema
            ORDER BY TABLE_NAME
            """;

        var tables = new List<DbTableRecord>();
        await using var command = CreateCommand(connection, sql, schema);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            try
            {
                var kind = reader.GetString(1);
                var comment = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                tables.Add(new DbTableRecord
                {
                    TableName = reader.GetString(0),
                    Kind = kind,
                    // MySQL fills TABLE_COMMENT of every view with the literal "VIEW" - not a real description.
                    Description = kind == "VIEW" && comment == "VIEW" ? string.Empty : comment,
                    SourceFile = sourceLabel,
                    SourceSheet = schema,
                });
            }
            catch (Exception ex)
            {
                log($"Loi doc thong tin bang: {ex.Message}");
            }
        }

        return tables;
    }

    private static async Task<List<DbColumnRecord>> ReadColumnsAsync(
        MySqlConnection connection, string schema, Action<string> log, CancellationToken ct)
    {
        // COLUMN_TYPE already carries length/precision/unsigned, e.g. "varchar(100)", "decimal(10,2) unsigned";
        // COLUMN_KEY = 'PRI' marks primary-key columns, so no separate constraint query is needed.
        const string sql =
            """
            SELECT TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION, COLUMN_TYPE, IS_NULLABLE,
                   COLUMN_DEFAULT, COLUMN_COMMENT, COLUMN_KEY
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @schema
            ORDER BY TABLE_NAME, ORDINAL_POSITION
            """;

        var columns = new List<DbColumnRecord>();
        await using var command = CreateCommand(connection, sql, schema);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            try
            {
                columns.Add(new DbColumnRecord
                {
                    TableName = reader.GetString(0),
                    ColumnName = reader.GetString(1),
                    OrdinalPosition = Convert.ToInt32(reader.GetValue(2)),
                    DataType = reader.GetString(3),
                    Nullable = reader.GetString(4) == "YES" ? "Y" : "N",
                    DefaultValue = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    Description = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Level = !reader.IsDBNull(7) && reader.GetString(7) == "PRI" ? 0 : 1,
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
        MySqlConnection connection, string schema, Action<string> log, CancellationToken ct)
    {
        try
        {
            const string sql =
                """
                SELECT TABLE_NAME, CONSTRAINT_NAME, COLUMN_NAME, ORDINAL_POSITION,
                       REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
                FROM information_schema.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = @schema AND REFERENCED_TABLE_NAME IS NOT NULL
                ORDER BY TABLE_NAME, CONSTRAINT_NAME, ORDINAL_POSITION
                """;

            var rows = new List<(string TableName, string ConstraintName, string LocalColumn, string RefTable, string RefColumn)>();
            await using var command = CreateCommand(connection, sql, schema);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(4), reader.GetString(5)));
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
