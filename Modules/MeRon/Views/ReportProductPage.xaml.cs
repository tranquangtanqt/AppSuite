using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>Thống kê theo món ăn. Vào từ menu = tất cả món, hôm nay; quay lại từ form đơn = giữ bộ lọc.</summary>
public sealed partial class ReportProductPage : Page
{
    public ReportProductViewModel ViewModel { get; } = new();

    public ReportProductPage()
    {
        InitializeComponent();
    }

    private bool _filterInitialized;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        // Trang được cache: bộ lọc mặc định chỉ ở lần đầu mở; vào lại từ menu thì giữ điều kiện đã chọn.
        if (!_filterInitialized)
        {
            ViewModel.Reset();
            _filterInitialized = true;
        }
        ViewModel.LoadProducts();
        ViewModel.Load();
    }

    private void Order_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OrderCardViewModel card })
        {
            Frame.Navigate(typeof(OrderFormPage), new OrderFormArgs(card.Id));
        }
    }
}
