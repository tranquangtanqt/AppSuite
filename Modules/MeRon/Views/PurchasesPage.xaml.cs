using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>Tham số form phiếu nhập: <see cref="Id"/> null = phiếu mới.</summary>
public sealed record PurchaseFormArgs(long? Id, DateOnly? InitialDay = null);

/// <summary>Danh sách phiếu nhập. Vào từ menu = hôm nay; quay lại từ form = giữ khoảng ngày (như trang Đặt hàng).</summary>
public sealed partial class PurchasesPage : Page
{
    public PurchasesViewModel ViewModel { get; } = new();

    public PurchasesPage()
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

    private void Add_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(PurchaseFormPage),
            new PurchaseFormArgs(null, ViewModel.Range.From is { } f && f == ViewModel.Range.To ? f : null));

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PurchaseCardViewModel card })
        {
            Frame.Navigate(typeof(PurchaseFormPage), new PurchaseFormArgs(card.Id));
        }
    }
}
