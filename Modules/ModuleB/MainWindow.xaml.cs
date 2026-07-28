using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModuleB.Models;
using ModuleB.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ModuleB;

public sealed partial class MainWindow : Window
{
    // TreeViewNode is a native WinRT control: every read of its Content property crosses the ABI,
    // and a plain C# object stored there does not reliably round-trip back to the same managed
    // instance (a cast can throw InvalidCastException on a bare WinRT.IInspectable). Content is set
    // to a plain string (the folder name) only for display - all folder metadata is tracked here
    // instead, keyed by node identity, and never read back off Content.
    private readonly Dictionary<TreeViewNode, FolderNode> _folderByNode = new();

    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
            };
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            LoadRoot(folder.Path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Khong the mo thu muc", ex.Message);
        }
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Dong",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void LoadRoot(string path)
    {
        ViewModel.RootPath = path;
        ViewModel.Results.Clear();

        FolderTreeView.RootNodes.Clear();
        _folderByNode.Clear();

        var folder = new FolderNode(new DirectoryInfo(path).Name, path);
        var rootNode = new TreeViewNode
        {
            Content = folder.Name,
            IsExpanded = true,
        };
        _folderByNode.Add(rootNode, folder);

        FolderTreeView.RootNodes.Add(rootNode);
        PopulateChildren(rootNode, folder);
    }

    private void FolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node.HasUnrealizedChildren && _folderByNode.TryGetValue(args.Node, out var folder))
        {
            PopulateChildren(args.Node, folder);
        }
    }

    private void PopulateChildren(TreeViewNode node, FolderNode folder)
    {
        node.Children.Clear();
        node.HasUnrealizedChildren = false;

        List<string> subDirectories;
        try
        {
            subDirectories = Directory.EnumerateDirectories(folder.FullPath)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var directory in subDirectories)
        {
            var childFolder = new FolderNode(Path.GetFileName(directory), directory);
            var child = new TreeViewNode
            {
                Content = childFolder.Name,
                HasUnrealizedChildren = HasSubDirectories(directory),
            };
            _folderByNode.Add(child, childFolder);
            node.Children.Add(child);
        }
    }

    private static bool HasSubDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Any();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private void FolderTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        var node = sender.SelectedNode;
        if (node is null || !_folderByNode.TryGetValue(node, out var folder) || ViewModel.RootPath is null)
        {
            return;
        }

        var relative = Path.GetRelativePath(ViewModel.RootPath, folder.FullPath);
        var group = relative == "." ? string.Empty : relative.Split(Path.DirectorySeparatorChar)[0];

        ViewModel.GroupFilter = group;
        if (ViewModel.SearchCommand.CanExecute(null))
        {
            ViewModel.SearchCommand.Execute(null);
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(this, ViewModel.RootPath)
        {
            XamlRoot = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            LoadRoot(dialog.SelectedPath);
        }
    }
}
