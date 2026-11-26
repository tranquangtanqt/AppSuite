using System.ComponentModel;
using DbTools.Core;
using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace DbTools.Views;

/// <summary>1 dòng của bảng kết quả (số thứ tự + giá trị các cột).</summary>
public sealed record QueryRowItem(int Number, string?[] Cells);

public sealed partial class QueryPage : Page
{
    private const double NumberWidth = 56;
    private List<double> _widths = [];
    private TextBlock? _menuCell;

    public QueryViewModel ViewModel { get; } = new();

    public QueryPage()
    {
        InitializeComponent();
        ViewModel.Confirm = c => Dialogs.ConfirmAsync(XamlRoot, c);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ResultList.AddHandler(PointerPressedEvent, new PointerEventHandler(ResultList_PointerPressed), true);
        if (App.MainWindow is { } window)
        {
            window.Closed += (_, _) => ViewModel.SaveDraft(); // đóng app khi đang ở trang này vẫn nhớ ô soạn
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.SaveDraft();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QueryViewModel.Table))
        {
            BuildTable(ViewModel.Table);
        }
        else if (e.PropertyName == nameof(QueryViewModel.Tab) && Tabs.Items.Count > ViewModel.Tab && Tabs.SelectedItem != Tabs.Items[ViewModel.Tab])
        {
            Tabs.SelectedItem = Tabs.Items[ViewModel.Tab];
        }
    }

    public Brush StatusBrush(bool error) => error ? Ui.ResultBrush(false) : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

    public Visibility EmptyVisible(bool hasTable, int tab) => Ui.VisibleIf(!hasTable && tab == 0);

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.Tab = Math.Max(0, sender.Items.IndexOf(sender.SelectedItem));

    // ----- Chạy -----

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private Task RunAsync() => ViewModel.RunAsync(Editor.SelectedText, Editor.SelectionStart);

    /// <summary>Ctrl+Enter trong ô soạn = Chạy (Enter thường vẫn xuống dòng).</summary>
    private async void Editor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            await RunAsync();
        }
    }

    // ----- Bảng kết quả -----

    private void BuildTable(QueryTable? table)
    {
        HeaderPanel.Children.Clear();
        ResultList.ItemsSource = null;
        if (table is null)
        {
            ResultGrid.Width = double.NaN;
            return;
        }
        _widths = QuickQuery.ColumnWidths(table.Columns, table.Rows);
        HeaderPanel.Children.Add(new TextBlock { Text = "#", Width = NumberWidth, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        for (var i = 0; i < table.Columns.Count; i++)
        {
            HeaderPanel.Children.Add(new StackPanel
            {
                Width = _widths[i],
                Padding = new Thickness(0, 0, 8, 0),
                Children =
                {
                    new TextBlock { Text = table.Columns[i], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = table.Types[i], FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            });
        }
        AutomationProperties.SetName(HeaderPanel, string.Join(" | ", table.Columns));
        ResultGrid.Width = NumberWidth + _widths.Sum() + 24 + 16; // + padding 2 bên + chỗ thanh cuộn dọc
        ResultList.ItemsSource = table.Rows.Select((r, i) => new QueryRowItem(i + 1, r)).ToList();
        ResultScroll.ChangeView(0, null, null, true);
    }

    /// <summary>Dựng ô cho dòng sắp hiện (ListView chỉ dựng dòng đang hiện, dùng lại ô khi cuộn).</summary>
    private void ResultList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not QueryRowItem row || args.ItemContainer.ContentTemplateRoot is not StackPanel panel)
        {
            return;
        }
        var count = row.Cells.Length + 1;
        while (panel.Children.Count > count)
        {
            panel.Children.RemoveAt(panel.Children.Count - 1);
        }
        while (panel.Children.Count < count)
        {
            panel.Children.Add(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Padding = new Thickness(0, 0, 8, 0) });
        }
        var number = (TextBlock)panel.Children[0];
        number.Text = row.Number.ToString("N0");
        number.Width = NumberWidth;
        number.Opacity = 0.6;
        for (var i = 0; i < row.Cells.Length; i++)
        {
            var cell = (TextBlock)panel.Children[i + 1];
            var value = row.Cells[i];
            cell.Text = QuickQuery.CellText(value);
            cell.Tag = i;
            cell.Width = i < _widths.Count ? _widths[i] : 120;
            cell.Opacity = value is null ? 0.45 : 1;
            cell.FontStyle = value is null ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
            ToolTipService.SetToolTip(cell, value is { Length: > 40 } || value?.IndexOfAny(['\r', '\n']) >= 0 ? value : null);
        }
        AutomationProperties.SetName(args.ItemContainer, string.Join(" | ", row.Cells.Select(QuickQuery.CellText)));
    }

    // ----- Chép -----

    private void Copy(IEnumerable<QueryRowItem> rows, bool header)
    {
        if (ViewModel.Table is not { } table)
        {
            return;
        }
        var list = rows.ToList();
        if (list.Count == 0)
        {
            return;
        }
        SetClipboard(QuickQuery.ToTsv(header ? table.Columns : null, list.Select(r => (IReadOnlyList<string?>)r.Cells)));
        ViewModel.Copied(list.Count);
    }

    private static void SetClipboard(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
    }

    private IEnumerable<QueryRowItem> SelectedRows() => ResultList.SelectedItems.OfType<QueryRowItem>().OrderBy(r => r.Number);

    private void CopySelected_Click(object sender, RoutedEventArgs e) => Copy(SelectedRows(), true);

    private void CopySelectedNoHeader_Click(object sender, RoutedEventArgs e) => Copy(SelectedRows(), false);

    private void CopyAll_Click(object sender, RoutedEventArgs e) => Copy(ResultList.ItemsSource as IEnumerable<QueryRowItem> ?? [], true);

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Copy(SelectedRows(), true);
    }

    /// <summary>Chuột phải: nhớ ô dưới chuột cho mục "Chép ô này" (bắt cả sự kiện ListViewItem đã xử lý).</summary>
    private void ResultList_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(ResultList).Properties.IsRightButtonPressed)
        {
            _menuCell = e.OriginalSource as TextBlock is { Tag: int } cell ? cell : null;
        }
    }

    private void RowMenu_Opening(object? sender, object e) => CopyCellItem.IsEnabled = _menuCell is not null;

    private void CopyCell_Click(object sender, RoutedEventArgs e)
    {
        if (_menuCell is { Tag: int column } && FindRow(_menuCell) is { } row && column < row.Cells.Length)
        {
            SetClipboard(row.Cells[column] ?? string.Empty);
            ViewModel.Copied(1);
        }
    }

    private static QueryRowItem? FindRow(DependencyObject element)
    {
        for (var e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is ListViewItem { Content: QueryRowItem row })
            {
                return row;
            }
        }
        return null;
    }

    // ----- Lịch sử -----

    private void History_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is QueryHistoryRow row)
        {
            ViewModel.LoadFromHistory(row);
            HistoryFlyout.Hide();
            Editor.Focus(FocusState.Programmatic);
        }
    }
}
