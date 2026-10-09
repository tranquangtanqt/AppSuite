using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Persists the imported DB-dictionary into a local SQLite file at Data\Database\{databaseName}.db,
/// next to the module executable - same pattern as ModuleB's SavedFolderRepository (plain ADO.NET,
/// no ORM). The file is named after the source database/service actually connected to, so importing
/// from different databases keeps separate caches instead of overwriting each other.
/// </summary>
public sealed class RdbmsHtmlGeneratorDatabase
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public RdbmsHtmlGeneratorDatabase(string databaseName)
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Database");
        Directory.CreateDirectory(dataDirectory);
        DatabasePath = Path.Combine(dataDirectory, $"{SanitizeFileName(databaseName)}.db");
        _connectionString = $"Data Source={DatabasePath}";
    }

    /// <summary>Replaces characters that PostgreSQL/Oracle allow in a database name but Windows
    /// doesn't allow in a file name (rare in practice, but database names are user-entered).</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return sanitized.Length == 0 ? "Rdbms.HtmlGenerator" : sanitized;
    }

    public bool Exists => File.Exists(DatabasePath);

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task ReplaceAllAsync(SchemaImportResult import)
    {
        var (tables, columns, foreignKeys, indexes, constraints) = import;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText =
                """
                DROP TABLE IF EXISTS Constraints;
                DROP TABLE IF EXISTS Indexes;
                DROP TABLE IF EXISTS ForeignKeys;
                DROP TABLE IF EXISTS Columns;
                DROP TABLE IF EXISTS Tables;

                CREATE TABLE Tables (
                  TableName       TEXT PRIMARY KEY,
                  Alias           TEXT,
                  JapaneseName    TEXT,
                  Kind            TEXT,
                  Note            TEXT,
                  Description     TEXT,
                  ManagementType  TEXT,
                  CautionItems    TEXT,
                  RevisionHistory TEXT,
                  SourceFile      TEXT NOT NULL,
                  SourceSheet     TEXT NOT NULL,
                  EstimatedRows   INTEGER
                );

                CREATE TABLE Indexes (
                  Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                  TableName     TEXT NOT NULL REFERENCES Tables(TableName),
                  IndexName     TEXT NOT NULL,
                  Columns       TEXT,
                  IsUnique      INTEGER NOT NULL,
                  IsPrimaryKey  INTEGER NOT NULL,
                  IndexType     TEXT,
                  Note          TEXT
                );

                CREATE TABLE Constraints (
                  Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                  TableName       TEXT NOT NULL REFERENCES Tables(TableName),
                  ConstraintName  TEXT NOT NULL,
                  ConstraintType  TEXT NOT NULL,
                  Definition      TEXT
                );

                CREATE TABLE Columns (
                  Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                  TableName        TEXT NOT NULL REFERENCES Tables(TableName),
                  OrdinalPosition  INTEGER NOT NULL,
                  Level            INTEGER,
                  ColumnName       TEXT NOT NULL,
                  Meta             TEXT,
                  DataType         TEXT,
                  Length           TEXT,
                  Nullable         TEXT,
                  DefaultValue     TEXT,
                  JapaneseName     TEXT,
                  Description      TEXT,
                  FullName         TEXT,
                  ValueRestriction TEXT,
                  IsCommon         INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX idx_columns_tablename ON Columns(TableName);

                CREATE TABLE ForeignKeys (
                  Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                  TableName          TEXT NOT NULL REFERENCES Tables(TableName),
                  OrdinalPosition    INTEGER NOT NULL,
                  LocalColumns       TEXT NOT NULL,
                  ReferencedTable    TEXT NOT NULL,
                  ReferencedColumns  TEXT
                );

                CREATE INDEX idx_foreignkeys_tablename ON ForeignKeys(TableName);
                """;
            await schema.ExecuteNonQueryAsync();
        }

        using (var insertTable = connection.CreateCommand())
        {
            insertTable.Transaction = transaction;
            insertTable.CommandText =
                "INSERT INTO Tables (TableName, Alias, JapaneseName, Kind, Note, Description, " +
                "ManagementType, CautionItems, RevisionHistory, SourceFile, SourceSheet, EstimatedRows) " +
                "VALUES ($tableName, $alias, $japaneseName, $kind, $note, $description, " +
                "$managementType, $cautionItems, $revisionHistory, $sourceFile, $sourceSheet, $estimatedRows)";
            var pTableName = insertTable.Parameters.Add("$tableName", SqliteType.Text);
            var pAlias = insertTable.Parameters.Add("$alias", SqliteType.Text);
            var pJapaneseName = insertTable.Parameters.Add("$japaneseName", SqliteType.Text);
            var pKind = insertTable.Parameters.Add("$kind", SqliteType.Text);
            var pNote = insertTable.Parameters.Add("$note", SqliteType.Text);
            var pDescription = insertTable.Parameters.Add("$description", SqliteType.Text);
            var pManagementType = insertTable.Parameters.Add("$managementType", SqliteType.Text);
            var pCautionItems = insertTable.Parameters.Add("$cautionItems", SqliteType.Text);
            var pRevisionHistory = insertTable.Parameters.Add("$revisionHistory", SqliteType.Text);
            var pSourceFile = insertTable.Parameters.Add("$sourceFile", SqliteType.Text);
            var pSourceSheet = insertTable.Parameters.Add("$sourceSheet", SqliteType.Text);
            var pEstimatedRows = insertTable.Parameters.Add("$estimatedRows", SqliteType.Integer);

            foreach (var table in tables)
            {
                pTableName.Value = table.TableName;
                pAlias.Value = table.Alias;
                pJapaneseName.Value = table.JapaneseName;
                pKind.Value = table.Kind;
                pNote.Value = table.Note;
                pDescription.Value = table.Description;
                pManagementType.Value = table.ManagementType;
                pCautionItems.Value = table.CautionItems;
                pRevisionHistory.Value = table.RevisionHistory;
                pSourceFile.Value = table.SourceFile;
                pSourceSheet.Value = table.SourceSheet;
                pEstimatedRows.Value = (object?)table.EstimatedRows ?? DBNull.Value;
                await insertTable.ExecuteNonQueryAsync();
            }
        }

        using (var insertColumn = connection.CreateCommand())
        {
            insertColumn.Transaction = transaction;
            insertColumn.CommandText =
                "INSERT INTO Columns (TableName, OrdinalPosition, Level, ColumnName, Meta, DataType, Length, " +
                "Nullable, DefaultValue, JapaneseName, Description, FullName, ValueRestriction, IsCommon) " +
                "VALUES ($tableName, $ordinal, $level, $columnName, $meta, $dataType, $length, " +
                "$nullable, $defaultValue, $japaneseName, $description, $fullName, $restriction, $isCommon)";
            var pTableName = insertColumn.Parameters.Add("$tableName", SqliteType.Text);
            var pOrdinal = insertColumn.Parameters.Add("$ordinal", SqliteType.Integer);
            var pLevel = insertColumn.Parameters.Add("$level", SqliteType.Integer);
            var pColumnName = insertColumn.Parameters.Add("$columnName", SqliteType.Text);
            var pMeta = insertColumn.Parameters.Add("$meta", SqliteType.Text);
            var pDataType = insertColumn.Parameters.Add("$dataType", SqliteType.Text);
            var pLength = insertColumn.Parameters.Add("$length", SqliteType.Text);
            var pNullable = insertColumn.Parameters.Add("$nullable", SqliteType.Text);
            var pDefaultValue = insertColumn.Parameters.Add("$defaultValue", SqliteType.Text);
            var pJapaneseName = insertColumn.Parameters.Add("$japaneseName", SqliteType.Text);
            var pDescription = insertColumn.Parameters.Add("$description", SqliteType.Text);
            var pFullName = insertColumn.Parameters.Add("$fullName", SqliteType.Text);
            var pRestriction = insertColumn.Parameters.Add("$restriction", SqliteType.Text);
            var pIsCommon = insertColumn.Parameters.Add("$isCommon", SqliteType.Integer);

            foreach (var column in columns)
            {
                pTableName.Value = column.TableName;
                pOrdinal.Value = column.OrdinalPosition;
                pLevel.Value = (object?)column.Level ?? DBNull.Value;
                pColumnName.Value = column.ColumnName;
                pMeta.Value = column.Meta;
                pDataType.Value = column.DataType;
                pLength.Value = column.Length;
                pNullable.Value = column.Nullable;
                pDefaultValue.Value = column.DefaultValue;
                pJapaneseName.Value = column.JapaneseName;
                pDescription.Value = column.Description;
                pFullName.Value = column.FullName;
                pRestriction.Value = column.ValueRestriction;
                pIsCommon.Value = column.IsCommon ? 1 : 0;
                await insertColumn.ExecuteNonQueryAsync();
            }
        }

        using (var insertForeignKey = connection.CreateCommand())
        {
            insertForeignKey.Transaction = transaction;
            insertForeignKey.CommandText =
                "INSERT INTO ForeignKeys (TableName, OrdinalPosition, LocalColumns, ReferencedTable, ReferencedColumns) " +
                "VALUES ($tableName, $ordinal, $localColumns, $referencedTable, $referencedColumns)";
            var pTableName = insertForeignKey.Parameters.Add("$tableName", SqliteType.Text);
            var pOrdinal = insertForeignKey.Parameters.Add("$ordinal", SqliteType.Integer);
            var pLocalColumns = insertForeignKey.Parameters.Add("$localColumns", SqliteType.Text);
            var pReferencedTable = insertForeignKey.Parameters.Add("$referencedTable", SqliteType.Text);
            var pReferencedColumns = insertForeignKey.Parameters.Add("$referencedColumns", SqliteType.Text);

            foreach (var fk in foreignKeys)
            {
                pTableName.Value = fk.TableName;
                pOrdinal.Value = fk.OrdinalPosition;
                pLocalColumns.Value = fk.LocalColumns;
                pReferencedTable.Value = fk.ReferencedTable;
                pReferencedColumns.Value = fk.ReferencedColumns;
                await insertForeignKey.ExecuteNonQueryAsync();
            }
        }

        using (var insertIndex = connection.CreateCommand())
        {
            insertIndex.Transaction = transaction;
            insertIndex.CommandText =
                "INSERT INTO Indexes (TableName, IndexName, Columns, IsUnique, IsPrimaryKey, IndexType, Note) " +
                "VALUES ($tableName, $indexName, $columns, $isUnique, $isPrimaryKey, $indexType, $note)";
            var pTableName = insertIndex.Parameters.Add("$tableName", SqliteType.Text);
            var pIndexName = insertIndex.Parameters.Add("$indexName", SqliteType.Text);
            var pColumns = insertIndex.Parameters.Add("$columns", SqliteType.Text);
            var pIsUnique = insertIndex.Parameters.Add("$isUnique", SqliteType.Integer);
            var pIsPrimaryKey = insertIndex.Parameters.Add("$isPrimaryKey", SqliteType.Integer);
            var pIndexType = insertIndex.Parameters.Add("$indexType", SqliteType.Text);
            var pNote = insertIndex.Parameters.Add("$note", SqliteType.Text);

            foreach (var index in indexes)
            {
                pTableName.Value = index.TableName;
                pIndexName.Value = index.IndexName;
                pColumns.Value = index.Columns;
                pIsUnique.Value = index.IsUnique ? 1 : 0;
                pIsPrimaryKey.Value = index.IsPrimaryKey ? 1 : 0;
                pIndexType.Value = index.IndexType;
                pNote.Value = index.Note;
                await insertIndex.ExecuteNonQueryAsync();
            }
        }

        using (var insertConstraint = connection.CreateCommand())
        {
            insertConstraint.Transaction = transaction;
            insertConstraint.CommandText =
                "INSERT INTO Constraints (TableName, ConstraintName, ConstraintType, Definition) " +
                "VALUES ($tableName, $constraintName, $constraintType, $definition)";
            var pTableName = insertConstraint.Parameters.Add("$tableName", SqliteType.Text);
            var pConstraintName = insertConstraint.Parameters.Add("$constraintName", SqliteType.Text);
            var pConstraintType = insertConstraint.Parameters.Add("$constraintType", SqliteType.Text);
            var pDefinition = insertConstraint.Parameters.Add("$definition", SqliteType.Text);

            foreach (var constraint in constraints)
            {
                pTableName.Value = constraint.TableName;
                pConstraintName.Value = constraint.ConstraintName;
                pConstraintType.Value = constraint.ConstraintType;
                pDefinition.Value = constraint.Definition;
                await insertConstraint.ExecuteNonQueryAsync();
            }
        }

        transaction.Commit();
    }

    /// <summary>A .db written before indexes / constraints / row estimates existed lacks those tables / columns -
    /// it still exports, just without that information (re-read the database to get it).</summary>
    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $column";
        command.Parameters.AddWithValue("$table", table);
        command.Parameters.AddWithValue("$column", column);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>False for a .db read by an older version (no index / constraint / row-estimate data).</summary>
    public bool HasExtendedInfo()
    {
        using var connection = OpenConnection();
        return HasColumn(connection, "Tables", "EstimatedRows");
    }

    public List<DbIndexRecord> GetAllIndexes()
    {
        using var connection = OpenConnection();
        if (!HasColumn(connection, "Indexes", "IndexName"))
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TableName, IndexName, Columns, IsUnique, IsPrimaryKey, IndexType, Note FROM Indexes ORDER BY Id";

        var result = new List<DbIndexRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DbIndexRecord
            {
                TableName = reader.GetString(0),
                IndexName = reader.GetString(1),
                Columns = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                IsUnique = reader.GetInt32(3) != 0,
                IsPrimaryKey = reader.GetInt32(4) != 0,
                IndexType = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Note = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            });
        }

        return result;
    }

    public List<DbConstraintRecord> GetAllConstraints()
    {
        using var connection = OpenConnection();
        if (!HasColumn(connection, "Constraints", "ConstraintName"))
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TableName, ConstraintName, ConstraintType, Definition FROM Constraints ORDER BY Id";

        var result = new List<DbConstraintRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DbConstraintRecord
            {
                TableName = reader.GetString(0),
                ConstraintName = reader.GetString(1),
                ConstraintType = reader.GetString(2),
                Definition = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            });
        }

        return result;
    }

    public List<DbTableRecord> GetAllTables()
    {
        using var connection = OpenConnection();
        var hasRows = HasColumn(connection, "Tables", "EstimatedRows");
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TableName, Alias, JapaneseName, Kind, Note, Description, " +
            "ManagementType, CautionItems, RevisionHistory, SourceFile, SourceSheet, " +
            (hasRows ? "EstimatedRows " : "NULL ") +
            "FROM Tables ORDER BY TableName";

        var result = new List<DbTableRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DbTableRecord
            {
                TableName = reader.GetString(0),
                Alias = reader.GetString(1),
                JapaneseName = reader.GetString(2),
                Kind = reader.GetString(3),
                Note = reader.GetString(4),
                Description = reader.GetString(5),
                ManagementType = reader.GetString(6),
                CautionItems = reader.GetString(7),
                RevisionHistory = reader.GetString(8),
                SourceFile = reader.GetString(9),
                SourceSheet = reader.GetString(10),
                EstimatedRows = reader.IsDBNull(11) ? null : reader.GetInt64(11),
            });
        }

        return result;
    }

    public List<DbColumnRecord> GetAllColumns()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TableName, OrdinalPosition, Level, ColumnName, Meta, DataType, Length, Nullable, " +
            "DefaultValue, JapaneseName, Description, FullName, ValueRestriction, IsCommon " +
            "FROM Columns ORDER BY TableName, OrdinalPosition";

        var result = new List<DbColumnRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DbColumnRecord
            {
                TableName = reader.GetString(0),
                OrdinalPosition = reader.GetInt32(1),
                Level = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                ColumnName = reader.GetString(3),
                Meta = reader.GetString(4),
                DataType = reader.GetString(5),
                Length = reader.GetString(6),
                Nullable = reader.GetString(7),
                DefaultValue = reader.GetString(8),
                JapaneseName = reader.GetString(9),
                Description = reader.GetString(10),
                FullName = reader.GetString(11),
                ValueRestriction = reader.GetString(12),
                IsCommon = reader.GetInt32(13) != 0,
            });
        }

        return result;
    }

    public List<DbForeignKeyRecord> GetAllForeignKeys()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TableName, OrdinalPosition, LocalColumns, ReferencedTable, ReferencedColumns " +
            "FROM ForeignKeys ORDER BY TableName, OrdinalPosition";

        var result = new List<DbForeignKeyRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DbForeignKeyRecord
            {
                TableName = reader.GetString(0),
                OrdinalPosition = reader.GetInt32(1),
                LocalColumns = reader.GetString(2),
                ReferencedTable = reader.GetString(3),
                ReferencedColumns = reader.GetString(4),
            });
        }

        return result;
    }
}
