using MeRon.Core.Models;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>
/// Thống kê theo đơn hàng. Bộ lọc mặc định chỉ ở lần đầu mở trong phiên, sau đó luôn giữ (ViewModel dùng chung cả phiên,
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

    private static bool _filterInitialized;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        // Bộ lọc mặc định chỉ ở lần đầu mở trang trong phiên; vào lại từ menu thì giữ điều kiện đã chọn.
        if (!_filterInitialized)
        {
            ViewModel.Reset();
            _filterInitialized = true;
        }
        ViewModel.Load();
        CustomerBox.Text = ViewModel.Customer?.Name ?? string.Empty;
        SyncStatusBar();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SyncStatusBar);
        ShowSort();
    }

    /// <summary>ViewModel sống cả phiên, trang thì không - gỡ mọi chỗ nghe ViewModel (cùng lý do với OrdersPage).</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Bindings.StopTracking();
        TilesList.ItemsSource = null;
        RowsList.ItemsSource = null;
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
