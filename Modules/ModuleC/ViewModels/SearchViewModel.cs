using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModuleC.Models;
using ModuleC.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ModuleC.ViewModels;

public sealed partial class SearchViewModel : ObservableObject
{
    private readonly TableColumnCatalog _catalog;

    public ObservableCollection<AliasRow> AliasRows { get; } = [];
    public ObservableCollection<SearchResultRow> Results { get; } = [];

    [ObservableProperty]
    private string _columnListText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SearchViewModel(TableColumnCatalog catalog)
    {
        _catalog = catalog;
        AliasRows.Add(new AliasRow());
    }

    public async Task InitializeAsync()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "table_columns.json");
            await _catalog.LoadAsync(path);
            StatusMessage = $"Da tai {_catalog.Count:N0} dong du lieu.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Khong tai duoc du lieu: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddRow() => AliasRows.Add(new AliasRow());

    [RelayCommand]
    private void RemoveRow(AliasRow? row)
    {
        if (row is not null)
        {
            AliasRows.Remove(row);
        }
    }

    [RelayCommand]
    private void Search()
    {
        Results.Clear();

        var aliasToTable = AliasRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Alias) && !string.IsNullOrWhiteSpace(r.TableName))
            .GroupBy(r => r.Alias.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().TableName.Trim(), StringComparer.OrdinalIgnoreCase);

        var lines = ColumnListText
            .Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.Length > 0);

        var stt = 1;
        var aliasesWithColumnFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var dotIndex = line.IndexOf('.');
            if (dotIndex <= 0 || dotIndex == line.Length - 1)
            {
                continue;
            }

            var alias = line[..dotIndex].Trim();
            var label = line[(dotIndex + 1)..].Trim();
            aliasesWithColumnFilter.Add(alias);

            if (!aliasToTable.TryGetValue(alias, out var tableName))
            {
                continue;
            }

            foreach (var entry in _catalog.FindByTableAndLabel(tableName, label))
            {
                Results.Add(new SearchResultRow
                {
                    Stt = stt++,
                    TableName = entry.TableName,
                    ColumnName = $"{alias}.{entry.ColumnName}",
                    Comment = entry.CommentColumn
                });
            }
        }

        // Alias entered with no matching line in the column list -> show every column of its table.
        foreach (var (alias, tableName) in aliasToTable)
        {
            if (aliasesWithColumnFilter.Contains(alias))
            {
                continue;
            }

            foreach (var entry in _catalog.GetColumnsByTable(tableName))
            {
                Results.Add(new SearchResultRow
                {
                    Stt = stt++,
                    TableName = entry.TableName,
                    ColumnName = $"{alias}.{entry.ColumnName}",
                    Comment = entry.CommentColumn
                });
            }
        }

        StatusMessage = $"Tim thay {Results.Count:N0} ket qua.";
    }

    [RelayCommand]
    private void CopyResults()
    {
        if (Results.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("STT\tTen bang\tTen cot\tChu thich ten cot");
        foreach (var row in Results)
        {
            sb.AppendLine($"{row.Stt}\t{row.TableName}\t{row.ColumnName}\t{row.Comment}");
        }

        var package = new DataPackage();
        package.SetText(sb.ToString());
        Clipboard.SetContent(package);
    }
}
