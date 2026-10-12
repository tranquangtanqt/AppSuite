using MeRon.Core.Orders;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

    private void MoneyBox_Loaded(object sender, RoutedEventArgs e) => ((NumberBox)sender).NumberFormatter = Format.MoneyFormatter();

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
        var name = error.Field switch
        {
            OrderLineField.FreeName => "FreeNameBox",
            OrderLineField.FreePrice => "FreePriceBox",
            _ => "ProductBox",
        };
        if (LinesList.ContainerFromIndex(index) is DependencyObject container && Find(container, name) is Control control)
        {
            control.StartBringIntoView();
            control.Focus(FocusState.Programmatic);
        }
    }

    private static FrameworkElement? Find(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } element && n == name)
            {
                return element;
            }
            if (Find(child, name) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
