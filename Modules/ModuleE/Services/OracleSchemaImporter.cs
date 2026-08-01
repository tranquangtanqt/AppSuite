using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModuleE.Models;
using Oracle.ManagedDataAccess.Client;

namespace ModuleE.Services;

/// <summary>
/// Reads table/column/foreign-key metadata out of Oracle's data dictionary (ALL_TABLES/ALL_VIEWS,
/// ALL_TAB_COLUMNS, ALL_TAB_COMMENTS/ALL_COL_COMMENTS, ALL_CONSTRAINTS/ALL_CONS_COLUMNS) into the
/// same DbTableRecord/DbColumnRecord/DbForeignKeyRecord shape PostgresSchemaImporter produces, so
/// ModuleEDatabase and HtmlReportGenerator are reused unmodified regardless of source database.
/// </summary>
public sealed class OracleSchemaImporter
{
    public async Task<(List<DbTableRecord> Tables, List<DbColumnRecord> Columns, List<DbForeignKeyRecord> ForeignKeys)> ImportAsync(
        OracleConnectionSettings settings, Action<string> log, CancellationToken cancellationToken = default)
    {
        var dataSource = BuildDataSource(settings);
        var builder = new OracleConnectionStringBuilder
        {
            DataSource = dataSource,
            UserID = settings.Username,
            Password = settings.Password,
        };

        var owner = string.IsNullOrWhiteSpace(settings.Schema)
            ? settings.Username.ToUpperInvariant()
            : settings.Schema.Trim().ToUpperInvariant();

        var sourceLabel = settings.ConnectBySid
            ? $"{settings.Host}:{settings.Port}:{settings.Sid}"
            : $"{settings.Host}:{settings.Port}/{settings.ServiceName}";

        log($"Dang ket noi {sourceLabel} (schema {owner})...");
        await using var connection = new OracleConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var tables = await ReadTablesAsync(connection, owner, sourceLabel, log, cancellationToken);
        var primaryKeys = await ReadPrimaryKeysAsync(connection, owner, cancellationToken);
        var columns = await ReadColumnsAsync(connection, owner, primaryKeys, log, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, owner, log, cancellationToken);

        log($"Da doc {tables.Count} bang, {columns.Count} cot, {foreignKeys.Count} khoa ngoai tu {sourceLabel}.");
        return (tables, columns, foreignKeys);
    }

