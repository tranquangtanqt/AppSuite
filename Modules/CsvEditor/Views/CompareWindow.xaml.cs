using System.Runtime.InteropServices;
using CsvEditor.Models;
using CsvEditor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CsvEditor.Views;

/// <summary>1 bảng đưa vào so sánh. <see cref="IsEditor"/>: là bảng đang mở trong cửa sổ chính (bấm đúp tới được ô).</summary>
public sealed record CompareTable(string Path, string Name, List<CsvColumn> Columns, List<CsvRow> Rows, bool IsEditor);

/// <summary>1 dòng hiển thị của bảng kết quả (màu tính sẵn theo trạng thái).</summary>
public sealed class CompareRow
{
    public CompareRow(CompareEntry entry, bool showKey, bool topLine)
    {
        Entry = entry;
        Key = showKey ? entry.Key : "";
        TopLine = topLine ? new Thickness(0, 1, 0, 0) : new Thickness(0);
        ColumnBrush = Res(entry.Column == CsvCompare.WholeRecord ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        ValueA = entry.Status == CompareStatus.OnlyB ? "-" : Show(entry.ValueA);
        ValueB = entry.Status == CompareStatus.OnlyA ? "-" : Show(entry.ValueB);
        (StatusLabel, PillBackground, PillForeground, BrushA, BrushB) = entry.Status switch
        {
            CompareStatus.Modified => ("Sửa", Res("SystemFillColorCautionBackgroundBrush"), Res("SystemFillColorCautionBrush"),
                Res("TextFillColorSecondaryBrush"), Res("SystemFillColorCautionBrush")),
            CompareStatus.OnlyB => ("Chỉ có ở B", Res("SystemFillColorSuccessBackgroundBrush"), Res("SystemFillColorSuccessBrush"),
                Res("TextFillColorTertiaryBrush"), Res("SystemFillColorSuccessBrush")),
            CompareStatus.OnlyA => ("Chỉ có ở A", Res("SystemFillColorCriticalBackgroundBrush"), Res("SystemFillColorCriticalBrush"),
                Res("SystemFillColorCriticalBrush"), Res("TextFillColorTertiaryBrush")),
            _ => ("Giống", Res("SubtleFillColorSecondaryBrush"), Res("TextFillColorSecondaryBrush"),
                Res("TextFillColorSecondaryBrush"), Res("TextFillColorSecondaryBrush")),
        };
    }

    private static string Show(string v) => v.Length == 0 ? "(trống)" : v.Replace("\r\n", " ⏎ ").Replace('\n', '⏎').Replace('\r', '⏎');

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public CompareEntry Entry { get; }
    public string Key { get; }
    public Thickness TopLine { get; }
    public string ColumnName => Entry.Column;
    public Brush ColumnBrush { get; }
    public string ValueA { get; }
    public string ValueB { get; }
    public Brush BrushA { get; }
    public Brush BrushB { get; }
    public string StatusLabel { get; }
    public Brush PillBackground { get; }
    public Brush PillForeground { get; }
}

/// <summary>
/// So sánh 2 file CSV: hộp thoại chọn file B + cách ghép bản ghi (<see cref="StartAsync"/>), rồi cửa sổ kết quả - mỗi ô khác
/// 1 dòng, 4 ô số (Giống / Sửa / Chỉ có ở B / Chỉ có ở A) là bộ lọc, tìm, F8 tới chỗ khác kế tiếp, bấm đúp tới ô trong file A,
/// Xuất Excel. Phần so ở <see cref="CsvCompare"/>.
/// </summary>
public sealed partial class CompareWindow : Window
{
    private readonly ICsvFileService _fileService;
    private readonly Func<CsvRow, int, bool> _goToCell;
    private CompareTable _a;
    private CompareTable _b;
    private CompareOptions _options;
    private CompareResult? _result;
    private List<CompareRow> _rows = new();
    private readonly HashSet<CompareStatus> _shown = new() { CompareStatus.Modified, CompareStatus.OnlyB, CompareStatus.OnlyA };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _cts;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly bool _hasHeader;

