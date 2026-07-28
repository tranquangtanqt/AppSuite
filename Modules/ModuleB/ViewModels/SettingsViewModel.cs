using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ModuleB.Models;
using ModuleB.Services;

namespace ModuleB.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SavedFolderRepository _repository;

    [ObservableProperty]
    private string? currentPath;

    public ObservableCollection<SavedFolder> SavedFolders { get; } = new();

    public SettingsViewModel(SavedFolderRepository repository, string? initialPath)
    {
        _repository = repository;
        currentPath = initialPath;
        Reload();
    }

    public void SaveCurrentPath()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath))
        {
            return;
        }

        _repository.Add(CurrentPath);
        Reload();
    }

    private void Reload()
    {
        SavedFolders.Clear();
        foreach (var folder in _repository.GetAll())
        {
            SavedFolders.Add(folder);
        }
    }
}
