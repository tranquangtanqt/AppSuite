using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModuleB.Models;
using ModuleB.ViewModels;

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
        SharedUI.Helpers.WindowIcon.Apply(this);
        Closed += (_, _) => _helpWindow?.Close(); // đóng app thì đóng luôn cửa sổ Hướng dẫn
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

        // A folder deleted from the saved-folders list may be the one currently loaded in the
        // Explorer tree - if so, the tree/search results are now stale and must be cleared too.
        dialog.ViewModel.FolderDeleted += path =>
        {
            if (string.Equals(path, ViewModel.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                ClearRoot();
            }
        };

        var result = await dialog.ShowAsync();
        if ((result == ContentDialogResult.Primary || dialog.ConfirmedByDoubleTap) && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            LoadRoot(dialog.SelectedPath);
            await ViewModel.IndexRootAsync(dialog.SelectedPath);
        }
    }

    private void ClearRoot()
    {
        ViewModel.RootPath = null;
        ViewModel.GroupFilter = string.Empty;
        ViewModel.Results.Clear();

        FolderTreeView.RootNodes.Clear();
        _folderByNode.Clear();
    }

    private async void ResultsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SearchResultItem item)
        {
            return;
        }

        var matches = await Task.Run(() => ViewModel.GetMatchingCells(item.FullPath));

        var dialog = new FileMatchesDialog(item.FileName, matches)
        {
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void OpenFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string path })
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            await ShowErrorAsync("Khong the mo file", ex.Message);
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

    // ----- Hướng dẫn (F1) -----

    private SharedUI.Help.HelpWindow? _helpWindow;

    /// <summary>Mở cửa sổ Hướng dẫn (1 cửa sổ duy nhất - đang mở thì đưa lên trước). Nội dung: Views/HelpContent.</summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is null)
        {
            _helpWindow = new SharedUI.Help.HelpWindow("ModuleB - Tìm file Excel", "Tìm file Excel theo nhóm thư mục và chữ trong nội dung, mở thẳng tới ô khớp.",
                Views.HelpContent.Sections, searchPlaceholder: "Tìm tính năng, vd: AND OR, chỉ mục, mở ô...");
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        _helpWindow.Activate();
    }
}
