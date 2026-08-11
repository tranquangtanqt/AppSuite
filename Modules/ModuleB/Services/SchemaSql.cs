using System;
using Microsoft.Data.Sqlite;

namespace ModuleB.Services;

/// <summary>
/// Shared schema setup/migration for Data\DataFromExcel.db, called by both SavedFolderRepository
/// and FileIndexRepository so either one can run first against a fresh or already-populated
/// database - each step is idempotent and ordered so later steps (indexes/triggers) never run
/// before the columns they reference exist.
/// </summary>
internal static class SchemaSql
{
    private const string CreateBaseTables =
        "CREATE TABLE IF NOT EXISTS SavedFolders (" +
        "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
        "Path TEXT NOT NULL UNIQUE); " +
        "CREATE TABLE IF NOT EXISTS IndexedFiles (" +
        "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
        "RootPath TEXT NOT NULL, " +
        "GroupName TEXT NOT NULL, " +
        "FullPath TEXT NOT NULL UNIQUE, " +
        "FileName TEXT NOT NULL, " +
        "Content TEXT NOT NULL, " +
        "LastWriteTimeUtcTicks INTEGER NOT NULL); " +
        "CREATE INDEX IF NOT EXISTS IX_IndexedFiles_RootPath ON IndexedFiles(RootPath); " +
        "CREATE TABLE IF NOT EXISTS IndexedCells (" +
        "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
        "SheetName TEXT NOT NULL, " +
        "CellReference TEXT NOT NULL, " +
        "RowIndex INTEGER NOT NULL, " +
        "ColumnIndex INTEGER NOT NULL, " +
        "Text TEXT NOT NULL)";

    public static void EnsureSchema(SqliteConnection connection)
    {
        Execute(connection, CreateBaseTables);

        // IndexVersion was added after IndexedFiles already shipped, so existing databases need a
        // migration rather than relying on CREATE TABLE IF NOT EXISTS. Tracking a version number
        // (bumped in ExcelIndexService whenever extraction logic changes) rather than reusing the
        // LastWriteTimeUtc/HasCells checks means a logic change alone re-triggers indexing even when
        // the file on disk hasn't changed - the earlier HasCells-only check silently stopped
        // backfilling any file that already had at least one cell row.
        if (!ColumnExists(connection, "IndexedFiles", "IndexVersion"))
        {
            Execute(connection, "ALTER TABLE IndexedFiles ADD COLUMN IndexVersion INTEGER NOT NULL DEFAULT 0");
        }

        // IndexedFileId replaces the old FullPath TEXT column on IndexedCells: repeating every
        // file's full absolute path on every one of its cell rows - plus a second copy in that
        // column's index - was the single largest contributor to database size on large indexes
        // (hundreds of MB of pure redundancy for a workbook with thousands of cells). Referencing
        // IndexedFiles by its integer Id instead costs 8 bytes/row.
        if (!ColumnExists(connection, "IndexedCells", "IndexedFileId"))
        {
            Execute(connection, "ALTER TABLE IndexedCells ADD COLUMN IndexedFileId INTEGER");

            if (ColumnExists(connection, "IndexedCells", "FullPath"))
            {
                Execute(connection,
                    "UPDATE IndexedCells SET IndexedFileId = " +
                    "(SELECT Id FROM IndexedFiles WHERE IndexedFiles.FullPath = IndexedCells.FullPath)");
                // A cell row whose FullPath no longer matches any IndexedFiles row is orphaned data
                // (the file's row was already gone) - nothing legitimate to migrate it to.
                Execute(connection, "DELETE FROM IndexedCells WHERE IndexedFileId IS NULL");
                Execute(connection, "DROP INDEX IF EXISTS IX_IndexedCells_FullPath");
                // The old cascade-delete trigger's body references FullPath - SQLite refuses to drop
                // that column while a trigger still reads it. It gets recreated (with the new,
                // IndexedFileId-based body) by the CREATE TRIGGER statements below.
                Execute(connection, "DROP TRIGGER IF EXISTS trg_IndexedFiles_CascadeDeleteCells");
                Execute(connection, "ALTER TABLE IndexedCells DROP COLUMN FullPath");
            }
        }

        Execute(connection, "CREATE INDEX IF NOT EXISTS IX_IndexedCells_IndexedFileId ON IndexedCells(IndexedFileId)");

        Execute(connection,
            "CREATE TRIGGER IF NOT EXISTS trg_SavedFolders_CascadeDeleteFiles " +
            "AFTER DELETE ON SavedFolders " +
            "FOR EACH ROW " +
            "BEGIN " +
            "DELETE FROM IndexedFiles WHERE RootPath = OLD.Path; " +
            "END");

        Execute(connection,
            "CREATE TRIGGER IF NOT EXISTS trg_IndexedFiles_CascadeDeleteCells " +
            "AFTER DELETE ON IndexedFiles " +
            "FOR EACH ROW " +
            "BEGIN " +
            "DELETE FROM IndexedCells WHERE IndexedFileId = OLD.Id; " +
            "END");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool ColumnExists(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
