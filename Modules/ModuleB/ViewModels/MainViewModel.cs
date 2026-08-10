using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModuleB.Models;
using ModuleB.Services;

namespace ModuleB.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ExcelIndexService _indexService = new();

    [ObservableProperty]
    private string? rootPath;

    [ObservableProperty]
    private string groupFilter = string.Empty;

    [ObservableProperty]
    private string keywordQuery = string.Empty;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private bool isIndexing;

    public ObservableCollection<SearchResultItem> Results { get; } = new();

    /// <summary>
    /// Scans the folder's .xlsx/.xlsm files and (re)saves their extracted text into SQLite, so
    /// subsequent searches read cached content instead of re-parsing every workbook.
    /// </summary>
    public async Task IndexRootAsync(string path)
    {
        IsIndexing = true;
        try
        {
            await _indexService.IndexFolderAsync(path);
        }
        finally
        {
            IsIndexing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (string.IsNullOrEmpty(RootPath))
        {
            return;
        }

        IsSearching = true;
        var root = RootPath;
        var group = GroupFilter;
        var keyword = KeywordQuery;
        try
        {
            var found = await Task.Run(() => _indexService.Search(root, group, keyword));
            Results.Clear();
            foreach (var (matchGroup, fullPath) in found)
            {
                Results.Add(SearchResultItem.FromFile(matchGroup, fullPath));
            }
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() => !string.IsNullOrEmpty(RootPath) && !IsSearching && !IsIndexing;

    /// <summary>Cells of an indexed file matching the current keyword, for FileMatchesDialog.</summary>
    public List<CellMatchItem> GetMatchingCells(string fullPath) => _indexService.GetMatchingCells(fullPath, KeywordQuery);

    [RelayCommand]
    private void Clear()
    {
        GroupFilter = string.Empty;
        KeywordQuery = string.Empty;
        Results.Clear();
    }

    partial void OnRootPathChanged(string? value) => SearchCommand.NotifyCanExecuteChanged();

    partial void OnIsSearchingChanged(bool value) => SearchCommand.NotifyCanExecuteChanged();

    partial void OnIsIndexingChanged(bool value) => SearchCommand.NotifyCanExecuteChanged();
}
