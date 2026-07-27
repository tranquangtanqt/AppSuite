using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModuleC.Models;
using ModuleC.Services;

namespace ModuleC.ViewModels;

/// <summary>Backs the "Tim kiem ten bang" dialog - search table name/comment across the whole catalog.</summary>
public sealed partial class TableNameSearchViewModel : ObservableObject
{
    private readonly TableColumnCatalog _catalog;

    public ObservableCollection<TableNameResult> Results { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    public TableNameSearchViewModel(TableColumnCatalog catalog)
    {
        _catalog = catalog;
    }

    [RelayCommand]
    private void Search()
    {
        Results.Clear();

        var query = Query.Trim();
        if (query.Length == 0)
        {
            return;
        }

        var stt = 1;
        foreach (var (tableName, commentTable) in _catalog.GetDistinctTables())
        {
            var matches = tableName.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || commentTable.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                continue;
            }

            Results.Add(new TableNameResult
            {
                Stt = stt++,
                TableName = tableName,
                CommentTable = commentTable
            });
        }
    }
}
