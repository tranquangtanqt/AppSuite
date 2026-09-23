using Microsoft.Data.Sqlite;
using Mcf.CrudDiagram.HtmlGenerator.Models;

namespace Mcf.CrudDiagram.HtmlGenerator.Services;

/// <summary>Persists the imported logic list into a local SQLite file at
/// Data\Database\02_CRUD図.db, next to the module executable - same ADO.NET-only pattern as
/// Mcf.Screen.HtmlGenerator's McfScreenHtmlGeneratorDatabase.</summary>
public sealed class CrudDatabase
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public CrudDatabase()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Database");
        Directory.CreateDirectory(dataDirectory);
        DatabasePath = Path.Combine(dataDirectory, "02_CRUD図.db");
        _connectionString = $"Data Source={DatabasePath}";
    }

    public bool Exists => File.Exists(DatabasePath);

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task ReplaceAllAsync(IReadOnlyList<CrudRecord> logics)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText =
                """
                DROP TABLE IF EXISTS Logics;

                CREATE TABLE Logics (
                  Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                  ScreenCode     TEXT NOT NULL,
                  ScreenName     TEXT NOT NULL,
                  ModuleId       TEXT,
                  ModuleName     TEXT,
                  SubModuleId    TEXT,
                  SubModuleName  TEXT,
                  DocNumber      TEXT,
                  Version        TEXT,
                  Revision       TEXT,
                  SourceFile     TEXT NOT NULL,
                  SearchText     TEXT,
                  HtmlFileName   TEXT NOT NULL,
                  BlockCount     INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX idx_logics_code ON Logics(ScreenCode);
                """;
            await schema.ExecuteNonQueryAsync();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO Logics (ScreenCode, ScreenName, ModuleId, ModuleName, SubModuleId, SubModuleName, " +
                "DocNumber, Version, Revision, SourceFile, SearchText, HtmlFileName, BlockCount) " +
                "VALUES ($code, $name, $moduleId, $moduleName, $subModuleId, $subModuleName, " +
                "$docNumber, $version, $revision, $sourceFile, $searchText, $htmlFileName, $blockCount)";
            var pCode = insert.Parameters.Add("$code", SqliteType.Text);
            var pName = insert.Parameters.Add("$name", SqliteType.Text);
            var pModuleId = insert.Parameters.Add("$moduleId", SqliteType.Text);
            var pModuleName = insert.Parameters.Add("$moduleName", SqliteType.Text);
            var pSubModuleId = insert.Parameters.Add("$subModuleId", SqliteType.Text);
            var pSubModuleName = insert.Parameters.Add("$subModuleName", SqliteType.Text);
            var pDocNumber = insert.Parameters.Add("$docNumber", SqliteType.Text);
            var pVersion = insert.Parameters.Add("$version", SqliteType.Text);
            var pRevision = insert.Parameters.Add("$revision", SqliteType.Text);
            var pSourceFile = insert.Parameters.Add("$sourceFile", SqliteType.Text);
            var pSearchText = insert.Parameters.Add("$searchText", SqliteType.Text);
            var pHtmlFileName = insert.Parameters.Add("$htmlFileName", SqliteType.Text);
            var pBlockCount = insert.Parameters.Add("$blockCount", SqliteType.Integer);

            foreach (var logic in logics)
            {
                pCode.Value = logic.ScreenCode;
                pName.Value = logic.ScreenName;
                pModuleId.Value = logic.ModuleId;
                pModuleName.Value = logic.ModuleName;
                pSubModuleId.Value = logic.SubModuleId;
                pSubModuleName.Value = logic.SubModuleName;
                pDocNumber.Value = logic.DocNumber;
                pVersion.Value = logic.Version;
                pRevision.Value = logic.Revision;
                pSourceFile.Value = logic.SourceFile;
                pSearchText.Value = logic.SearchText;
                pHtmlFileName.Value = logic.HtmlFileName;
                pBlockCount.Value = logic.BlockCount;
                await insert.ExecuteNonQueryAsync();
            }
        }

        transaction.Commit();
    }

    public List<CrudRecord> GetAllLogics()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ScreenCode, ScreenName, ModuleId, ModuleName, SubModuleId, SubModuleName, " +
            "DocNumber, Version, Revision, SourceFile, SearchText, HtmlFileName, BlockCount " +
            "FROM Logics ORDER BY ScreenCode";

        var result = new List<CrudRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CrudRecord
            {
                ScreenCode = reader.GetString(0),
                ScreenName = reader.GetString(1),
                ModuleId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                ModuleName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                SubModuleId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                SubModuleName = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                DocNumber = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                Version = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                Revision = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                SourceFile = reader.GetString(9),
                SearchText = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                HtmlFileName = reader.GetString(11),
                BlockCount = reader.GetInt32(12),
            });
        }

        return result;
    }
}
