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
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.NavigationMode == NavigationMode.New)
        {
            ViewModel.Range.Reset();
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
