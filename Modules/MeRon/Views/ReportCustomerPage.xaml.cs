using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class ReportCustomerPage : Page
{
    public ReportCustomerViewModel ViewModel { get; } = new();

    public ReportCustomerPage()
    {
        InitializeComponent();
        Shortcuts.Ctrl(this, Windows.System.VirtualKey.D, RangeBar.ShowPresets);
    }

    private bool _filterInitialized;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        // Trang được cache: khoảng ngày mặc định chỉ ở lần đầu mở; vào lại từ menu thì giữ điều kiện đã chọn.
        if (!_filterInitialized)
        {
            ViewModel.Range.Reset();
            _filterInitialized = true;
        }
        ViewModel.Load();
    }

    private void Customer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CustomerReportRow row })
        {
            Frame.Navigate(typeof(OrderDetailPage), ViewModel.DetailArgs(row));
        }
    }
}
