using DbTools.Core;
using DbTools.Models;
using DbTools.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace DbTools.Views;

public sealed partial class ConnectionsPage : Page
{
    public ConnectionsViewModel ViewModel { get; } = new();

    private bool _settingPassword;

    /// <summary>Đang dựng lại cây: bỏ qua sự kiện chọn / mở / đóng do chính việc dựng gây ra.</summary>
    private bool _building;
    private bool _rebuildQueued;

    /// <summary>Đang đổi lựa chọn do bấm trong cây: cây đã đúng, không dựng lại (dựng lại thì mất bấm đúp, vị trí cuộn).</summary>
    private bool _pickingInTree;

    /// <summary>Đang kéo 1 dòng của cây / vừa thả xuống khoảng trống dưới cây (= đưa ra gốc).</summary>
    private bool _dragging;
    private bool _droppedOnBlank;

    /// <summary>Hồ sơ + chữ đang hiện ở ô Thư mục (gõ xong mới chuyển, không chuyển theo từng phím).</summary>
    private ConnectionProfile? _folderOwner;
    private string _folderShown = string.Empty;

    public ConnectionsPage()
    {
        InitializeComponent();
        ViewModel.ConfirmDelete = ConfirmAsync;
        ViewModel.TreeChanged += QueueRebuild;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionsViewModel.Selected))
            {
                ShowPassword();
                CommitFolder(); // ô Thư mục còn chữ chưa nhận của hồ sơ trước
                ShowFolder();
                if (!_pickingInTree)
                {
                    QueueRebuild();
                }
            }
        };
        Loaded += (_, _) =>
        {
            ShowPassword();
            ShowFolder();
            Rebuild();
        };
        Unloaded += (_, _) => CommitFolder();
    }

    // ----- Cây hồ sơ -----

    /// <summary>Dựng lại sau khi xử lý xong sự kiện hiện tại (đang giữa kéo-thả / đổi lựa chọn thì không sửa cây được).</summary>
    private void QueueRebuild()
    {
        if (!_rebuildQueued)
        {
            _rebuildQueued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _rebuildQueued = false;
                Rebuild();
            });
        }
    }

    private void Rebuild()
    {
        var filtering = ViewModel.Filter.Trim().Length > 0;
        var collapsed = new HashSet<string>(AppState.Settings.CollapsedFolders, StringComparer.OrdinalIgnoreCase);
        var selectedFolder = ViewModel.Selected?.Folder ?? ViewModel.SelectedFolder;
        TreeViewNode? selected = null;

        void Fill(IList<TreeViewNode> nodes, ProfileFolder folder)
        {
            foreach (var f in folder.Folders)
            {
                // Hồ sơ đang chọn luôn thấy được: mở các thư mục chứa nó.
                var holdsSelected = ViewModel.Selected is not null && ProfileTree.IsUnder(selectedFolder, f.Path);
                var node = new TreeViewNode { Content = f, IsExpanded = filtering || holdsSelected || !collapsed.Contains(f.Path) };
                Fill(node.Children, f);
                nodes.Add(node);
                if (ViewModel.Selected is null && f.Path.Equals(selectedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    selected = node;
                }
            }
            foreach (var p in folder.Profiles)
            {
                var node = new TreeViewNode { Content = p };
                nodes.Add(node);
                if (p == ViewModel.Selected)
                {
                    selected = node;
                }
            }
        }

        _building = true;
        try
        {
            Tree.RootNodes.Clear();
            Fill(Tree.RootNodes, ProfileTree.Build(ViewModel.Profiles, ViewModel.Filter));
            Tree.SelectedNode = selected;
        }
        finally
        {
            _building = false;
        }
        if (selected is not null)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Tree.FindDescendant<ListViewBase>()?.ScrollIntoView(selected));
        }
    }

    private void Tree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_building || args.AddedItems.FirstOrDefault() is not TreeViewNode node)
        {
            return;
        }
        _pickingInTree = true;
        if (node.Content is ConnectionProfile profile)
        {
            ViewModel.Selected = profile;
        }
        else if (node.Content is ProfileFolder folder)
        {
            ViewModel.SelectFolder(folder.Path);
        }
        _pickingInTree = false;
    }

    private void Tree_Expanding(TreeView sender, TreeViewExpandingEventArgs args) => RememberExpanded(args.Node, expanded: true);

    private void Tree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args) => RememberExpanded(args.Node, expanded: false);

    private void RememberExpanded(TreeViewNode node, bool expanded)
    {
        if (_building || ViewModel.Filter.Trim().Length > 0 || node.Content is not ProfileFolder folder)
        {
            return;
        }
        var existing = new HashSet<string>(ProfileTree.FolderPaths(ViewModel.Profiles), StringComparer.OrdinalIgnoreCase);
        var list = AppState.Settings.CollapsedFolders;
        list.RemoveAll(p => p.Equals(folder.Path, StringComparison.OrdinalIgnoreCase) || !existing.Contains(p));
        if (!expanded)
        {
            list.Add(folder.Path);
        }
        AppState.SaveSettings();
    }

    private void Tree_DragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
    {
        _dragging = true;
        _droppedOnBlank = false;
    }

    private void Blank_DragOver(object sender, DragEventArgs e)
    {
        if (_dragging && e.GetPosition(Tree).Y > Tree.ActualHeight)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = "Đưa ra gốc";
            e.Handled = true;
        }
    }

    private void Blank_Drop(object sender, DragEventArgs e) => _droppedOnBlank = _dragging && e.GetPosition(Tree).Y > Tree.ActualHeight;

    /// <summary>Kéo-thả xong: cây đã tự chuyển dòng, lấy thư mục mới rồi dựng lại theo dữ liệu (thả lên hồ sơ = vào thư mục của nó).</summary>
    private void Tree_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        _dragging = false;
        if (args.DropResult == DataPackageOperation.None)
        {
            return; // Esc / thả ra ngoài cây
        }
        var target = _droppedOnBlank ? string.Empty : (args.NewParentItem as TreeViewNode)?.Content switch
        {
            ProfileFolder f => f.Path,
            ConnectionProfile p => p.Folder,
            _ => string.Empty,
        };
        foreach (var node in args.Items.OfType<TreeViewNode>())
        {
            if (node.Content is ConnectionProfile profile)
            {
                ViewModel.MoveProfile(profile, target);
            }
            else if (node.Content is ProfileFolder folder && !ProfileTree.IsUnder(target, folder.Path))
            {
                ViewModel.MoveFolder(folder.Path, target + "/" + folder.Name);
            }
        }
        ShowFolder();
        QueueRebuild();
    }

    private void Folder_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FolderNode(sender) is { } node)
        {
            node.IsExpanded = !node.IsExpanded;
            e.Handled = true;
        }
    }

    private void Folder_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (FolderNode(sender)?.Content is not ProfileFolder folder)
        {
            return;
        }
        var add = new MenuFlyoutItem { Text = "Thêm hồ sơ vào đây" };
        add.Click += (_, _) =>
        {
            ViewModel.SelectFolder(folder.Path);
            ViewModel.AddCommand.Execute(null);
        };
        var rename = new MenuFlyoutItem { Text = "Đổi tên thư mục..." };
        rename.Click += async (_, _) => await RenameFolderAsync(folder);
        var menu = new MenuFlyout { Items = { add, rename } };
        menu.ShowAt((UIElement)sender, e.GetPosition((UIElement)sender));
        e.Handled = true;
    }

    private static TreeViewNode? FolderNode(object sender) => (sender as FrameworkElement)?.DataContext as TreeViewNode;

    private async Task RenameFolderAsync(ProfileFolder folder)
    {
        var box = new TextBox { Text = folder.Name, IsSpellCheckEnabled = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "FolderNameBox");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Đổi tên thư mục",
            Content = box,
            PrimaryButtonText = "Đổi tên",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
        };
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = ProfileTree.Normalize(box.Text).Length > 0;
        box.Loaded += (_, _) => box.SelectAll();
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.MoveFolder(folder.Path, ProfileTree.Parent(folder.Path) + "/" + box.Text);
            ShowFolder();
        }
    }

    /// <summary>Đổi tên xong thì xếp lại cây theo tên mới (đang gõ thì không xếp).</summary>
    private void NameBox_LostFocus(object sender, RoutedEventArgs e) => QueueRebuild();

    // ----- Ô Thư mục -----

    private void ShowFolder()
    {
        _folderOwner = ViewModel.Selected;
        _folderShown = _folderOwner?.Folder ?? string.Empty;
        FolderBox.Text = _folderShown;
    }

    private void CommitFolder()
    {
        if (_folderOwner is { } owner && FolderBox.Text != _folderShown)
        {
            ViewModel.MoveProfile(owner, FolderBox.Text);
            ShowFolder();
        }
    }

    private void SuggestFolders(string filter) => FolderBox.ItemsSource = ProfileTree.FolderPaths(ViewModel.Profiles)
        .Where(f => f.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();

    private void FolderBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SuggestFolders(string.Empty);
        FolderBox.IsSuggestionListOpen = true;
    }

    private void FolderBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            SuggestFolders(sender.Text);
        }
    }

    private void FolderBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is string chosen)
        {
            sender.Text = chosen;
        }
        CommitFolder();
        sender.IsSuggestionListOpen = false;
        DispatcherQueue.TryEnqueue(() => sender.ItemsSource = null); // không thì danh sách còn mở lại sau khi bấm
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitFolder();
        FolderBox.IsSuggestionListOpen = false;
    }

    // ----- Ô khác -----

    /// <summary>PasswordBox không x:Bind 2 chiều tiện như TextBox → đổi hồ sơ thì đặt lại tay.</summary>
    private void ShowPassword()
    {
        _settingPassword = true;
        PasswordBox.Password = ViewModel.Selected?.Password ?? string.Empty;
        _settingPassword = false;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_settingPassword && ViewModel.Selected is { } profile)
        {
            profile.Password = PasswordBox.Password;
        }
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Xoá hồ sơ",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Xoá",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

/// <summary>Cây hồ sơ: mẫu hiện cho thư mục / hồ sơ.</summary>
public sealed partial class ProfileTreeTemplates : DataTemplateSelector
{
    public DataTemplate? Folder { get; set; }
    public DataTemplate? Profile { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is TreeViewNode { Content: ProfileFolder } ? Folder : Profile;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