    private CompareWindow(ICsvFileService fileService, Func<CsvRow, int, bool> goToCell, CompareTable a, CompareTable b, CompareOptions options, bool hasHeader)
    {
        _fileService = fileService;
        _hasHeader = hasHeader;
        _goToCell = goToCell;
        _a = a;
        _b = b;
        _options = options;
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        App.ApplyTitleBarTheme(AppWindow);
        // 1000 × 600 logical, nhưng không quá 95% vùng làm việc của màn hình (màn hình nhỏ / DPI cao), đặt giữa màn hình.
        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min((int)(1000 * scale), (int)(work.Width * 0.95));
        var height = Math.Min((int)(600 * scale), (int)(work.Height * 0.95));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            Refresh();
        };
        Closed += (_, _) => _cts?.Cancel();
        _ = RunAsync();
    }

    /// <summary>Hỏi file B + cách so, đọc file B rồi mở cửa sổ kết quả. A = bảng đang mở trong cửa sổ chính.
    /// <paramref name="goToCell"/>: tới ô (dòng, cột) trong bảng chính - false nếu dòng đang bị lọc / đã xoá.</summary>
    public static async Task<CompareWindow?> StartAsync(Window owner, XamlRoot root, ICsvFileService fileService, CsvDocument docA,
        IReadOnlyList<string> recent, Func<CsvRow, int, bool> goToCell)
    {
        var a = new CompareTable(docA.FilePath, docA.FileName, docA.Columns.ToList(), docA.Rows.ToList(), true);
        var suggested = await Task.Run(() => CsvCompare.SuggestKey(a.Columns, a.Rows));
        var setup = await ShowSetupAsync(owner, root, a, null, null, suggested, recent);
        if (setup is null)
        {
            return null;
        }

        var (path, options) = setup.Value;
        try
        {
            var b = await LoadAsync(fileService, path, docA.HasHeader);
            var missing = options.KeyColumns.Where(k => !b.Columns.Any(c => string.Equals(c.Name.Trim(), k.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException($"File B ({b.Name}) không có cột khoá {string.Join(", ", missing.Select(m => $"\"{m}\""))}.\n\n"
                    + "Cột của file B: " + string.Join(", ", b.Columns.Select(c => c.Name)));
            }
            var window = new CompareWindow(fileService, goToCell, a, b, options, docA.HasHeader);
            window.Activate();
            return window;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await new ContentDialog
            {
                XamlRoot = root,
                Title = "Không so sánh được",
                Content = SharedUI.Helpers.DialogContent.Scrollable(ex.Message),
                CloseButtonText = "Đóng",
            }.ShowAsync();
            return null;
        }
    }

    private static async Task<CompareTable> LoadAsync(ICsvFileService fileService, string path, bool hasHeader)
    {
        var result = await fileService.OpenAsync(path, null, null, _ => Task.FromResult(true), null, CancellationToken.None, hasHeader);
        return new CompareTable(path, Path.GetFileName(path), result.Document.Columns, result.Document.Rows.ToList(), false);
    }

    // ----- So và hiện kết quả -----

    private async Task RunAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var (a, b, options) = (_a, _b, _options);

        FileAText.Text = $"A: {a.Name} ({a.Rows.Count:N0} dòng · {a.Columns.Count} cột)";
        FileBText.Text = $"B: {b.Name} ({b.Rows.Count:N0} dòng · {b.Columns.Count} cột)";
        KeyText.Text = options.ByOrder ? "Không có cột khoá: căn theo thứ tự dòng" : "Khoá: " + string.Join(" + ", options.KeyColumns);
        KeyHeader.Text = options.ByOrder ? "Dòng" : string.Join(" + ", options.KeyColumns);
        Title = $"So sánh {a.Name} ⇄ {b.Name} - CsvEditor";
        BusyRing.IsActive = true;
        FooterText.Text = "Đang so sánh...";
        ResultList.ItemsSource = null;
        EmptyText.Visibility = Visibility.Collapsed;
        try
        {
            _result = await Task.Run(() => CsvCompare.Compare(a.Columns, a.Rows, b.Columns, b.Rows, options, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _result = null;
            BusyRing.IsActive = false;
            FooterText.Text = "";
            EmptyText.Text = "Không so sánh được: " + ex.Message;
            EmptyText.Visibility = Visibility.Visible;
            return;
        }
        BusyRing.IsActive = false;

        var r = _result;
        SameCount.Text = r.Same.ToString("N0");
        ModifiedCount.Text = r.Modified.ToString("N0");
        ModifiedLabel.Text = $"Sửa ({r.ChangedCells:N0} ô)";
        OnlyBCount.Text = r.OnlyB.ToString("N0");
        OnlyACount.Text = r.OnlyA.ToString("N0");

        var notes = new List<string> { r.Different == 0 ? "Hai file giống nhau (theo cách so đã chọn)." : $"{r.Different:N0} bản ghi khác." };
        if (r.TooDifferent)
        {
            notes.Add("2 file khác nhau quá nhiều để căn theo thứ tự: chỉ ghép dòng giống hệt, không đoán Sửa - nên chọn cột khoá.");
        }
        if (r.ColumnsOnlyA.Count > 0)
        {
            notes.Add($"Cột chỉ có ở A: {string.Join(", ", r.ColumnsOnlyA)}.");
        }
        if (r.ColumnsOnlyB.Count > 0)
        {
            notes.Add($"Cột chỉ có ở B: {string.Join(", ", r.ColumnsOnlyB)}.");
        }
        if (r.DuplicateKeysA + r.DuplicateKeysB > 0)
        {
            notes.Add($"Khoá trùng: A {r.DuplicateKeysA:N0}, B {r.DuplicateKeysB:N0} giá trị (ghép theo thứ tự xuất hiện).");
        }
        if (options.IgnoredColumns.Count > 0)
        {
            notes.Add($"Bỏ qua: {string.Join(", ", options.IgnoredColumns)}.");
        }
        notes.Add(a.IsEditor ? "Bấm đúp / Enter: tới ô trong file A. F8: chỗ khác kế tiếp." : "F8: chỗ khác kế tiếp.");
        FooterText.Text = string.Join("  ", notes);
        Refresh();
    }

    private void Refresh()
    {
        if (_result is null)
        {
            return;
        }
        var q = SearchBox.Text.Trim();
        var rows = new List<CompareRow>();
        string? previousKey = null;
        foreach (var e in _result.Entries)
        {
            if (!_shown.Contains(e.Status))
            {
                continue;
            }
            if (q.Length > 0 && !(Has(e.Key, q) || Has(e.Column, q) || Has(e.ValueA, q) || Has(e.ValueB, q)))
            {
                continue;
            }
            var newGroup = e.Key != previousKey || e.IsGroupStart;
            rows.Add(new CompareRow(e, newGroup, newGroup && rows.Count > 0));
            previousKey = e.Key;
        }
        _rows = rows;
        ResultList.ItemsSource = rows;
        ShownText.Text = $"Hiện {rows.Count:N0} dòng";
        EmptyText.Text = _result.Entries.Count == 0 ? "Cả 2 file đều không có bản ghi nào."
            : _result.Different == 0 && !_shown.Contains(CompareStatus.Same) ? "Hai file giống nhau. Bấm ô Giống để xem các bản ghi."
            : "Không có dòng nào khớp bộ lọc / ô tìm.";
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (tile, status, brush) in new[]
        {
            (TileSame, CompareStatus.Same, "TextFillColorSecondaryBrush"),
            (TileModified, CompareStatus.Modified, "SystemFillColorCautionBrush"),
            (TileOnlyB, CompareStatus.OnlyB, "SystemFillColorSuccessBrush"),
            (TileOnlyA, CompareStatus.OnlyA, "SystemFillColorCriticalBrush"),
        })
        {
            var on = _shown.Contains(status);
            tile.BorderBrush = on ? (Brush)Application.Current.Resources[brush] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            tile.Opacity = on ? 1 : 0.6;
            ToolTipService.SetToolTip(tile, on ? "Đang hiện - bấm để ẩn" : "Đang ẩn - bấm để hiện");
        }
    }

    private static bool Has(string s, string q) => s.Contains(q, StringComparison.OrdinalIgnoreCase);

    private string ShownDescription()
    {
        var names = new[] { (CompareStatus.Same, "Giống"), (CompareStatus.Modified, "Sửa"), (CompareStatus.OnlyB, "Chỉ có ở B"), (CompareStatus.OnlyA, "Chỉ có ở A") }
            .Where(p => _shown.Contains(p.Item1)).Select(p => p.Item2);
        var q = SearchBox.Text.Trim();
        return string.Join(", ", names) + (q.Length > 0 ? $"; tìm \"{q}\"" : "");
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        var status = Enum.Parse<CompareStatus>((string)((Button)sender).Tag);
        if (!_shown.Remove(status))
        {
            _shown.Add(status);
        }
        Refresh();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.SelectAll();
        args.Handled = true;
    }

    private void NextDifference_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        MoveToDifference(+1);
        args.Handled = true;
    }

    private void PreviousDifference_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        MoveToDifference(-1);
        args.Handled = true;
    }

    /// <summary>F8 / Shift+F8: chọn dòng đầu của bản ghi khác kế tiếp / trước đó (bỏ qua bản ghi Giống), hết thì quay vòng.</summary>
    private void MoveToDifference(int step)
    {
        if (_rows.Count == 0)
        {
            return;
        }
        var i = ResultList.SelectedIndex;
        for (var n = 0; n < _rows.Count; n++)
        {
            i = i < 0 ? (step > 0 ? 0 : _rows.Count - 1) : (i + step + _rows.Count) % _rows.Count;
            var row = _rows[i];
            if (row.Key.Length > 0 && row.Entry.Status != CompareStatus.Same)
            {
                ResultList.SelectedIndex = i;
                ResultList.ScrollIntoView(row);
                (ResultList.ContainerFromIndex(i) as ListViewItem)?.Focus(FocusState.Keyboard);
                return;
            }
        }
    }

    private void ResultList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => GoToSelected();

    // PreviewKeyDown: KeyDown không tới được vì ListViewItem tự xử lý Enter.
    private void ResultList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            GoToSelected();
            e.Handled = true;
        }
    }

    /// <summary>Tới ô của dòng đang chọn trong bảng chính (chỉ khi A là file đang mở).</summary>
    private void GoToSelected()
    {
        if (ResultList.SelectedItem is not CompareRow row)
        {
            return;
        }
        if (!_a.IsEditor)
        {
            FooterText.Text = "A đang là file khác (đã đổi chỗ): bấm ⇄ để A là file đang mở rồi mới tới được ô.";
        }
        else if (row.Entry.RowA < 0)
        {
            FooterText.Text = "Bản ghi này chỉ có ở B - không có trong file đang mở.";
        }
        else if (!_goToCell(_a.Rows[row.Entry.RowA], row.Entry.ColumnA))
        {
            FooterText.Text = "Không tới được: dòng này đang bị lọc hoặc đã xoá ở cửa sổ chính.";
        }
    }

    private async void SwapButton_Click(object sender, RoutedEventArgs e)
    {
        (_a, _b) = (_b, _a);
        await RunAsync();
    }

    private async void OptionsButton_Click(object sender, RoutedEventArgs e)
    {
        var setup = await ShowSetupAsync(this, Content.XamlRoot, _a, _b.Path, _options, null, Array.Empty<string>());
        if (setup is null)
        {
            return;
        }
        var (path, options) = setup.Value;
        try
        {
            if (!string.Equals(path, _b.Path, StringComparison.OrdinalIgnoreCase))
            {
                _b = await LoadAsync(_fileService, path, _hasHeader);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Không mở được file B", ex.Message);
            return;
        }
        _options = options;
        await RunAsync();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null)
        {
            return;
        }
        var picker = new FileSavePicker { SuggestedFileName = $"So sánh {Path.GetFileNameWithoutExtension(_a.Name)} - {Path.GetFileNameWithoutExtension(_b.Name)}" };
        picker.FileTypeChoices.Add("Excel", new List<string> { ".xlsx" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        var entries = _rows.Select(r => r.Entry).ToList();
        var summary = new CompareSummary(_a.Name, _a.Rows.Count, _b.Name, _b.Rows.Count, _options, _result, ShownDescription());
        try
        {
            BusyRing.IsActive = true;
            var written = await Task.Run(() => CompareXlsxExporter.Export(file.Path, summary, entries, CancellationToken.None));
            FooterText.Text = $"Đã xuất {written:N0} dòng ra {file.Path}"
                + (written < entries.Count ? $" (bỏ {entries.Count - written:N0} dòng vượt giới hạn của Excel)." : ".");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Không xuất được Excel", ex.Message);
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private async Task ShowErrorAsync(string title, string message) =>
        await new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = SharedUI.Helpers.DialogContent.Scrollable(message), CloseButtonText = "Đóng" }.ShowAsync();

    // ----- Hộp thoại chọn file B + cách ghép bản ghi -----

    /// <param name="fixedB">Đường dẫn file B đang so (mở từ Tuỳ chọn...), null = chưa chọn.</param>
    /// <param name="suggested">Cột khoá gợi ý (rỗng = không tìm được → chọn sẵn căn theo thứ tự dòng); null khi đã có <paramref name="current"/>.</param>
    private static async Task<(string Path, CompareOptions Options)?> ShowSetupAsync(Window owner, XamlRoot root, CompareTable a,
        string? fixedB, CompareOptions? current, IReadOnlyList<string>? suggested, IReadOnlyList<string> recent)
    {
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var panel = new StackPanel { Spacing = 10, Width = 500 };
        panel.Children.Add(new TextBlock { Text = $"File A: {a.Name} - {a.Rows.Count:N0} dòng · {a.Columns.Count} cột", TextWrapping = TextWrapping.Wrap });

        // File B
        var pathBox = new TextBox { Header = "File B (để so với A)", PlaceholderText = "Chọn, kéo-thả hoặc dán đường dẫn file CSV / TSV", Text = fixedB ?? "", AllowDrop = true };
        Name(pathBox, "File B");
        pathBox.DragOver += (_, e) => e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        pathBox.Drop += async (_, e) =>
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
                && (await e.DataView.GetStorageItemsAsync()).FirstOrDefault() is { } item)
            {
                pathBox.Text = item.Path;
            }
        };
        var pick = new Button { Content = "Chọn...", VerticalAlignment = VerticalAlignment.Bottom };
        pick.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            foreach (var ext in new[] { ".csv", ".tsv", ".txt" })
            {
                picker.FileTypeFilter.Add(ext);
            }
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
            if (await picker.PickSingleFileAsync() is { } f)
            {
                pathBox.Text = f.Path;
            }
        };
        var fileRow = new Grid { ColumnSpacing = 8 };
        fileRow.ColumnDefinitions.Add(new ColumnDefinition());
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fileRow.Children.Add(pathBox);
        Grid.SetColumn(pick, 1);
        fileRow.Children.Add(pick);
        if (recent.Count > 0)
        {
            var recentButton = new DropDownButton { Content = new FontIcon { Glyph = "", FontSize = 14 }, VerticalAlignment = VerticalAlignment.Bottom };
            ToolTipService.SetToolTip(recentButton, "File gần đây");
            Name(recentButton, "File gần đây");
            var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
            foreach (var p in recent)
            {
                var item = new MenuFlyoutItem { Text = Path.GetFileName(p) };
                ToolTipService.SetToolTip(item, p);
                item.Click += (_, _) => pathBox.Text = p;
                menu.Items.Add(item);
            }
            recentButton.Flyout = menu;
            Grid.SetColumn(recentButton, 2);
            fileRow.Children.Add(recentButton);
        }
        panel.Children.Add(fileRow);

        // Ghép bản ghi: theo cột khoá (tích 1 hoặc nhiều cột) / không có cột khoá
        var initialKeys = current?.KeyColumns ?? suggested ?? Array.Empty<string>();
        var byKey = new RadioButton { Content = "Theo cột khoá (tích 1 hoặc nhiều cột):", GroupName = "CompareMode", IsChecked = initialKeys.Count > 0 };
        var byOrder = new RadioButton { Content = "Không có cột khoá: tự căn theo thứ tự dòng", GroupName = "CompareMode", IsChecked = initialKeys.Count == 0 };
        panel.Children.Add(new TextBlock { Text = "Ghép bản ghi A với B", Margin = new Thickness(0, 4, 0, -6) });
        panel.Children.Add(byKey);

        var keyBoxes = new List<CheckBox>();
        var keyWrap = new SharedUI.Controls.WrapPanel { Spacing = 4 };
        foreach (var c in a.Columns)
        {
            var cb = new CheckBox { Content = c.Name, MinWidth = 0, Margin = new Thickness(0, 0, 8, 0), IsChecked = initialKeys.Contains(c.Name, StringComparer.OrdinalIgnoreCase) };
            Name(cb, "Khoá: " + c.Name);
            keyBoxes.Add(cb);
            keyWrap.Children.Add(cb);
        }
        var keyScroll = new ScrollViewer { Content = keyWrap, MaxHeight = 96, Margin = new Thickness(28, -4, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(keyScroll);
        var checkText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28, 0, 0, 0), FontSize = 12 };
        panel.Children.Add(checkText);
        panel.Children.Add(byOrder);
        var orderNote = new TextBlock
        {
            Text = (suggested is { Count: 0 } && current is null ? "Không tìm được cột (hay tổ hợp ≤ 3 cột) nào không trùng nên chọn sẵn cách này. " : "")
                + "Dòng giống hệt được căn với nhau (chèn / xoá dòng ở giữa không làm lệch), dòng giống ≥ 1 nửa số cột tính là Sửa. "
                + "File B bị sắp xếp lại thì nên dùng cột khoá.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = secondary,
            Margin = new Thickness(28, -4, 0, 0),
        };
        panel.Children.Add(orderNote);

        // Cách so
        var trim = new CheckBox { Content = "Bỏ khoảng trắng đầu / cuối ô", IsChecked = current?.TrimSpaces ?? true };
        var ignoreCase = new CheckBox { Content = "Không phân biệt hoa thường", IsChecked = current?.IgnoreCase ?? false };
        var numbers = new CheckBox { Content = "Số bằng nhau coi là giống (1.0 = 1)", IsChecked = current?.NumbersByValue ?? true };
        var how = new StackPanel();
        how.Children.Add(new TextBlock { Text = "Cách so", Margin = new Thickness(0, 4, 0, 2) });
        how.Children.Add(trim);
        how.Children.Add(ignoreCase);
        how.Children.Add(numbers);
        panel.Children.Add(how);

        // Bỏ qua cột (cột khoá không tích được)
        panel.Children.Add(new TextBlock { Text = "Bỏ qua cột (không so, vd ngày cập nhật):", Margin = new Thickness(0, 4, 0, 0) });
        var ignoreBoxes = new List<CheckBox>();
        var ignoreWrap = new SharedUI.Controls.WrapPanel { Spacing = 4 };
        foreach (var c in a.Columns)
        {
            var cb = new CheckBox { Content = c.Name, MinWidth = 0, Margin = new Thickness(0, 0, 8, 0), IsChecked = current?.IgnoredColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase) ?? false };
            Name(cb, "Bỏ qua: " + c.Name);
            ignoreBoxes.Add(cb);
            ignoreWrap.Children.Add(cb);
        }
        panel.Children.Add(new ScrollViewer { Content = ignoreWrap, MaxHeight = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        panel.Children.Add(error);

        List<string> Keys() => byKey.IsChecked == true ? keyBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList() : new List<string>();
        CompareOptions Options() => new(Keys(),
            ignoreBoxes.Where(b => b.IsChecked == true && b.IsEnabled).Select(b => (string)b.Content).ToList(),
            trim.IsChecked == true, ignoreCase.IsChecked == true, numbers.IsChecked == true);

        // Kiểm tra trùng chạy nền mỗi lần đổi khoá / cách so; chỉ kết quả của lần đổi mới nhất được hiện.
        var version = 0;
        async void Update()
        {
            var mode = byKey.IsChecked == true;
            keyScroll.Opacity = mode ? 1 : 0.5;
            keyWrap.IsHitTestVisible = mode;
            checkText.Visibility = mode ? Visibility.Visible : Visibility.Collapsed;
            orderNote.Opacity = mode ? 0.6 : 1;
            var keys = Keys();
            for (var i = 0; i < ignoreBoxes.Count; i++)
            {
                ignoreBoxes[i].IsEnabled = !keys.Contains(a.Columns[i].Name);
            }
            if (!mode)
            {
                return;
            }
            if (keys.Count == 0)
            {
                checkText.Text = "Chưa tích cột khoá nào.";
                checkText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                return;
            }
            var my = ++version;
            checkText.Text = "Đang kiểm tra cột khoá...";
            checkText.Foreground = secondary;
            var options = Options();
            var check = await Task.Run(() => CsvCompare.CheckKey(a.Columns, a.Rows, keys, options));
            if (my != version)
            {
                return;
            }
            var label = string.Join(" + ", keys);
            if (check.IsUnique)
            {
                checkText.Text = $"✓ {label}: {check.Rows:N0} bản ghi ở A, không trùng.";
                checkText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            }
            else
            {
                var parts = new List<string>();
                if (check.DuplicateValues > 0)
                {
                    parts.Add(check.Example is { } ex ? $"{check.DuplicateValues:N0} giá trị bị trùng (vd \"{ex}\" có {check.ExampleCount:N0} dòng)" : $"{check.DuplicateValues:N0} giá trị bị trùng");
                }
                if (check.EmptyKeys > 0)
                {
                    parts.Add($"{check.EmptyKeys:N0} dòng trống khoá");
                }
                checkText.Text = $"⚠ {label} ở A: {string.Join(", ", parts)}. Nên tích thêm cột để phân biệt - vẫn so được, bản ghi cùng khoá ghép theo thứ tự xuất hiện.";
                checkText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            }
        }
        foreach (var cb in keyBoxes)
        {
            cb.Click += (_, _) => Update();
        }
        byKey.Checked += (_, _) => Update();
        byOrder.Checked += (_, _) => Update();
        trim.Click += (_, _) => Update();
        ignoreCase.Click += (_, _) => Update();
        numbers.Click += (_, _) => Update();
        Update();

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "So sánh với file khác",
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "So sánh",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            string? message = null;
            var path = pathBox.Text.Trim().Trim('"');
            if (path.Length == 0 || !File.Exists(path))
            {
                message = path.Length == 0 ? "Chưa chọn file B." : "Không thấy file B: " + path;
            }
            if (message is null && byKey.IsChecked == true && Keys().Count == 0)
            {
                message = "Tích ít nhất 1 cột khoá, hoặc chọn \"Không có cột khoá\".";
            }
            if (message is not null)
            {
                error.Text = message;
                error.Visibility = Visibility.Visible;
                e.Cancel = true;
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }
        return (pathBox.Text.Trim().Trim('"'), Options());
    }

    private static void Name(DependencyObject o, string name) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(o, name);
}
