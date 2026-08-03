using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using ModuleF.Models;
using ModuleF.Services;
using ModuleF.ViewModels;
using ModuleF.Views;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ModuleF;

/// <summary>
/// Owns every dialog/picker interaction (View responsibility) and wires DataGrid events to plain
/// ViewModel method calls - the ViewModel itself never references WinUI types. See PLAN.md for why
/// cell edits use a OneWay indexer binding + <see cref="Grid_CellEditEnding"/> instead of TwoWay:
/// TwoWay would write the new value straight into the model, bypassing the Undo/Redo command that
/// SetCell needs to create.
/// </summary>
public sealed partial class MainWindow : Window
{
    private CancellationTokenSource? _currentOperationCts;
    private string? _currentFilePath;

    public MainWindow()
    {
        var editService = new CsvEditService();
        var fileService = new CsvFileService(new EncodingDetector(), new DelimiterDetector(), new ValidationService());
        ViewModel = new ModuleFViewModel(fileService, editService);

        InitializeComponent();

        ViewModel.ColumnsChanged += (_, _) => RebuildColumns();
        ViewModel.PropertyChanged += (_, _) => Bindings.Update();
        ViewModel.Issues.CollectionChanged += (_, _) => Bindings.Update();

        RebuildColumns();
    }

    public ModuleFViewModel ViewModel { get; }

    public Visibility IsBusyVisibility => ViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasIssuesVisibility => ViewModel.Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string IssuesHeader => $"Cảnh báo ({ViewModel.Issues.Count})";

    public string SaveStateText => ViewModel.IsDirty ? "● Chưa lưu" : "Đã lưu";

    // ----- DataGrid column/row wiring -----

