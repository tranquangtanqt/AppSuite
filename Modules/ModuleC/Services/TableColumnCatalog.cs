using System.Text.Json;
using ModuleC.Models;

namespace ModuleC.Services;

/// <summary>
/// In-memory index of Data\table_columns.json, keyed by table name so a search only has to scan
/// the rows that belong to the tables the user has entered on the left-hand side.
/// </summary>
public sealed class TableColumnCatalog
{
    private readonly Dictionary<string, List<TableColumnEntry>> _byTable = new(StringComparer.OrdinalIgnoreCase);
    private List<(string TableName, string CommentTable)>? _distinctTables;

    public int Count { get; private set; }

    public async Task LoadAsync(string jsonPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(jsonPath);
        var entries = await JsonSerializer.DeserializeAsync<List<TableColumnEntry>>(stream, cancellationToken: cancellationToken)
                      ?? [];

        _byTable.Clear();
        foreach (var group in entries.GroupBy(e => e.TableName))
        {
            _byTable[group.Key] = group.ToList();
        }

        _distinctTables = null;
        Count = entries.Count;
    }

    /// <summary>Every distinct table name with its Japanese table comment, for the table-name search dialog.</summary>
    public IReadOnlyList<(string TableName, string CommentTable)> GetDistinctTables()
    {
        return _distinctTables ??= _byTable
            .Select(kvp => (TableName: kvp.Key, CommentTable: kvp.Value.Count > 0 ? kvp.Value[0].CommentTable : string.Empty))
            .ToList();
    }

    /// <summary>
    /// Finds columns of <paramref name="tableName"/> whose Japanese comment label (the part of
    /// CommentColumn before the first "//") matches <paramref name="label"/>.
    /// </summary>
    public IReadOnlyList<TableColumnEntry> FindByTableAndLabel(string tableName, string label)
    {
        if (!_byTable.TryGetValue(tableName, out var entries))
        {
            return [];
        }

        return entries.Where(e => CommentLabelMatches(e.CommentColumn, label)).ToList();
    }

    /// <summary>Every column of <paramref name="tableName"/>, used when the user gives no column filter for it.</summary>
    public IReadOnlyList<TableColumnEntry> GetColumnsByTable(string tableName)
    {
        return _byTable.TryGetValue(tableName, out var entries) ? entries : [];
    }

    private static bool CommentLabelMatches(string commentColumn, string label)
    {
        var separatorIndex = commentColumn.IndexOf("//", StringComparison.Ordinal);
        var segment = separatorIndex >= 0 ? commentColumn[..separatorIndex] : commentColumn;
        return string.Equals(segment.Trim(), label.Trim(), StringComparison.Ordinal);
    }
}
