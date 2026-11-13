using MeRon.Core.Orders;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace MeRon.Views;

/// <summary>
/// Popup Sửa đơn của Nhập đơn hàng loạt: giống form đơn nhưng chỉ có những gì dòng chữ ghi được (khách, món, đã thanh toán).
/// Giảm giá, ghi chú không có; ngày giao dùng chung ở đầu trang.
/// </summary>
public sealed partial class BatchOrderEditor : UserControl
{
    public OrderFormViewModel ViewModel { get; } = new();

    private readonly BatchLineMatch _match;

    private BatchOrderEditor(BatchLineMatch match)
    {
        _match = match;
        ViewModel.LoadBatchLine(match);
        InitializeComponent();
        CustomerSuggest.Attach(CustomerBox, () => ViewModel.Customers, () => ViewModel.Customer, c => ViewModel.Customer = c);
        CustomerBox.PlaceholderText = match.Line.Customer.Length > 0
            ? $"Chưa khớp \"{match.Line.Customer}\": gõ để chọn, bỏ trống thì giữ chữ này"
            : "Gõ tên hoặc số điện thoại rồi chọn";
    }

    /// <summary>Trả về dòng chữ mới + ghim (khách / món như đã chọn); null = bấm Huỷ. ContentDialog chỉ mở được 1 cái mỗi lúc nên ở đây không thêm khách mới
    /// được: chữ khách chưa có giữ nguyên trên dòng, thêm bằng nút Thêm khách trên thẻ.</summary>
    public static async Task<(string Line, BatchPin Pin)?> ShowAsync(XamlRoot root, BatchLineMatch match)
    {
        var editor = new BatchOrderEditor(match) { Width = Math.Min(760, root.Size.Width - 120) };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = $"Sửa đơn - dòng {match.Line.Number}",
            Content = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, // ContentDialog không tự cuộn
            PrimaryButtonText = "Áp dụng",
            CloseButtonText = "Huỷ",
            PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        dialog.Resources["ContentDialogMaxWidth"] = 820.0;

        // Ô gợi ý nuốt phím Esc nên hộp thoại không tự đóng được: danh sách đang mở thì để ô tự đóng, không thì đóng popup.
        dialog.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Escape && !SuggestionOpen(root))
            {
                args.Handled = true;
                dialog.Hide();
            }
        };

        (string Line, BatchPin Pin)? line = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            line = editor.Apply();
            args.Cancel = line is null;
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? line : null;
    }

    private static bool SuggestionOpen(XamlRoot root)
    {
        for (var element = FocusManager.GetFocusedElement(root) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is AutoSuggestBox box)
            {
                return box.IsSuggestionListOpen;
            }
        }
        return false;
    }

    /// <summary>Kiểm tra như form đơn; lỗi thì hiện trong popup, đưa con trỏ về đúng ô và không đóng.</summary>
    private (string Line, BatchPin Pin)? Apply()
    {
        var customerRequired = _match.Line.Customer.Length == 0;
        if (ViewModel.Validate(customerRequired) is not { } invalid)
        {
            return ViewModel.BatchLineText(_match);
        }
        if (invalid.LineIndex is { } index)
        {
            FocusLine(index, invalid.Field switch
            {
                OrderLineField.FreeName => "FreeNameBox",
                OrderLineField.FreePrice => "FreePriceBox",
                _ => "ProductBox",
            });
        }
        else if (ViewModel.Customer is null && customerRequired)
        {
            CustomerBox.Focus(FocusState.Programmatic);
        }
        return null;
    }

    private void FreePriceBox_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (NumberBox)sender;
        box.NumberFormatter = Format.MoneyFormatter();
        if (box.DataContext is OrderLineViewModel line)
        {
            LiveNumberBox.Attach(box, line.PreviewFreePrice, TextAlignment.Right);
        }
    }

    private void QuantityBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is NumberBox { DataContext: OrderLineViewModel line } box)
        {
            LiveNumberBox.Attach(box, line.PreviewQuantity, TextAlignment.Center);
        }
    }

    private void ProductBox_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (AutoSuggestBox)sender;
        ProductSuggest.Attach(box, () => FocusSameLine(box, "QuantityBox"), FocusAddLine);
    }

    private void AddLine_Click(object sender, RoutedEventArgs e) => FocusLast("ProductBox");

    private void AddFreeLine_Click(object sender, RoutedEventArgs e) => FocusLast("FreeNameBox");

    /// <summary>Click chạy trước Command: đợi dòng mới hiện rồi đưa con trỏ vào.</summary>
    private void FocusLast(string name) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusLine(ViewModel.Lines.Count - 1, name));

    private void FocusAddLine() =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => AddLineButton.Focus(FocusState.Keyboard));

    private void FocusSameLine(FrameworkElement element, string name)
    {
        if (element.DataContext is OrderLineViewModel line)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusLine(ViewModel.Lines.IndexOf(line), name));
        }
    }

    private void FocusLine(int index, string name)
    {
        LinesList.UpdateLayout();
        if (index >= 0 && LinesList.ContainerFromIndex(index) is DependencyObject container && Ui.FindByName(container, name) is Control control)
        {
            control.StartBringIntoView();
            control.Focus(FocusState.Programmatic);
            if (control is NumberBox && Ui.FindByName(control, "InputBox") is TextBox input)
            {
                input.SelectAll();
            }
        }
    }
}