    private void RebuildColumns()
    {
        Grid.Columns.Clear();
        var columns = ViewModel.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i].Name,
                Binding = new Binding
                {
                    Path = new PropertyPath($"[{i}]"),
                    Mode = BindingMode.OneWay,
                },
            });
        }
    }

    private void Grid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        if (e.Column.GetCellContent(e.Row) is not TextBox textBox)
        {
            return;
        }

        var viewRowIndex = e.Row.GetIndex();
        var columnIndex = Grid.Columns.IndexOf(e.Column);
        ViewModel.SetCell(viewRowIndex, columnIndex, textBox.Text);
    }

    // ----- Toolbar: Open/Save -----

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".tsv");
        picker.FileTypeFilter.Add(".txt");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        _currentFilePath = file.Path;
        _currentOperationCts = new CancellationTokenSource();
        await ViewModel.OpenAsync(file.Path, ConfirmLargeFileAsync, _currentOperationCts.Token);
    }

    /// <summary>Status bar "Encoding"/"Delimiter" labels are tappable - re-opens the same file with a
    /// manually chosen encoding/delimiter when auto-detection guessed wrong.</summary>
    private async void EncodingLabel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_currentFilePath is null)
        {
            return;
        }

        var dialog = new EncodingPickerDialog { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _currentOperationCts = new CancellationTokenSource();
        await ViewModel.OpenAsync(_currentFilePath, dialog.SelectedEncoding, dialog.SelectedDelimiter, ConfirmLargeFileAsync, _currentOperationCts.Token);
    }

    private async Task<bool> ConfirmLargeFileAsync(long estimatedRows)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "File rất lớn",
            Content = $"Ước tính khoảng {estimatedRows:N0} dòng - có thể vượt RAM khả dụng khi nạp toàn bộ. Vẫn mở?",
            PrimaryButtonText = "Vẫn mở",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _currentOperationCts = new CancellationTokenSource();
        try
        {
            await ViewModel.SaveAsync(_currentOperationCts.Token);
        }
        catch (InvalidOperationException)
        {
            SaveAsButton_Click(sender, e);
        }
    }

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        picker.FileTypeChoices.Add("TSV", new List<string> { ".tsv" });
        picker.SuggestedFileName = "data";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        _currentOperationCts = new CancellationTokenSource();
        await ViewModel.SaveAsAsync(file.Path, _currentOperationCts.Token);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _currentOperationCts?.Cancel();

    // ----- Toolbar: rows/columns -----

    private void AddRowButton_Click(object sender, RoutedEventArgs e)
    {
        int? anchor = Grid.SelectedIndex >= 0 ? Grid.SelectedIndex : null;
        ViewModel.AddRow(anchor);
    }

    private void DeleteRowButton_Click(object sender, RoutedEventArgs e) => DeleteSelectedRows();

    private void DeleteSelectedRows()
    {
        var indexes = Grid.SelectedItems.Cast<CsvRow>().Select(row => ViewModel.ViewRows.IndexOf(row)).Where(i => i >= 0).ToList();
        if (indexes.Count > 0)
        {
            ViewModel.RemoveRows(indexes);
        }
    }

    private async void AddColumnButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColumnDialog("Thêm cột") { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ColumnName))
        {
            ViewModel.AddColumn(ViewModel.Columns.Count, dialog.ColumnName);
        }
    }

    private async void DeleteColumnButton_Click(object sender, RoutedEventArgs e)
    {
        var columnIndex = Grid.CurrentColumn is { } currentColumn ? Grid.Columns.IndexOf(currentColumn) : -1;
        if (columnIndex < 0)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Xóa cột",
            Content = $"Xóa cột '{ViewModel.Columns[columnIndex].Name}'?",
            PrimaryButtonText = "Xóa",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.RemoveColumn(columnIndex);
        }
    }

    private async void RenameColumnButton_Click(object sender, RoutedEventArgs e)
    {
        var columnIndex = Grid.CurrentColumn is { } currentColumn ? Grid.Columns.IndexOf(currentColumn) : -1;
        if (columnIndex < 0)
        {
            return;
        }

        var dialog = new ColumnDialog("Đổi tên cột", ViewModel.Columns[columnIndex].Name) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ColumnName))
        {
            ViewModel.RenameColumn(columnIndex, dialog.ColumnName);
        }
    }

    // ----- Toolbar: find/filter/sort/statistics -----

    private async void FindButton_Click(object sender, RoutedEventArgs e) => await ShowFindReplaceDialogAsync();

    private async void ReplaceButton_Click(object sender, RoutedEventArgs e) => await ShowFindReplaceDialogAsync();

    private async Task ShowFindReplaceDialogAsync()
    {
        var dialog = new FindReplaceDialog(ViewModel) { XamlRoot = Content.XamlRoot };
        dialog.MatchNavigated += (_, match) => NavigateToCell(match);
        await dialog.ShowAsync();
    }

    private void NavigateToCell(CellRef cell)
    {
        if (cell.RowIndex < 0 || cell.RowIndex >= ViewModel.ViewRows.Count)
        {
            return;
        }

        // CommunityToolkit.WinUI.UI.Controls.DataGrid 7.1.2 has no public ScrollIntoView(item, column)
        // overload - selecting the row/column is enough to highlight the match; the grid does not
        // reliably auto-scroll to it for very large result sets (documented limitation in PLAN.md).
        var row = ViewModel.ViewRows[cell.RowIndex];
        Grid.SelectedItem = row;
        if (cell.ColumnIndex < Grid.Columns.Count)
        {
            Grid.CurrentColumn = Grid.Columns[cell.ColumnIndex];
        }
    }

    private async void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FilterDialog(ViewModel.Columns, null) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.ApplyFilter(dialog.BuildExpression());
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ViewModel.ApplyFilter(null);
        }
    }

    private async void SortButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SortDialog(ViewModel.Columns, new List<(int, bool)>()) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.ApplySort(dialog.BuildSortSpec());
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ViewModel.ApplySort(new List<(int, bool)>());
        }
    }

    private async void StatisticsButton_Click(object sender, RoutedEventArgs e)
    {
        _currentOperationCts = new CancellationTokenSource();
        var progress = new Progress<int>(p => ViewModel.ProgressPercent = p);
        ViewModel.IsBusy = true;
        try
        {
            var statistics = await ViewModel.ComputeStatisticsAsync(progress, _currentOperationCts.Token);
            var dialog = new StatisticsDialog(statistics) { XamlRoot = Content.XamlRoot };
            await dialog.ShowAsync();
        }
        catch (OperationCanceledException)
        {
            ViewModel.StatusMessage = "Đã hủy tính thống kê.";
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    // ----- Context menu -----

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedRows = Grid.SelectedItems.Cast<CsvRow>().ToList();
        if (selectedRows.Count == 0)
        {
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var row in selectedRows)
        {
            sb.AppendLine(string.Join('\t', Enumerable.Range(0, ViewModel.Columns.Count).Select(row.GetCell)));
        }

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private async void PasteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var clipboardContent = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (!clipboardContent.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            return;
        }

        var text = await clipboardContent.GetTextAsync();
        var block = text.TrimEnd('\r', '\n').Split('\n')
            .Select(line => (IReadOnlyList<string>)line.TrimEnd('\r').Split('\t'))
            .ToList();

        var anchorRowIndex = Grid.SelectedIndex >= 0 ? Grid.SelectedIndex : 0;
        var currentColumnIndex = Grid.CurrentColumn is { } pasteColumn ? Grid.Columns.IndexOf(pasteColumn) : -1;
        var anchorColumnIndex = Math.Max(currentColumnIndex, 0);
        ViewModel.PasteBlock(anchorRowIndex, anchorColumnIndex, block);
    }

    private void DeleteRowsMenuItem_Click(object sender, RoutedEventArgs e) => DeleteSelectedRows();

    private void InsertRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        int? anchor = Grid.SelectedIndex >= 0 ? Grid.SelectedIndex : null;
        ViewModel.AddRow(anchor);
    }

    private void DuplicateRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedIndex >= 0)
        {
            ViewModel.DuplicateRow(Grid.SelectedIndex);
        }
    }

    private void IssuesListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IssuesListView.SelectedItem is ValidationIssue { RowIndex: { } rowIndex })
        {
            NavigateToCell(new CellRef(rowIndex, 0));
        }
    }
}
