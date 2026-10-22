using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenCapture.Models;
using ScreenCapture.Services;
using ScreenCapture.ViewModels;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>
/// Đóng tab (×, Ctrl+W, Đóng tất cả, menu chuột phải của tab) + xoá file đã tự lưu khi đóng (2026-10-19, Cài đặt > Lưu ảnh >
/// Khi đóng ảnh đã tự lưu: Giữ / Hỏi / Luôn xoá - xoá = vào Thùng rác), menu chuột phải của tab (Mở thư mục chứa ảnh, Copy
/// đường dẫn, Lưu thành, Đóng / Đóng các tab khác / bên phải, Đóng và xoá file), nút Mở thư mục ở tab Tệp, kéo tab đổi chỗ.
/// Tắt Editor bằng nút X (nhớ tab cho lần sau) không xoá gì - xem <see cref="RequestCloseAsync"/>.
/// </summary>
public sealed partial class EditorWindow
{
    /// <summary>Cài đặt "Khi đóng ảnh đã tự lưu" hiện tại (launcher gán - đổi trong Cài đặt thì lần đóng sau dùng luôn).</summary>
    public Func<AutoSavedCloseAction> AutoSavedCloseActionSource { get; set; } = () => AutoSavedCloseAction.Keep;

    /// <summary>Tích "Không hỏi lại" trong hộp hỏi xoá → launcher ghi cài đặt mới (Giữ / Luôn xoá).</summary>
    public event EventHandler<AutoSavedCloseAction>? AutoSavedCloseActionChanged;

    /// <summary>Đang hỏi lưu / xoá cho tab - giữ Ctrl+W (phím tự lặp) không mở thêm ContentDialog thứ 2
    /// (WinUI văng lỗi khi 2 ContentDialog cùng mở).</summary>
    private bool _closingTab;

    private enum CloseDecision
    {
        Cancel,
        /// <summary>Không cần hỏi (đã lưu, không sửa gì).</summary>
        Clean,
        /// <summary>Vừa bấm Lưu - người dùng muốn giữ ảnh → không xoá file tự lưu.</summary>
        Saved,
        /// <summary>Bấm Không lưu (bỏ phần sửa).</summary>
        Discarded,
    }

    /// <summary>Đóng 1 tab (nút × hoặc Ctrl+W / Ctrl+F4).</summary>
    private Task<bool> CloseTabAsync(TabViewItem? tab) => tab is null ? Task.FromResult(false) : CloseTabsAsync([tab]);

    /// <summary>
    /// Đóng các tab: ảnh chưa lưu thì hỏi Lưu / Không lưu / Huỷ từng ảnh (Huỷ = thôi không đóng tab nào); rồi file đã tự
    /// lưu của các ảnh không vừa bấm Lưu → theo cài đặt (1 hộp hỏi cho cả loạt). <paramref name="deleteAutoSaved"/> (menu
    /// "Đóng và xoá file"): xoá luôn không hỏi theo cài đặt, chỉ hỏi khi còn phần sửa chưa lưu (sẽ mất).
    /// </summary>
    private async Task<bool> CloseTabsAsync(IReadOnlyList<TabViewItem> tabs, bool deleteAutoSaved = false)
    {
        var closing = tabs.Where(t => t.Tag is EditorViewModel).ToList();
        if (_closingTab || closing.Count == 0)
        {
            return false;
        }
        CommitInlineText(select: false);
        _closingTab = true;
        List<string> toDelete;
        try
        {
            var candidates = new List<EditorViewModel>();
            foreach (var tab in closing)
            {
                var vm = (EditorViewModel)tab.Tag;
                if (deleteAutoSaved)
                {
                    if (!await ConfirmDiscardAndDeleteAsync(vm))
                    {
                        return false;
                    }
                    candidates.Add(vm);
                    continue;
                }
                if (vm.NeedsSave && closing.Count > 1)
                {
                    DocumentTabs.SelectedItem = tab; // hỏi lưu ảnh nào thì cho thấy ảnh đó
                }
                var decision = await ConfirmCloseDocumentAsync(vm);
                if (decision == CloseDecision.Cancel)
                {
                    return false;
                }
                if (decision != CloseDecision.Saved)
                {
                    candidates.Add(vm);
                }
            }
            var files = deleteAutoSaved
                ? candidates.Select(vm => vm.DeletableAutoSavedFile).OfType<string>().ToList()
                : await AutoSavedFilesToDeleteAsync(candidates);
            if (files is null)
            {
                return false;
            }
            toDelete = files;
        }
        finally
        {
            _closingTab = false;
        }

        foreach (var tab in closing)
        {
            RememberClosedTab(tab);
            DocumentTabs.TabItems.Remove(tab);
            DetachDocument((EditorViewModel)tab.Tag);
        }
        FinishClosingTabs(toDelete);
        return true;
    }

