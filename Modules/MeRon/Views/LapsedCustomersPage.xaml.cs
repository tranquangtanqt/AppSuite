using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Reports;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace MeRon.Views;

/// <summary>Khách lâu không đặt - xem <see cref="LapsedCustomersViewModel"/>. Trang cache: giữ bộ lọc khi chuyển trang.</summary>
public sealed partial class LapsedCustomersPage : Page
{
    public LapsedCustomersViewModel ViewModel { get; } = new();

    public LapsedCustomersPage()
    {
        InitializeComponent();
        Shortcuts.Search(this, SearchBox);
        foreach (var days in LapsedCustomersViewModel.DayPresets)
        {
            var item = new MenuFlyoutItem { Text = $"{days} ngày" };
            item.Click += (_, _) => ViewModel.MinDays = days;
            DaysMenu.Items.Add(item);
        }
        foreach (var sort in Enum.GetValues<LapsedSort>())
        {
            var item = new RadioMenuFlyoutItem { Text = LapsedCustomersViewModel.SortLabel(sort), Tag = sort, GroupName = "sort" };
            item.Click += (_, _) =>
            {
                ViewModel.Sort = sort;
                ShowSort();
            };
            SortMenu.Items.Add(item);
        }
        ShowSort();
    }

    /// <summary>Mỗi lần vào trang đọc lại (vừa nhập dữ liệu / thêm đơn); bộ lọc giữ nguyên.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.Message = string.Empty;
        ViewModel.Load();
    }

    private void ShowSort()
    {
        foreach (var item in SortMenu.Items.OfType<RadioMenuFlyoutItem>())
        {
            item.IsChecked = (LapsedSort)item.Tag == ViewModel.Sort;
        }
        SortText.Text = ViewModel.Sort == LapsedSort.Longest ? "Sắp xếp" : LapsedCustomersViewModel.SortLabel(ViewModel.Sort);
        SortText.FontWeight = ViewModel.Sort == LapsedSort.Longest ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
    }

    private static LapsedCustomer? CustomerOf(object sender) => (sender as FrameworkElement)?.Tag is LapsedRow row ? row.Customer : null;

    /// <summary>Mọi đơn của khách (mọi ngày, mọi trạng thái) ở màn Chi tiết.</summary>
    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (CustomerOf(sender) is { } customer)
        {
            Frame.Navigate(typeof(OrderDetailPage), new OrderDetailArgs(new OrderFilter(null, null, null, customer.UserId)));
        }
    }

    /// <summary>Đặt lại như lần trước: form đơn mới chép từ đơn gần nhất, giao hôm nay.</summary>
    private void Reorder_Click(object sender, RoutedEventArgs e)
    {
        if (CustomerOf(sender) is { } customer)
        {
            Frame.Navigate(typeof(OrderFormPage),
                new OrderFormArgs(null, InitialDelivery: DateOnly.FromDateTime(DateTime.Today), CopyFromId: customer.LastOrderId));
        }
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (CustomerOf(sender) is { } customer)
        {
            CopyText(LapsedCustomers.ZaloMessage(customer));
            ViewModel.Message = $"Đã chép tin nhắn cho {customer.Name} - dán vào Zalo bằng Ctrl+V.";
        }
    }

    private void CopyPhone_Click(object sender, RoutedEventArgs e)
    {
        if (CustomerOf(sender) is { Phone: { } phone } customer)
        {
            CopyText(phone);
            ViewModel.Message = $"Đã chép số điện thoại của {customer.Name}: {phone}";
        }
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
