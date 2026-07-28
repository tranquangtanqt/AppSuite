using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using ModuleB.Models;

namespace ModuleB.Services;

/// <summary>
/// Persists the user's chosen root folders in a local SQLite file at Data\DataFromExcel.db, next to
/// the module executable, so the Settings dialog can offer previously used folders.
/// </summary>
public sealed class SavedFolderRepository
{
    private readonly string _connectionString;

    public SavedFolderRepository()
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
            "CREATE TABLE IF NOT EXISTS SavedFolders (" +
            "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "Path TEXT NOT NULL UNIQUE)";
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public List<SavedFolder> GetAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Path FROM SavedFolders ORDER BY Path";

        var result = new List<SavedFolder>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SavedFolder(reader.GetInt32(0), reader.GetString(1)));
        }

        return result;
    }

    public SavedFolder Add(string path)
    {
        using var connection = OpenConnection();

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT OR IGNORE INTO SavedFolders (Path) VALUES ($path)";
            insert.Parameters.AddWithValue("$path", path);
            insert.ExecuteNonQuery();
        }

        using var select = connection.CreateCommand();
        select.CommandText = "SELECT Id FROM SavedFolders WHERE Path = $path";
        select.Parameters.AddWithValue("$path", path);
        var id = Convert.ToInt32(select.ExecuteScalar());

        return new SavedFolder(id, path);
    }
}
