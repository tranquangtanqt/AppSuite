using MeRon.Core.Models;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>
/// Thống kê theo đơn hàng. Vào từ menu = bộ lọc mặc định; quay lại từ Chi tiết = giữ bộ lọc (ViewModel dùng chung cả phiên,
/// trang không cache - cùng lý do với OrdersPage: SelectorBar trong trang cache áp lại tab cũ).
/// </summary>
public sealed partial class ReportOrderPage : Page
{
    private static readonly ReportOrderViewModel Session = new();

    public ReportOrderViewModel ViewModel => Session;

    public ReportOrderPage()
    {
        InitializeComponent();
        CustomerSuggest.Attach(CustomerBox, () => ViewModel.Customers, () => ViewModel.Customer, c => ViewModel.Customer = c);
        foreach (var sort in Enum.GetValues<CustomerSort>())
        {
            var item = new RadioMenuFlyoutItem { Text = ReportOrderViewModel.SortLabel(sort), Tag = sort, GroupName = "sort" };
            item.Click += (_, _) =>
            {
                ViewModel.Sort = sort;
                ShowSort();
            };
            SortMenu.Items.Add(item);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.NavigationMode == NavigationMode.New)
        {
            ViewModel.Reset();
        }
        ViewModel.Load();
        CustomerBox.Text = ViewModel.Customer?.Name ?? string.Empty;
        SyncStatusBar();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SyncStatusBar);
        ShowSort();
    }

    private bool _syncing;

    private void SyncStatusBar()
    {
        _syncing = true;
        var tag = ViewModel.Status is { } s ? ((int)s).ToString() : "0";
        StatusBar.SelectedItem = StatusBar.Items.First(i => (string)i.Tag == tag);
        _syncing = false;
    }

    private void ShowSort()
    {
        foreach (var item in SortMenu.Items.OfType<RadioMenuFlyoutItem>())
        {
            item.IsChecked = (CustomerSort)item.Tag == ViewModel.Sort;
        }
        SortText.Text = ReportOrderViewModel.SortLabel(ViewModel.Sort);
    }

    private void StatusBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // Bỏ qua thay đổi do chính trang đặt, hoặc SelectorBar tự áp lại lúc trang chưa gắn vào cửa sổ.
        if (_syncing || !IsLoaded)
        {
            return;
        }
        ViewModel.Status = sender.SelectedItem?.Tag is string tag && tag != "0" ? (OrderStatus)int.Parse(tag) : null;
    }

    private void ClearCustomer_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Customer = null;
        CustomerBox.Text = string.Empty;
    }

    private void Customer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CustomerReportRow row })
        {
            Frame.Navigate(typeof(OrderDetailPage), ViewModel.DetailArgs(row));
        }
    }
}
