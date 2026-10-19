using MeRon.Core.Orders;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace MeRon.Views;

public sealed partial class OrderFormPage : Page
{
    public OrderFormViewModel ViewModel { get; } = new();

    public OrderFormPage()
    {
        InitializeComponent();
        // Chọn được khách (bấm gợi ý / Enter / Tab) → con trỏ sang nút Thêm món (Enter / Space là thêm dòng, gõ món luôn).
        CustomerSuggest.Attach(CustomerBox, () => ViewModel.Customers, () => ViewModel.Customer, c => ViewModel.Customer = c, FocusAddLine);
        Shortcuts.Ctrl(this, VirtualKey.M, AddLine);
        Shortcuts.Add(this, VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => _ = SaveAsync(AfterSave.New));
        Shortcuts.Add(this, VirtualKey.K, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => _ = SaveAsync(AfterSave.CopyForOtherCustomer));
        Shortcuts.Add(this, VirtualKey.C, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () =>
        {
            if (ViewModel.IsEdit)
            {
                CopyOrder_Click(this, new RoutedEventArgs());
            }
        });
        Shortcuts.Ctrl(this, VirtualKey.L, () => _ = CopyLastOrderAsync());
        Shortcuts.EscapeBack(this, GoBack);
        // Thêm đơn (nút Thêm, Ctrl+N, Lưu và thêm mới, Sao chép đơn): con trỏ vào ngay ô Khách hàng - ô trống thì hiện sẵn
        // danh sách khách (GotFocus của CustomerSuggest) để chọn luôn. Đợi 1 nhịp sau Loaded thì ô mới nhận focus được.
        Loaded += (_, _) =>
        {
            if (!ViewModel.IsEdit)
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => CustomerBox.Focus(FocusState.Programmatic));
            }
        };
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
            // Nhập xong số lượng: Enter / Tab → con trỏ sang nút Thêm món để thêm món tiếp (Shift+Tab lùi như thường).
            // Enter: NumberBox tự xử lý (ghi số) rồi mới tới đây → nghe cả sự kiện đã Handled.
            box.AddHandler(KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
            {
                if (args.Key == VirtualKey.Enter)
                {
                    args.Handled = true;
                    FocusAddLine();
                }
            }), handledEventsToo: true);
            box.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == VirtualKey.Tab && !SuggestionList.IsShiftDown())
                {
                    args.Handled = true;
                    FocusAddLine();
                }
            };
        }
    }

    private async void CopyLast_Click(object sender, RoutedEventArgs e) => await CopyLastOrderAsync();

    /// <summary>"Lần trước" → chép các món vào đơn (form đã có món thì hỏi thay). Xong con trỏ về nút Thêm món: thêm món
    /// khác hoặc Ctrl+S lưu luôn.</summary>
    private async Task CopyLastOrderAsync()
    {
        if (!ViewModel.HasLastOrder)
        {
            return;
        }
        if (ViewModel.FilledLineCount is var filled and > 0
            && !await Dialogs.ConfirmAsync(XamlRoot, "Thay các món đang có?",
                $"Đơn đang có {filled} món. Chép đơn lần trước sẽ thay toàn bộ các dòng món này.", "Thay"))
        {
            return;
        }
        if (ViewModel.ApplyLastOrder())
        {
            FocusAddLine();
        }
    }

    private void FocusAddLine() =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            AddLineButton.StartBringIntoView();
            AddLineButton.Focus(FocusState.Keyboard);
        });

    /// <summary>Chọn được món (bấm gợi ý / Enter / Tab) → con trỏ sang ô Số lượng cùng dòng.</summary>
    private void ProductBox_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (AutoSuggestBox)sender;
        ProductSuggest.Attach(box, () => FocusSameLine(box, "QuantityBox"));
    }

    /// <summary>Gõ xong tên món tự do, Enter → con trỏ sang ô Đơn giá cùng dòng.</summary>
    private void FreeNameBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is FrameworkElement box)
        {
            e.Handled = true;
            FocusSameLine(box, "FreePriceBox");
        }
    }

    private void FocusSameLine(FrameworkElement element, string name)
    {
        if (element.DataContext is OrderLineViewModel line)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusLine(ViewModel.Lines.IndexOf(line), name));
        }
    }

    /// <summary>Click chạy trước Command (dòng chưa thêm) → đợi dòng mới hiện rồi đưa con trỏ vào ô món của nó để gõ ngay.</summary>
    private void AddLine_Click(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusLine(ViewModel.Lines.Count - 1, "ProductBox"));

    /// <summary>Như Thêm món: đợi dòng tự do mới hiện rồi đưa con trỏ vào ô Tên món tự do.</summary>
    private void AddFreeLine_Click(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusLine(ViewModel.Lines.Count - 1, "FreeNameBox"));

    /// <summary>Ctrl+M: như nút Thêm món.</summary>
    private void AddLine()
    {
        ViewModel.AddLineCommand.Execute(null);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusLine(ViewModel.Lines.Count - 1, "ProductBox"));
    }

    private void FocusLine(int index, string name)
    {
        LinesList.UpdateLayout();
        if (index >= 0 && LinesList.ContainerFromIndex(index) is DependencyObject container && Ui.FindByName(container, name) is Control control)
        {
            control.StartBringIntoView();
            control.Focus(FocusState.Programmatic);
            // Ô số (Số lượng, Đơn giá): bôi đen số cũ để gõ đè luôn.
            if (control is NumberBox && Ui.FindByName(control, "InputBox") is TextBox input)
            {
                input.SelectAll();
            }
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
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(AfterSave.Back);

    private async void SaveNew_Click(object sender, RoutedEventArgs e) => await SaveAsync(AfterSave.New);

    private async void SaveCopy_Click(object sender, RoutedEventArgs e) => await SaveAsync(AfterSave.CopyForOtherCustomer);

    /// <summary>Lưu xong thì: về danh sách / mở form đơn mới trống (Ctrl+Shift+N, cùng ngày giao) để nhập liên tiếp /
    /// mở form đơn mới chép đơn vừa lưu nhưng bỏ trống khách (Ctrl+Shift+K) - nhiều khách đặt giống nhau.</summary>
    private enum AfterSave { Back, New, CopyForOtherCustomer }

    /// <param name="after">New / CopyForOtherCustomer: form mới thay chỗ form này trong lịch sử (Quay lại về thẳng danh sách);
    /// con trỏ vào sẵn ô Khách hàng.</param>
    private async Task SaveAsync(AfterSave after)
    {
        // Ctrl+S khi con trỏ còn trong 1 ô (khách hàng, số tiền...) → chuyển focus để ô đó ghi nhận giá trị trước khi đọc.
        SaveButton.Focus(FocusState.Programmatic);

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
        if (!ViewModel.Save())
        {
            return;
        }
        switch (after)
        {
            case AfterSave.New:
                var day = ViewModel.DeliveryDate is { } d ? DateOnly.FromDateTime(d.Date) : (DateOnly?)null;
                ReplaceWithForm(new OrderFormArgs(null, InitialDelivery: day));
                break;
            case AfterSave.CopyForOtherCustomer:
                ReplaceWithForm(new OrderFormArgs(null, CopyFromId: ViewModel.SavedId, ForOtherCustomer: true));
                break;
            default:
                GoBack();
                break;
        }
    }

    /// <summary>
    /// Mở form đơn mới chép từ đơn đang mở (chỗ sửa chưa lưu không được chép, chép theo dữ liệu đã lưu). Form bản sao thay chỗ
    /// form này trong lịch sử: Lưu / Quay lại về thẳng danh sách.
    /// </summary>
    private void CopyOrder_Click(object sender, RoutedEventArgs e)
    {
        ReplaceWithForm(new OrderFormArgs(null, CopyFromId: ViewModel.Id));
    }

    /// <summary>Mở form đơn khác thay chỗ form này trong lịch sử: Lưu / Quay lại ở form mới về thẳng danh sách.</summary>
    private void ReplaceWithForm(OrderFormArgs args)
    {
        Frame.Navigate(typeof(OrderFormPage), args);
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
