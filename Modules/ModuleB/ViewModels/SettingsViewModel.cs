using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ModuleB.Models;
using ModuleB.Services;

namespace ModuleB.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SavedFolderRepository _repository;

    /// <summary>Raised with the deleted folder's path so MainWindow can clear its Explorer tree
    /// when the folder currently loaded there was the one just deleted.</summary>
    public event Action<string>? FolderDeleted;

    [ObservableProperty]
    private string? currentPath;

    private int? _editingFolderId;

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

        if (_editingFolderId is int id)
        {
            _repository.Update(id, CurrentPath);
            _editingFolderId = null;
        }
        else
        {
            _repository.Add(CurrentPath);
        }

        Reload();
    }

    public void BeginEdit(SavedFolder folder)
    {
        _editingFolderId = folder.Id;
        CurrentPath = folder.Path;
    }

    public void DeleteFolder(SavedFolder folder)
    {
        _repository.Delete(folder.Id);
        if (_editingFolderId == folder.Id)
        {
            _editingFolderId = null;
        }

        Reload();
        FolderDeleted?.Invoke(folder.Path);
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
