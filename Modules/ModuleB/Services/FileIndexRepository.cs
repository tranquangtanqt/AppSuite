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

        using var connection = OpenConnection();
        SchemaSql.EnsureSchema(connection);
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

        foreach (var path in toDelete)
        {
            filesPathParam.Value = path;
            deleteFiles.ExecuteNonQuery();
        }
    }

    /// <summary>Replaces all cells stored for a file - simpler and cheap enough than diffing per cell.
    /// Called right after <see cref="Upsert"/> for the same fullPath, so its IndexedFiles row is
    /// guaranteed to exist.</summary>
    public void ReplaceCells(string fullPath, IEnumerable<ExcelCellMatch> cells)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        object indexedFileId;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Id FROM IndexedFiles WHERE FullPath = $path";
            select.Parameters.AddWithValue("$path", fullPath);
            indexedFileId = select.ExecuteScalar() ?? throw new InvalidOperationException(
                $"No IndexedFiles row for '{fullPath}' - Upsert must run before ReplaceCells.");
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM IndexedCells WHERE IndexedFileId = $id";
            delete.Parameters.AddWithValue("$id", indexedFileId);
            delete.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO IndexedCells (IndexedFileId, SheetName, CellReference, RowIndex, ColumnIndex, Text) " +
                "VALUES ($id, $sheet, $reference, $row, $column, $text)";
            var idParam = insert.Parameters.Add("$id", SqliteType.Integer);
            var sheetParam = insert.Parameters.Add("$sheet", SqliteType.Text);
            var referenceParam = insert.Parameters.Add("$reference", SqliteType.Text);
            var rowParam = insert.Parameters.Add("$row", SqliteType.Integer);
            var columnParam = insert.Parameters.Add("$column", SqliteType.Integer);
            var textParam = insert.Parameters.Add("$text", SqliteType.Text);

            foreach (var cell in cells)
            {
                idParam.Value = indexedFileId;
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
            "SELECT c.SheetName, c.CellReference, c.RowIndex, c.ColumnIndex, c.Text " +
            "FROM IndexedCells c JOIN IndexedFiles f ON f.Id = c.IndexedFileId " +
            "WHERE f.FullPath = $path";
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
