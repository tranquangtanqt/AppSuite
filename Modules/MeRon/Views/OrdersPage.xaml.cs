using MeRon.Core;
using MeRon.Core.Models;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>
/// Danh sách đơn. Bộ lọc giữ trong 1 ViewModel dùng chung cả phiên: quay lại từ form / Chi tiết (Back) giữ nguyên bộ lọc;
/// vào lại từ menu cũng giữ (mặc định chỉ ở lần đầu mở trong phiên). Trang KHÔNG cache: SelectorBar trong trang cache tự áp lại
/// tab cũ khi gắn lại vào cửa sổ, lúc có lúc không (quay lại từ Chi tiết thì bộ lọc tự về "Tất cả").
/// </summary>
public sealed partial class OrdersPage : Page
{
    private static readonly OrdersViewModel Session = new();

    public OrdersViewModel ViewModel => Session;

    public OrdersPage()
    {
        InitializeComponent();
        CustomerSuggest.Attach(CustomerBox, () => ViewModel.Customers, () => ViewModel.Customer, c => ViewModel.Customer = c);
        Shortcuts.Search(this, CustomerBox);
        Shortcuts.StatusTabs(this, StatusBar);
        Shortcuts.Ctrl(this, Windows.System.VirtualKey.D, () => RangeMenu.ShowAt(RangeButton));
        foreach (var sort in Enum.GetValues<OrderSort>())
        {
            var item = new RadioMenuFlyoutItem { Text = OrdersViewModel.SortLabel(sort), Tag = sort, GroupName = "sort" };
            item.Click += (_, _) => ViewModel.Sort = sort;
            SortMenu.Items.Add(item);
        }
        foreach (var range in Enum.GetValues<DateRangePreset>())
        {
            var item = new MenuFlyoutItem { Text = OrdersViewModel.RangeLabel(range) };
            item.Click += (_, _) => ViewModel.ApplyRange(range);
            RangeMenu.Items.Add(item);
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrdersViewModel.Sort))
        {
            ShowSort();
        }
    }

    private static bool _filterInitialized;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelChanged;
        // Bộ lọc mặc định (Đã lên đơn, hôm nay) chỉ ở lần đầu mở trang trong phiên; vào lại từ menu thì giữ điều kiện đã chọn.
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

    /// <summary>
    /// ViewModel sống cả phiên, trang thì không - gỡ mọi chỗ nghe ViewModel (x:Bind, danh sách thẻ): trang cũ đã gỡ khỏi cửa sổ
    /// mà còn nghe Cards thì ném COMException "Unspecified error" khi danh sách đổi.
    /// </summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
        Bindings.StopTracking();
        CardsRepeater.ItemsSource = null;
    }

    private bool _syncing;

    private void SyncStatusBar()
    {
        _syncing = true;
        var tag = ViewModel.Status is { } s ? ((int)s).ToString() : "0";
        StatusBar.SelectedItem = StatusBar.Items.First(i => (string)i.Tag == tag);
        _syncing = false;
    }

    /// <summary>Nút Sắp xếp hiện cách đang chọn (đậm khi khác mặc định, như icon đổi màu của điện thoại).</summary>
    private void ShowSort()
    {
        foreach (var item in SortMenu.Items.OfType<RadioMenuFlyoutItem>())
        {
            item.IsChecked = (OrderSort)item.Tag == ViewModel.Sort;
        }
        SortText.Text = ViewModel.Sort == OrderSort.Default ? "Sắp xếp" : OrdersViewModel.SortLabel(ViewModel.Sort);
        SortText.FontWeight = ViewModel.Sort == OrderSort.Default ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
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

    private void Refresh_Click(object sender, RoutedEventArgs e) => ViewModel.Load();

    private void Add_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(OrderFormPage), ViewModel.NewOrderArgs());

    private void Detail_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(OrderDetailPage), new OrderDetailArgs(ViewModel.Filter));

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OrderCardViewModel card })
        {
            Frame.Navigate(typeof(OrderFormPage), new OrderFormArgs(card.Id));
        }
    }
}