    // ---------- Mở lại tab vừa đóng (Ctrl+Shift+T, menu tab, Danh sách ảnh, menu khay) ----------

    private readonly ClosedTabStack<EditorViewModel> _closedTabs;

    /// <summary>Cất tab sắp đóng (cả ảnh, hình vẽ, Undo, zoom) + vị trí của nó để mở lại được.</summary>
    private void RememberClosedTab(TabViewItem tab)
    {
        var vm = (EditorViewModel)tab.Tag;
        _closedTabs.Push(vm, DocumentTabs.TabItems.IndexOf(tab), vm.EstimatedBytes);
    }

    /// <summary>Gỡ các handler của cửa sổ này khỏi ảnh đã đóng - ảnh có thể được mở lại ở cửa sổ Editor khác.</summary>
    private void DetachDocument(EditorViewModel vm)
    {
        vm.Saved -= Document_Saved;
        vm.RequestRedraw -= ViewModel_RequestRedraw;
        vm.PropertyChanged -= ViewModel_PropertyChanged;
    }

    /// <summary>Mở lại tab đóng gần nhất vào đúng vị trí cũ và chọn nó. False nếu không còn tab nào.</summary>
    public bool ReopenClosedTab()
    {
        if (_closedTabs.Pop() is not { } entry)
        {
            if (_viewModel is not null)
            {
                _viewModel.StatusText = _closedTabs.MaxCount == 0
                    ? "Mở lại tab vừa đóng đang tắt (Cài đặt > Phiên làm việc: giữ tối đa 0 tab)."
                    : "Không còn tab vừa đóng để mở lại.";
            }
            return false;
        }
        var vm = entry.Item;
        vm.SetOwnerWindow(WindowNative.GetWindowHandle(this));
        var tab = AddTab(vm, entry.Index);
        SwitchTo(vm);
        DocumentTabs.SelectedItem = tab;
        UpdateTabPosition();
        vm.StatusText = _closedTabs.Count > 0
            ? $"Đã mở lại {vm.Title} - còn {_closedTabs.Count} tab vừa đóng (Ctrl+Shift+T)."
            : $"Đã mở lại {vm.Title}.";
        TrySaveSession();
        return true;
    }

    /// <summary>Chữ cho mục menu: "Mở lại tab vừa đóng (10:01:46)".</summary>
    private string ReopenMenuText() =>
        _closedTabs.Peek() is { } last ? $"Mở lại tab vừa đóng ({TabTitle.Short(last.Title, DateTime.Now)})" : "Mở lại tab vừa đóng";

    private void ReopenTabButton_Click(object sender, RoutedEventArgs e)
    {
        TabListFlyout.Hide();
        ReopenClosedTab();
    }

    /// <summary>Nút ở Danh sách ảnh: tên + bật / tắt theo danh sách tab vừa đóng (gọi khi mở Danh sách ảnh).</summary>
    private void UpdateReopenTabButton()
    {
        ReopenTabButton.IsEnabled = _closedTabs.Count > 0;
        ReopenTabText.Text = ReopenMenuText();
    }

    /// <summary>Đã gỡ tab: ghi lại phiên (file tạm của tab vừa đóng bị xoá luôn), xoá file tự lưu, hết tab thì đóng Editor.</summary>
    private void FinishClosingTabs(IReadOnlyList<string> toDelete)
    {
        UpdateTabPosition(); // đóng tab khác tab đang mở: thứ tự / tổng số vẫn đổi
        TrySaveSession();
        var message = DeleteAutoSavedFiles(toDelete);
        if (DocumentTabs.TabItems.Count == 0)
        {
            // Đóng tab cuối = đóng Editor (đã hỏi lưu rồi; thư mục phiên giờ rỗng).
            _forceClose = true;
            Close();
        }
        else if (message is not null)
        {
            _viewModel.StatusText = message;
        }
    }

    private async void CloseAllTabsButton_Click(object sender, RoutedEventArgs e) => await CloseAllTabsAsync();

