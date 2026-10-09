using System;
using System.Collections.Generic;
using System.Linq;
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
    public async Task<SchemaImportResult> ImportAsync(
        MySqlConnectionSettings settings, AppOptions options, Action<string> log, CancellationToken cancellationToken = default)
    {
        var sourceLabel = $"{settings.Host}:{settings.Port}/{settings.Database}";
        log($"Đang kết nối {sourceLabel} (tối đa {options.EffectiveConnectTimeoutSeconds} giây)...");
        await using var connection = CreateConnection(settings, options);
        await DatabaseConnectException.OpenAsync(connection, log, cancellationToken);

        var schema = settings.Database.Trim();
        var tables = await ReadTablesAsync(connection, schema, sourceLabel, log, cancellationToken);
        var columns = await ReadColumnsAsync(connection, schema, log, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, schema, log, cancellationToken);
        var indexes = await ReadIndexesAsync(connection, schema, log, cancellationToken);
        var constraints = await ReadConstraintsAsync(connection, schema, log, cancellationToken);

        log($"Đã đọc {tables.Count} bảng, {columns.Count} cột, {foreignKeys.Count} khoá ngoại, {indexes.Count} index, " +
            $"{constraints.Count} ràng buộc UNIQUE / CHECK từ {sourceLabel}.");
        return new SchemaImportResult(tables, columns, foreignKeys, indexes, constraints);
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
            SELECT TABLE_NAME, TABLE_TYPE, TABLE_COMMENT, TABLE_ROWS
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
                    // TABLE_ROWS: InnoDB's estimate (exact for MyISAM); NULL for views.
                    EstimatedRows = reader.IsDBNull(3) ? null : Convert.ToInt64(reader.GetValue(3)),
                    SourceFile = sourceLabel,
                    SourceSheet = schema,
                });
            }
            catch (Exception ex)
            {
                log($"Lỗi đọc thông tin bảng: {ex.Message}");
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
                    Schema = schema,
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
                log($"Lỗi đọc thông tin cột: {ex.Message}");
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
                       REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME, REFERENCED_TABLE_SCHEMA
                FROM information_schema.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = @schema AND REFERENCED_TABLE_NAME IS NOT NULL
                ORDER BY TABLE_NAME, CONSTRAINT_NAME, ORDINAL_POSITION
                """;

            var rows = new List<ForeignKeyColumnRow>();
            await using var command = CreateCommand(connection, sql, schema);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new ForeignKeyColumnRow(schema, reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(6), reader.GetString(4), reader.GetString(5)));
            }

            return ForeignKeyGrouper.Group(rows);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"Lỗi đọc khoá ngoại: {ex.Message}");
            return [];
        }
    }

    /// <summary>information_schema.STATISTICS has one row per index column. SUB_PART = prefix length ("name(10)"),
    /// COLLATION 'D' = descending (8.0+), COLUMN_NAME NULL = functional key part (8.0.13+).</summary>
    private static async Task<List<DbIndexRecord>> ReadIndexesAsync(
        MySqlConnection connection, string schema, Action<string> log, CancellationToken ct)
    {
        try
        {
            const string sql =
                """
                SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE, COLUMN_NAME, SUB_PART, COLLATION, INDEX_TYPE
                FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = @schema
                ORDER BY TABLE_NAME, INDEX_NAME = 'PRIMARY' DESC, INDEX_NAME, SEQ_IN_INDEX
                """;

            var rows = new List<(string Table, string Index, bool Unique, string Column, string Type)>();
            await using var command = CreateCommand(connection, sql, schema);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var column = reader.IsDBNull(3) ? "(biểu thức)" : reader.GetString(3);
                if (!reader.IsDBNull(4))
                {
                    column += $"({Convert.ToInt64(reader.GetValue(4))})";
                }

                if (!reader.IsDBNull(5) && reader.GetString(5) == "D")
                {
                    column += " DESC";
                }

                rows.Add((reader.GetString(0), reader.GetString(1), Convert.ToInt64(reader.GetValue(2)) == 0,
                    column, reader.IsDBNull(6) ? string.Empty : reader.GetString(6)));
            }

            return rows
                .GroupBy(r => (r.Table, r.Index))
                .Select(g => new DbIndexRecord
                {
                    Schema = schema,
                    TableName = g.Key.Table,
                    IndexName = g.Key.Index,
                    IsUnique = g.First().Unique,
                    IsPrimaryKey = g.Key.Index == "PRIMARY",
                    IndexType = g.First().Type,
                    Columns = string.Join(", ", g.Select(r => r.Column)),
                })
                .ToList();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"Lỗi đọc index: {ex.Message}");
            return [];
        }
    }

    /// <summary>UNIQUE from TABLE_CONSTRAINTS + KEY_COLUMN_USAGE; CHECK from CHECK_CONSTRAINTS, which only exists
    /// from MySQL 8.0.16 / MariaDB 10.2 - read separately so an older server still gets its UNIQUE list.</summary>
    private static async Task<List<DbConstraintRecord>> ReadConstraintsAsync(
        MySqlConnection connection, string schema, Action<string> log, CancellationToken ct)
    {
        var constraints = new List<DbConstraintRecord>();
        try
        {
            const string sql =
                """
                SELECT tc.TABLE_NAME, tc.CONSTRAINT_NAME,
                       GROUP_CONCAT(k.COLUMN_NAME ORDER BY k.ORDINAL_POSITION SEPARATOR ', ')
                FROM information_schema.TABLE_CONSTRAINTS tc
                JOIN information_schema.KEY_COLUMN_USAGE k
                    ON k.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND k.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
                       AND k.TABLE_NAME = tc.TABLE_NAME
                WHERE tc.TABLE_SCHEMA = @schema AND tc.CONSTRAINT_TYPE = 'UNIQUE'
                GROUP BY tc.TABLE_NAME, tc.CONSTRAINT_NAME
                ORDER BY tc.TABLE_NAME, tc.CONSTRAINT_NAME
                """;

            await using var command = CreateCommand(connection, sql, schema);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                constraints.Add(new DbConstraintRecord
                {
                    Schema = schema,
                    TableName = reader.GetString(0),
                    ConstraintName = reader.GetString(1),
                    ConstraintType = "UNIQUE",
                    Definition = $"({reader.GetString(2)})",
                });
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"Lỗi đọc ràng buộc UNIQUE: {ex.Message}");
        }

        try
        {
            const string sql =
                """
                SELECT tc.TABLE_NAME, cc.CONSTRAINT_NAME, cc.CHECK_CLAUSE
                FROM information_schema.CHECK_CONSTRAINTS cc
                JOIN information_schema.TABLE_CONSTRAINTS tc
                    ON tc.CONSTRAINT_SCHEMA = cc.CONSTRAINT_SCHEMA AND tc.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
                       AND tc.CONSTRAINT_TYPE = 'CHECK'
                WHERE cc.CONSTRAINT_SCHEMA = @schema
                ORDER BY tc.TABLE_NAME, cc.CONSTRAINT_NAME
                """;

            await using var command = CreateCommand(connection, sql, schema);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                constraints.Add(new DbConstraintRecord
                {
                    Schema = schema,
                    TableName = reader.GetString(0),
                    ConstraintName = reader.GetString(1),
                    ConstraintType = "CHECK",
                    Definition = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                });
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"Không đọc được ràng buộc CHECK (cần MySQL 8.0.16 / MariaDB 10.2 trở lên): {ex.Message}");
        }

        return constraints;
    }
}
