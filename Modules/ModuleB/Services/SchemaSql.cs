namespace ModuleB.Services;

/// <summary>
/// Shared DDL for Data\DataFromExcel.db, used by both SavedFolderRepository and
/// FileIndexRepository so either one can run first against a fresh database - each
/// repository's EnsureSchema executes this same idempotent script before doing its own
/// column migrations, so table/trigger creation never races between the two.
/// </summary>
internal static class SchemaSql
{
    public const string CreateTablesAndTriggers =
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
        "FullPath TEXT NOT NULL, " +
        "SheetName TEXT NOT NULL, " +
        "CellReference TEXT NOT NULL, " +
        "RowIndex INTEGER NOT NULL, " +
        "ColumnIndex INTEGER NOT NULL, " +
        "Text TEXT NOT NULL); " +
        "CREATE INDEX IF NOT EXISTS IX_IndexedCells_FullPath ON IndexedCells(FullPath); " +
        "CREATE TRIGGER IF NOT EXISTS trg_SavedFolders_CascadeDeleteFiles " +
        "AFTER DELETE ON SavedFolders " +
        "FOR EACH ROW " +
        "BEGIN " +
        "DELETE FROM IndexedFiles WHERE RootPath = OLD.Path; " +
        "END; " +
        "CREATE TRIGGER IF NOT EXISTS trg_IndexedFiles_CascadeDeleteCells " +
        "AFTER DELETE ON IndexedFiles " +
        "FOR EACH ROW " +
        "BEGIN " +
        "DELETE FROM IndexedCells WHERE FullPath = OLD.FullPath; " +
        "END;";
}
