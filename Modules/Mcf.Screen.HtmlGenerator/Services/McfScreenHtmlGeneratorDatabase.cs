using Microsoft.Data.Sqlite;
using Mcf.Screen.HtmlGenerator.Models;

namespace Mcf.Screen.HtmlGenerator.Services;

/// <summary>Persists the imported screen list into a local SQLite file at
/// Data\Database\01_画面説明書.db, next to the module executable - same ADO.NET-only pattern as
/// McfDbDefHtmlGeneratorDatabase/RdbmsHtmlGeneratorDatabase.</summary>
public sealed class McfScreenHtmlGeneratorDatabase
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public McfScreenHtmlGeneratorDatabase()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Database");
        Directory.CreateDirectory(dataDirectory);
        DatabasePath = Path.Combine(dataDirectory, "01_画面説明書.db");
        _connectionString = $"Data Source={DatabasePath}";
    }

    public bool Exists => File.Exists(DatabasePath);

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task ReplaceAllAsync(IReadOnlyList<ScreenRecord> screens)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText =
                """
                DROP TABLE IF EXISTS Screens;

                CREATE TABLE Screens (
                  Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                  ScreenCode   TEXT NOT NULL,
                  ScreenName   TEXT NOT NULL,
                  DocNumber    TEXT,
                  Revision     TEXT,
                  SourceFile   TEXT NOT NULL,
                  SearchText   TEXT,
                  HtmlFileName TEXT NOT NULL,
                  SheetCount   INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX idx_screens_code ON Screens(ScreenCode);
                """;
            await schema.ExecuteNonQueryAsync();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO Screens (ScreenCode, ScreenName, DocNumber, Revision, SourceFile, SearchText, HtmlFileName, SheetCount) " +
                "VALUES ($code, $name, $docNumber, $revision, $sourceFile, $searchText, $htmlFileName, $sheetCount)";
            var pCode = insert.Parameters.Add("$code", SqliteType.Text);
            var pName = insert.Parameters.Add("$name", SqliteType.Text);
            var pDocNumber = insert.Parameters.Add("$docNumber", SqliteType.Text);
            var pRevision = insert.Parameters.Add("$revision", SqliteType.Text);
            var pSourceFile = insert.Parameters.Add("$sourceFile", SqliteType.Text);
            var pSearchText = insert.Parameters.Add("$searchText", SqliteType.Text);
            var pHtmlFileName = insert.Parameters.Add("$htmlFileName", SqliteType.Text);
            var pSheetCount = insert.Parameters.Add("$sheetCount", SqliteType.Integer);

            foreach (var screen in screens)
            {
                pCode.Value = screen.ScreenCode;
                pName.Value = screen.ScreenName;
                pDocNumber.Value = screen.DocNumber;
                pRevision.Value = screen.Revision;
                pSourceFile.Value = screen.SourceFile;
                pSearchText.Value = screen.SearchText;
                pHtmlFileName.Value = screen.HtmlFileName;
                pSheetCount.Value = screen.SheetCount;
                await insert.ExecuteNonQueryAsync();
            }
        }

        transaction.Commit();
    }

    public List<ScreenRecord> GetAllScreens()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ScreenCode, ScreenName, DocNumber, Revision, SourceFile, SearchText, HtmlFileName, SheetCount " +
            "FROM Screens ORDER BY ScreenCode";

        var result = new List<ScreenRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ScreenRecord
            {
                ScreenCode = reader.GetString(0),
                ScreenName = reader.GetString(1),
                DocNumber = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Revision = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                SourceFile = reader.GetString(4),
                SearchText = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                HtmlFileName = reader.GetString(6),
                SheetCount = reader.GetInt32(7),
            });
        }

        return result;
    }
}
