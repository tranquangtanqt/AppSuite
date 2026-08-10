using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using ModuleB.Models;

namespace ModuleB.Services;

/// <summary>
/// Persists extracted Excel content in the same SQLite file as SavedFolderRepository, keyed by full
/// path, so MainViewModel.Search can read cached content instead of re-parsing every workbook on
/// every search.
/// </summary>
public sealed class FileIndexRepository
{
    private readonly string _connectionString;

    public FileIndexRepository()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "DataFromExcel.db");
        _connectionString = $"Data Source={dbPath}";

        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
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
            "CREATE INDEX IF NOT EXISTS IX_IndexedCells_FullPath ON IndexedCells(FullPath)";
        command.ExecuteNonQuery();

        // IndexVersion was added after IndexedFiles already shipped, so existing databases need a
        // migration rather than relying on CREATE TABLE IF NOT EXISTS. Tracking a version number
        // (bumped in ExcelIndexService whenever extraction logic changes) rather than reusing the
        // LastWriteTimeUtc/HasCells checks means a logic change alone re-triggers indexing even when
        // the file on disk hasn't changed - the earlier HasCells-only check silently stopped
        // backfilling any file that already had at least one cell row.
        if (!ColumnExists(connection, "IndexedFiles", "IndexVersion"))
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE IndexedFiles ADD COLUMN IndexVersion INTEGER NOT NULL DEFAULT 0";
            alterCommand.ExecuteNonQuery();
        }
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

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public DateTime? GetLastWriteTimeUtc(string fullPath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT LastWriteTimeUtcTicks FROM IndexedFiles WHERE FullPath = $path";
        command.Parameters.AddWithValue("$path", fullPath);

        var result = command.ExecuteScalar();
        return result is null ? null : new DateTime((long)result, DateTimeKind.Utc);
    }

    public int GetIndexVersion(string fullPath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IndexVersion FROM IndexedFiles WHERE FullPath = $path";
        command.Parameters.AddWithValue("$path", fullPath);

        var result = command.ExecuteScalar();
        return result is null ? -1 : Convert.ToInt32(result);
    }

    public void Upsert(string rootPath, string groupName, string fullPath, string fileName, string content, DateTime lastWriteTimeUtc, int indexVersion)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO IndexedFiles (RootPath, GroupName, FullPath, FileName, Content, LastWriteTimeUtcTicks, IndexVersion) " +
            "VALUES ($root, $group, $path, $name, $content, $ticks, $version) " +
            "ON CONFLICT(FullPath) DO UPDATE SET " +
            "RootPath = excluded.RootPath, GroupName = excluded.GroupName, FileName = excluded.FileName, " +
            "Content = excluded.Content, LastWriteTimeUtcTicks = excluded.LastWriteTimeUtcTicks, " +
            "IndexVersion = excluded.IndexVersion";
        command.Parameters.AddWithValue("$root", rootPath);
        command.Parameters.AddWithValue("$group", groupName);
        command.Parameters.AddWithValue("$path", fullPath);
        command.Parameters.AddWithValue("$name", fileName);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$ticks", lastWriteTimeUtc.Ticks);
        command.Parameters.AddWithValue("$version", indexVersion);
        command.ExecuteNonQuery();
    }

    /// <summary>Removes rows under <paramref name="rootPath"/> whose file no longer exists on disk.</summary>
    public void DeleteMissing(string rootPath, IReadOnlySet<string> existingPaths)
    {
        using var connection = OpenConnection();

        var toDelete = new List<string>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT FullPath FROM IndexedFiles WHERE RootPath = $root";
            select.Parameters.AddWithValue("$root", rootPath);

            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (!existingPaths.Contains(path))
                {
                    toDelete.Add(path);
                }
            }
        }

        if (toDelete.Count == 0)
        {
            return;
        }

        using var deleteFiles = connection.CreateCommand();
        deleteFiles.CommandText = "DELETE FROM IndexedFiles WHERE FullPath = $path";
        var filesPathParam = deleteFiles.Parameters.Add("$path", SqliteType.Text);

        using var deleteCells = connection.CreateCommand();
        deleteCells.CommandText = "DELETE FROM IndexedCells WHERE FullPath = $path";
        var cellsPathParam = deleteCells.Parameters.Add("$path", SqliteType.Text);

        foreach (var path in toDelete)
        {
            filesPathParam.Value = path;
            deleteFiles.ExecuteNonQuery();
            cellsPathParam.Value = path;
            deleteCells.ExecuteNonQuery();
        }
    }

    /// <summary>Replaces all cells stored for a file - simpler and cheap enough than diffing per cell.</summary>
    public void ReplaceCells(string fullPath, IEnumerable<ExcelCellMatch> cells)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM IndexedCells WHERE FullPath = $path";
            delete.Parameters.AddWithValue("$path", fullPath);
            delete.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO IndexedCells (FullPath, SheetName, CellReference, RowIndex, ColumnIndex, Text) " +
                "VALUES ($path, $sheet, $reference, $row, $column, $text)";
            var pathParam = insert.Parameters.Add("$path", SqliteType.Text);
            var sheetParam = insert.Parameters.Add("$sheet", SqliteType.Text);
            var referenceParam = insert.Parameters.Add("$reference", SqliteType.Text);
            var rowParam = insert.Parameters.Add("$row", SqliteType.Integer);
            var columnParam = insert.Parameters.Add("$column", SqliteType.Integer);
            var textParam = insert.Parameters.Add("$text", SqliteType.Text);

            foreach (var cell in cells)
            {
                pathParam.Value = fullPath;
                sheetParam.Value = cell.SheetName;
                referenceParam.Value = cell.CellReference;
                rowParam.Value = cell.RowIndex;
                columnParam.Value = cell.ColumnIndex;
                textParam.Value = cell.Text;
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public List<ExcelCellMatch> GetCells(string fullPath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT SheetName, CellReference, RowIndex, ColumnIndex, Text FROM IndexedCells WHERE FullPath = $path";
        command.Parameters.AddWithValue("$path", fullPath);

        var result = new List<ExcelCellMatch>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ExcelCellMatch(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4)));
        }

        return result;
    }

    public List<IndexedFile> GetByRoot(string rootPath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT GroupName, FullPath, FileName, Content FROM IndexedFiles WHERE RootPath = $root";
        command.Parameters.AddWithValue("$root", rootPath);

        var result = new List<IndexedFile>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new IndexedFile(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return result;
    }
}
