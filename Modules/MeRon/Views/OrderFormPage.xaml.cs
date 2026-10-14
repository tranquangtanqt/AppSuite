using MeRon.Core.Orders;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class OrderFormPage : Page
{
    public OrderFormViewModel ViewModel { get; } = new();

    public OrderFormPage()
    {
        InitializeComponent();
        CustomerSuggest.Attach(CustomerBox, () => ViewModel.Customers, () => ViewModel.Customer, c => ViewModel.Customer = c);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.Load(e.Parameter as OrderFormArgs ?? new OrderFormArgs(null));
        CustomerBox.Text = ViewModel.Customer?.Name ?? string.Empty;
        Bindings.Update();
    }

    // Ô số: Tổng tính lại ngay lúc gõ (LiveNumberBox); tiền canh phải, số lượng canh giữa.
    private void DiscountBox_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (NumberBox)sender;
        box.NumberFormatter = Format.MoneyFormatter();
        LiveNumberBox.Attach(box, ViewModel.PreviewDiscount, TextAlignment.Right);
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

    private void ProductBox_Loaded(object sender, RoutedEventArgs e) => ProductSuggest.Attach((AutoSuggestBox)sender);

    /// <summary>Click chạy trước Command (dòng chưa thêm) → đợi dòng mới hiện rồi đưa con trỏ vào ô món của nó để gõ ngay.</summary>
    private void AddLine_Click(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusLine(ViewModel.Lines.Count - 1, "ProductBox"));

    private void FocusLine(int index, string name)
    {
        LinesList.UpdateLayout();
        if (index >= 0 && LinesList.ContainerFromIndex(index) is DependencyObject container && Ui.FindByName(container, name) is Control control)
        {
            control.StartBringIntoView();
            control.Focus(FocusState.Programmatic);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(OrdersPage));
        }
    }

    /// <summary>Kiểm tra → (trùng đơn cùng ngày thì hỏi "Cảnh báo trùng đơn": Hủy / Vẫn lưu) → lưu → quay lại danh sách.</summary>
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        // Ctrl+S khi con trỏ còn trong 1 ô (khách hàng, số tiền...) → chuyển focus để ô đó ghi nhận giá trị trước khi đọc.
        (sender as Control)?.Focus(FocusState.Programmatic);

        if (ViewModel.Validate() is { } invalid)
        {
            FocusInvalid(invalid);
            return;
        }
        if (ViewModel.HasDuplicate()
            && !await Dialogs.ConfirmAsync(XamlRoot, "Cảnh báo trùng đơn",
                "Khách hàng này đã có đơn đặt hàng trong ngày giao này. Bạn có chắc chắn muốn tiếp tục lưu đơn này?", "Vẫn lưu"))
        {
            return;
        }
        if (ViewModel.Save())
        {
            GoBack();
        }
    }

    /// <summary>
    /// Mở form đơn mới chép từ đơn đang mở (chỗ sửa chưa lưu không được chép, chép theo dữ liệu đã lưu). Form bản sao thay chỗ
    /// form này trong lịch sử: Lưu / Quay lại về thẳng danh sách.
    /// </summary>
    private void CopyOrder_Click(object sender, RoutedEventArgs e)
    {
        var id = ViewModel.Id;
        Frame.Navigate(typeof(OrderFormPage), new OrderFormArgs(null, CopyFromId: id));
        if (Frame.BackStack.Count > 0 && Frame.BackStack[^1].SourcePageType == typeof(OrderFormPage))
        {
            Frame.BackStack.RemoveAt(Frame.BackStack.Count - 1);
        }
    }

    private async void CancelOrder_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Huỷ đơn?", "Bạn có chắc chắn muốn huỷ đơn? Đơn vẫn được giữ lại để tra cứu, không tính vào báo cáo.", "Huỷ đơn")
            && ViewModel.CancelOrder())
        {
            GoBack();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Xoá đơn?", "Xoá hẳn đơn và các dòng món của đơn khỏi dữ liệu. Không hoàn tác được.", "Xoá")
            && ViewModel.DeleteOrder())
        {
            GoBack();
        }
    }

    /// <summary>Lỗi ở 1 dòng món → đưa con trỏ về đúng ô (Tên / Đơn giá món tự do, hoặc ô Món) như điện thoại.</summary>
    private void FocusInvalid(OrderValidationException error)
    {
        if (error.LineIndex is not { } index)
        {
            if (ViewModel.Customer is null)
            {
                CustomerBox.Focus(FocusState.Programmatic);
            }
            return;
        }
        FocusLine(index, error.Field switch
        {
            OrderLineField.FreeName => "FreeNameBox",
            OrderLineField.FreePrice => "FreePriceBox",
            _ => "ProductBox",
        });
    }
}
