using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModuleB.Models;
using ModuleB.Services;

namespace ModuleB.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly OfficeTextSearchService _textSearchService = new();

    [ObservableProperty]
    private string? rootPath;

    [ObservableProperty]
    private string groupFilter = string.Empty;

    [ObservableProperty]
    private string keywordQuery = string.Empty;

    [ObservableProperty]
    private bool isSearching;

    public ObservableCollection<SearchResultItem> Results { get; } = new();

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
            var found = await Task.Run(() => FindMatches(root, group, keyword));
            Results.Clear();
            foreach (var item in found)
            {
                Results.Add(item);
            }
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() => !string.IsNullOrEmpty(RootPath) && !IsSearching;

    [RelayCommand]
    private void Clear()
    {
        GroupFilter = string.Empty;
        KeywordQuery = string.Empty;
        Results.Clear();
    }

    partial void OnRootPathChanged(string? value) => SearchCommand.NotifyCanExecuteChanged();

    partial void OnIsSearchingChanged(bool value) => SearchCommand.NotifyCanExecuteChanged();

    private List<SearchResultItem> FindMatches(string root, string groupFilter, string keywordQuery)
    {
        var results = new List<SearchResultItem>();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var segments = relative.Split(Path.DirectorySeparatorChar);
            var group = segments.Length > 1 ? segments[0] : "(root)";

            if (!QueryMatcher.Matches(group, groupFilter))
            {
                continue;
            }

            var content = _textSearchService.ExtractText(file) ?? Path.GetFileNameWithoutExtension(file);
            if (!QueryMatcher.Matches(content, keywordQuery))
            {
                continue;
            }

            results.Add(SearchResultItem.FromFile(group, file));
        }

        return results
            .OrderBy(r => r.GroupName)
            .ThenBy(r => r.FileName)
            .ToList();
    }
}