    /// <summary>SID has no EZ Connect shorthand (that syntax only carries a service name), so SID
    /// mode builds the long-form connect descriptor instead of "host:port/name".</summary>
    private static string BuildDataSource(OracleConnectionSettings settings)
    {
        if (settings.ConnectBySid)
        {
            return $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={settings.Host})(PORT={settings.Port}))" +
                   $"(CONNECT_DATA=(SID={settings.Sid})))";
        }

        return $"{settings.Host}:{settings.Port}/{settings.ServiceName}";
    }

    private static OracleCommand CreateCommand(OracleConnection connection, string sql, string owner)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new OracleParameter("owner", owner));
        return command;
    }

    private static async Task<List<DbTableRecord>> ReadTablesAsync(
        OracleConnection connection, string owner, string sourceLabel, Action<string> log, CancellationToken ct)
    {
        const string sql =
            """
            SELECT t.TABLE_NAME, 'BASE TABLE' AS OBJ_TYPE, c.COMMENTS
            FROM ALL_TABLES t
            LEFT JOIN ALL_TAB_COMMENTS c ON c.OWNER = t.OWNER AND c.TABLE_NAME = t.TABLE_NAME
            WHERE t.OWNER = :owner
            UNION ALL
            SELECT v.VIEW_NAME, 'VIEW' AS OBJ_TYPE, c.COMMENTS
            FROM ALL_VIEWS v
            LEFT JOIN ALL_TAB_COMMENTS c ON c.OWNER = v.OWNER AND c.TABLE_NAME = v.VIEW_NAME
            WHERE v.OWNER = :owner
            ORDER BY 1
            """;

        var tables = new List<DbTableRecord>();
        await using var command = CreateCommand(connection, sql, owner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            try
            {
                tables.Add(new DbTableRecord
                {
                    TableName = reader.GetString(0),
                    Kind = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    SourceFile = sourceLabel,
                    SourceSheet = owner,
                });
            }
            catch (Exception ex)
            {
                log($"Loi doc thong tin bang: {ex.Message}");
            }
        }

        return tables;
    }

    private static async Task<HashSet<(string Table, string Column)>> ReadPrimaryKeysAsync(
        OracleConnection connection, string owner, CancellationToken ct)
    {
        const string sql =
            """
            SELECT acc.TABLE_NAME, acc.COLUMN_NAME
            FROM ALL_CONSTRAINTS ac
            JOIN ALL_CONS_COLUMNS acc ON acc.OWNER = ac.OWNER AND acc.CONSTRAINT_NAME = ac.CONSTRAINT_NAME
            WHERE ac.CONSTRAINT_TYPE = 'P' AND ac.OWNER = :owner
            """;

        var result = new HashSet<(string, string)>();
        await using var command = CreateCommand(connection, sql, owner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }

        return result;
    }

    private static async Task<List<DbColumnRecord>> ReadColumnsAsync(
        OracleConnection connection,
        string owner,
        HashSet<(string Table, string Column)> primaryKeys,
        Action<string> log,
        CancellationToken ct)
    {
        const string sql =
            """
            SELECT c.TABLE_NAME, c.COLUMN_NAME, c.COLUMN_ID, c.DATA_TYPE,
                   c.DATA_LENGTH, c.DATA_PRECISION, c.DATA_SCALE,
                   c.NULLABLE, c.DATA_DEFAULT, cc.COMMENTS
            FROM ALL_TAB_COLUMNS c
            LEFT JOIN ALL_COL_COMMENTS cc
                ON cc.OWNER = c.OWNER AND cc.TABLE_NAME = c.TABLE_NAME AND cc.COLUMN_NAME = c.COLUMN_NAME
            WHERE c.OWNER = :owner
            ORDER BY c.TABLE_NAME, c.COLUMN_ID
            """;

        var columns = new List<DbColumnRecord>();
        await using var command = CreateCommand(connection, sql, owner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            try
            {
                var tableName = reader.GetString(0);
                var columnName = reader.GetString(1);
                var isPrimaryKey = primaryKeys.Contains((tableName, columnName));

                var dataType = reader.GetString(3);
                var dataLength = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var dataPrecision = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var dataScale = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);

                columns.Add(new DbColumnRecord
                {
                    TableName = tableName,
                    OrdinalPosition = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    Level = isPrimaryKey ? 0 : 1,
                    ColumnName = columnName,
                    DataType = FormatDataType(dataType, dataLength, dataPrecision, dataScale),
                    Nullable = reader.IsDBNull(7) ? "Y" : (reader.GetString(7) == "Y" ? "Y" : "N"),
                    DefaultValue = reader.IsDBNull(8) ? string.Empty : reader.GetString(8).Trim(),
                    Description = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                });
            }
            catch (Exception ex)
            {
                log($"Loi doc thong tin cot: {ex.Message}");
            }
        }

        return columns;
    }

    /// <summary>Builds a display type string similar to Postgres' format_type(), e.g. "VARCHAR2(100)",
    /// "NUMBER(10,2)", "DATE" - so the HTML report's "Kieu" column reads the same regardless of source.</summary>
    private static string FormatDataType(string dataType, int? length, int? precision, int? scale)
    {
        var isCharType = dataType is "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" or "RAW";
        if (isCharType && length is > 0)
        {
            return $"{dataType}({length})";
        }

        if (dataType == "NUMBER" && precision is > 0)
        {
            return scale is > 0 ? $"NUMBER({precision},{scale})" : $"NUMBER({precision})";
        }

        return dataType;
    }

    private static async Task<List<DbForeignKeyRecord>> ReadForeignKeysAsync(
        OracleConnection connection, string owner, Action<string> log, CancellationToken ct)
    {
        var foreignKeys = new List<DbForeignKeyRecord>();
        try
        {
            const string sql =
                """
                SELECT ac.TABLE_NAME, ac.CONSTRAINT_NAME, acc.COLUMN_NAME AS LOCAL_COLUMN, acc.POSITION,
                       rac.TABLE_NAME AS REF_TABLE, racc.COLUMN_NAME AS REF_COLUMN
                FROM ALL_CONSTRAINTS ac
                JOIN ALL_CONS_COLUMNS acc
                    ON acc.OWNER = ac.OWNER AND acc.CONSTRAINT_NAME = ac.CONSTRAINT_NAME
                JOIN ALL_CONSTRAINTS rac
                    ON rac.OWNER = ac.R_OWNER AND rac.CONSTRAINT_NAME = ac.R_CONSTRAINT_NAME
                JOIN ALL_CONS_COLUMNS racc
                    ON racc.OWNER = rac.OWNER AND racc.CONSTRAINT_NAME = rac.CONSTRAINT_NAME
                       AND racc.POSITION = acc.POSITION
                WHERE ac.CONSTRAINT_TYPE = 'R' AND ac.OWNER = :owner
                ORDER BY ac.TABLE_NAME, ac.CONSTRAINT_NAME, acc.POSITION
                """;

            var rows = new List<(string TableName, string ConstraintName, string LocalColumn, string RefTable, string RefColumn)>();
            await using var command = CreateCommand(connection, sql, owner);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(4), reader.GetString(5)));
            }

            var ordinal = 0;
            foreach (var group in rows.GroupBy(r => (r.TableName, r.ConstraintName)))
            {
                var groupRows = group.ToList();
                foreignKeys.Add(new DbForeignKeyRecord
                {
                    TableName = group.Key.TableName,
                    OrdinalPosition = ordinal++,
                    LocalColumns = string.Join(",", groupRows.Select(r => r.LocalColumn)),
                    ReferencedTable = groupRows[0].RefTable,
                    ReferencedColumns = string.Join(",", groupRows.Select(r => r.RefColumn)),
                });
            }
        }
        catch (Exception ex)
        {
            log($"Loi doc khoa ngoai: {ex.Message}");
        }

        return foreignKeys;
    }
}