    /// <summary>"Đóng tất cả": còn ảnh chưa lưu → hỏi Lưu tất cả (chọn 1 thư mục, lưu hết các ảnh chưa lưu vào đó, tên file =
    /// tên tab) / Đóng không lưu / Huỷ; rồi file đã tự lưu theo cài đặt (1 hộp hỏi cho cả loạt). Xong thì đóng mọi tab = đóng
    /// Editor, thư mục phiên tạm được dọn rỗng.</summary>
    private async Task CloseAllTabsAsync()
    {
        if (_closingTab)
        {
            return;
        }
        CommitInlineText(select: false);
        var all = DocumentTabs.TabItems.OfType<TabViewItem>().Select(t => (EditorViewModel)t.Tag).ToList();
        var unsaved = all.Where(vm => vm.NeedsSave).ToList();
        List<string>? toDelete;
        _closingTab = true;
        try
        {
            if (unsaved.Count > 0)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = "Đóng tất cả ảnh",
                    Content = $"Có {unsaved.Count} ảnh chưa được lưu. Chọn 1 thư mục để lưu tất cả trước khi đóng?",
                    PrimaryButtonText = "Lưu tất cả...",
                    SecondaryButtonText = "Đóng không lưu",
                    CloseButtonText = "Huỷ",
                    DefaultButton = ContentDialogButton.Primary,
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.None)
                {
                    return;
                }
                if (result == ContentDialogResult.Primary && !await SaveAllToFolderAsync(unsaved))
                {
                    return; // huỷ chọn thư mục hoặc lưu lỗi → giữ nguyên các tab
                }
            }
            // Ảnh vừa Lưu tất cả đã gắn file mới (không còn là file tự lưu) nên tự rơi khỏi danh sách xoá.
            toDelete = await AutoSavedFilesToDeleteAsync(all);
            if (toDelete is null)
            {
                return;
            }
        }
        finally
        {
            _closingTab = false;
        }

        // Như đóng lần lượt từ trái sang (mỗi tab ở vị trí 0 lúc đóng): Ctrl+Shift+T mở lại từ tab bên phải nhất, thứ tự cũ.
        foreach (var vm in all)
        {
            _closedTabs.Push(vm, 0, vm.EstimatedBytes);
            DetachDocument(vm);
        }
        DocumentTabs.TabItems.Clear();
        TrySaveSession(); // 0 tab → thư mục phiên tạm được dọn rỗng
        DeleteAutoSavedFiles(toDelete);
        _forceClose = true;
        Close();
    }

    /// <summary>Ảnh cần lưu → hỏi Lưu / Không lưu / Huỷ.</summary>
    private async Task<CloseDecision> ConfirmCloseDocumentAsync(EditorViewModel vm)
    {
        if (!vm.NeedsSave)
        {
            return CloseDecision.Clean;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Ảnh chưa được lưu",
            // File đã lưu bị xoá ngoài app (đã sửa gì thêm thì là "chưa lưu" bình thường): nói rõ, Lưu = ghi lại đúng chỗ cũ.
            Content = vm.FileMissing && !vm.UndoRedo.IsDirty
                ? $"File đã lưu không còn trên ổ (bị xoá hoặc đổi tên):\n{vm.FilePath}\n\nLưu lại ảnh \"{vm.Title}\" vào đó trước khi đóng?"
                : $"Lưu ảnh \"{vm.Title}\" trước khi đóng?",
            PrimaryButtonText = "Lưu",
            SecondaryButtonText = "Không lưu",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => await vm.SaveToFileAsync() ? CloseDecision.Saved : CloseDecision.Cancel,
            ContentDialogResult.Secondary => CloseDecision.Discarded,
            _ => CloseDecision.Cancel,
        };
    }

    /// <summary>"Đóng và xoá file": ảnh còn sửa sau lần lưu → hỏi (phần sửa mất cùng file). Trả true nếu được đóng.</summary>
    private async Task<bool> ConfirmDiscardAndDeleteAsync(EditorViewModel vm)
    {
        if (!vm.UndoRedo.IsDirty)
        {
            return true;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Bỏ phần đã sửa và xoá file?",
            Content = $"Ảnh \"{vm.Title}\" còn phần sửa chưa lưu. Đóng và xoá file sẽ mất cả phần sửa lẫn file đã tự lưu (file vào Thùng rác).",
            PrimaryButtonText = "Đóng và xoá file",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>File đã tự lưu của các ảnh sắp đóng cần xoá, theo cài đặt (Hỏi: 1 hộp cho cả loạt). Null = bấm Huỷ.</summary>
    private async Task<List<string>?> AutoSavedFilesToDeleteAsync(IEnumerable<EditorViewModel> documents)
    {
        var files = documents.Select(vm => vm.DeletableAutoSavedFile).OfType<string>().ToList();
        if (files.Count == 0)
        {
            return [];
        }
        switch (AutoSavedCloseActionSource())
        {
            case AutoSavedCloseAction.Keep:
                return [];
            case AutoSavedCloseAction.Delete:
                return files;
        }

        var names = files.Take(5).Select(Path.GetFileName).ToList();
        var list = files.Count == 1 ? files[0] : string.Join("\n", names) + (files.Count > names.Count ? $"\n… và {files.Count - names.Count} file khác" : string.Empty);
        var dontAsk = new CheckBox { Content = "Không hỏi lại (đổi lại ở Cài đặt > Lưu ảnh)" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dontAsk, "Không hỏi lại");
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = files.Count == 1 ? "Xoá file đã tự lưu?" : $"Xoá {files.Count} file đã tự lưu?",
            Content = SharedUI.Helpers.DialogContent.Scrollable(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = list, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                    new TextBlock { Text = "File được chuyển vào Thùng rác (lấy lại được).", Opacity = 0.75 },
                    dontAsk,
                },
            }),
            PrimaryButtonText = files.Count == 1 ? "Đóng và xoá file" : $"Đóng và xoá {files.Count} file",
            SecondaryButtonText = "Đóng, giữ file",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Secondary,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
        {
            return null;
        }
        bool delete = result == ContentDialogResult.Primary;
        if (dontAsk.IsChecked == true)
        {
            AutoSavedCloseActionChanged?.Invoke(this, delete ? AutoSavedCloseAction.Delete : AutoSavedCloseAction.Keep);
        }
        return delete ? files : [];
    }

    // ---------- Menu chuột phải của tab (Shift+F10 / phím menu cũng mở) ----------

    /// <summary>Gắn menu cho 1 tab (AddTab gọi). Bật / tắt / ẩn từng mục lúc mở menu theo trạng thái hiện tại của ảnh.</summary>
    private void AttachTabMenu(TabViewItem tab)
    {
        var vm = (EditorViewModel)tab.Tag;
        MenuFlyoutItem Item(string text, string glyph, Action click, string? keys = null)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            if (keys is not null)
            {
                item.KeyboardAcceleratorTextOverride = keys;
            }
            item.Click += (_, _) => click();
            return item;
        }

        var openFolder = Item("Mở thư mục chứa ảnh", "", () => OpenContainingFolder(vm));
        var copyPath = Item("Copy đường dẫn file", "", () => CopyFilePath(vm));
        var saveAs = Item("Lưu thành…", "", () => SaveTabAs(tab), "Ctrl+Shift+S");
        var close = Item("Đóng", "", () => _ = CloseTabAsync(tab), "Ctrl+W");
        var closeOthers = Item("Đóng các tab khác", "", () => _ = CloseTabsAsync(OtherTabs(tab)));
        var closeRight = Item("Đóng các tab bên phải", "", () => _ = CloseTabsAsync(TabsRightOf(tab)));
        var closeDelete = Item("Đóng và xoá file", "", () => _ = CloseTabsAsync([tab], deleteAutoSaved: true));
        ToolTipService.SetToolTip(closeDelete, "Đóng ảnh và chuyển file đã tự lưu của nó vào Thùng rác");
        var reopen = Item("Mở lại tab vừa đóng", "", () => ReopenClosedTab(), "Ctrl+Shift+T");

        var menu = new MenuFlyout
        {
            Items = { openFolder, copyPath, saveAs, new MenuFlyoutSeparator(), close, closeOthers, closeRight, closeDelete, new MenuFlyoutSeparator(), reopen },
        };
        menu.Opening += (_, _) =>
        {
            var folder = ContainingFolder(vm);
            openFolder.IsEnabled = folder is not null;
            openFolder.Text = vm.FilePath is null ? "Mở thư mục chứa ảnh (ảnh chưa lưu)" : "Mở thư mục chứa ảnh";
            ToolTipService.SetToolTip(openFolder, vm.FilePath ?? "Ảnh chưa lưu ra file");
            copyPath.IsEnabled = vm.FilePath is not null;
            closeOthers.IsEnabled = DocumentTabs.TabItems.Count > 1;
            closeRight.IsEnabled = TabsRightOf(tab).Count > 0;
            closeDelete.Visibility = vm.DeletableAutoSavedFile is not null ? Visibility.Visible : Visibility.Collapsed;
            reopen.IsEnabled = _closedTabs.Count > 0;
            reopen.Text = ReopenMenuText();
        };
        tab.ContextFlyout = menu;
    }

    private List<TabViewItem> OtherTabs(TabViewItem tab) =>
        DocumentTabs.TabItems.OfType<TabViewItem>().Where(t => !ReferenceEquals(t, tab)).ToList();

    private List<TabViewItem> TabsRightOf(TabViewItem tab) =>
        DocumentTabs.TabItems.OfType<TabViewItem>().SkipWhile(t => !ReferenceEquals(t, tab)).Skip(1).ToList();

    /// <summary>Lưu thành cho tab bất kỳ (menu chuột phải): chuyển sang tab đó trước để thấy đang lưu ảnh nào.</summary>
    private void SaveTabAs(TabViewItem tab)
    {
        DocumentTabs.SelectedItem = tab;
        ((EditorViewModel)tab.Tag).SaveAsCommand.Execute(null);
    }

    /// <summary>Thư mục mở được cho ảnh: file còn → thư mục của file; file đã bị xoá / đổi tên → thư mục cha nếu còn;
    /// ảnh chưa lưu → null.</summary>
    private static string? ContainingFolder(EditorViewModel vm) =>
        vm.FilePath is { } path && Path.GetDirectoryName(path) is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;

    /// <summary>Mở Explorer ở thư mục chứa ảnh, chọn sẵn file (file đã mất thì chỉ mở thư mục).</summary>
    private void OpenContainingFolder(EditorViewModel vm)
    {
        if (ContainingFolder(vm) is not { } folder)
        {
            _viewModel.StatusText = vm.FilePath is null
                ? "Ảnh chưa lưu ra file - chưa có thư mục để mở."
                : $"Không còn thư mục chứa ảnh: {Path.GetDirectoryName(vm.FilePath)}";
            return;
        }
        try
        {
            var args = File.Exists(vm.FilePath) ? $"/select,\"{vm.FilePath}\"" : $"\"{folder}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            if (!File.Exists(vm.FilePath))
            {
                _viewModel.StatusText = $"File không còn (bị xoá hoặc đổi tên) - đã mở thư mục {folder}";
            }
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Không mở được thư mục {Folder}", folder);
            _viewModel.StatusText = $"Không mở được thư mục: {ex.Message}";
        }
    }

    private void CopyFilePath(EditorViewModel vm)
    {
        if (vm.FilePath is not { } path)
        {
            return;
        }
        _clipboardService.CopyText(path);
        _viewModel.StatusText = $"Đã copy đường dẫn: {path}";
    }

    // ---------- Nút "Mở thư mục" ở tab Tệp ----------

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenContainingFolder(_viewModel);

    /// <summary>Bật / tắt nút Mở thư mục + tooltip đường dẫn theo ảnh đang mở (đổi tab, vừa lưu).</summary>
    private void UpdateOpenFolderButton()
    {
        if (_viewModel is not { } vm)
        {
            return;
        }
        OpenFolderButton.IsEnabled = ContainingFolder(vm) is not null;
        ToolTipService.SetToolTip(OpenFolderButton, vm.FilePath is { } path
            ? $"Mở thư mục chứa ảnh trong Explorer (chọn sẵn file):\n{path}"
            : "Mở thư mục chứa ảnh - ảnh chưa lưu ra file");
    }

    // ---------- Kéo tab đổi chỗ ----------

    /// <summary>Thả tab ở chỗ mới: "Ảnh x / n" + ghi phiên tạm theo thứ tự mới (mở lại app giữ thứ tự). Kéo ra ngoài thanh tab
    /// thì TabView tự bỏ qua (không tách thành cửa sổ riêng).</summary>
    private void DocumentTabs_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        UpdateTabPosition();
        TrySaveSession();
    }

    /// <summary>Chuyển các file vào Thùng rác. Trả câu báo cho thanh trạng thái (null nếu không xoá gì).</summary>
    private static string? DeleteAutoSavedFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            return null;
        }
        var failed = new List<string>();
        foreach (var file in files)
        {
            try
            {
                RecycleBin.Send(file);
                Log.LogInformation("Đóng ảnh: chuyển file tự lưu vào Thùng rác {File}", file);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Không xoá được file tự lưu {File}", file);
                failed.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return failed.Count == 0
            ? files.Count == 1 ? $"Đã chuyển {Path.GetFileName(files[0])} vào Thùng rác." : $"Đã chuyển {files.Count} file tự lưu vào Thùng rác."
            : $"Không xoá được {failed.Count} file: {string.Join("; ", failed)}";
    }
}
