using MeRon.Core;
using MeRon.Core.Reports;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace MeRon.Views;

/// <summary>Công nợ - xem <see cref="DebtsViewModel"/>. Trang cache: giữ bộ lọc khi chuyển trang.</summary>
public sealed partial class DebtsPage : Page
{
    public DebtsViewModel ViewModel { get; } = new();

    public DebtsPage()
    {
        InitializeComponent();
        Shortcuts.Search(this, SearchBox);
        foreach (var sort in Enum.GetValues<DebtSort>())
        {
            var item = new RadioMenuFlyoutItem { Text = DebtsViewModel.SortLabel(sort), Tag = sort, GroupName = "sort" };
            item.Click += (_, _) =>
            {
                ViewModel.Sort = sort;
                ShowSort();
            };
            SortMenu.Items.Add(item);
        }
        ShowSort();
    }

    /// <summary>Mỗi lần vào trang đọc lại (vừa nhập dữ liệu / sửa đơn / thanh toán ở màn Chi tiết); bộ lọc giữ nguyên.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.Message = string.Empty;
        ViewModel.Load();
    }

    private void ShowSort()
    {
        foreach (var item in SortMenu.Items.OfType<RadioMenuFlyoutItem>())
        {
            item.IsChecked = (DebtSort)item.Tag == ViewModel.Sort;
        }
        SortText.Text = DebtsViewModel.SortLabel(ViewModel.Sort);
    }

    private static CustomerDebt? DebtOf(object sender) => (sender as FrameworkElement)?.Tag is DebtRow row ? row.Debt : null;

    /// <summary>Chi tiết: màn Chi tiết chỉ các đơn đang nợ của khách, có dòng tiêu đề công nợ - ở đó Chụp ảnh (gửi Zalo),
    /// Thanh toán toàn bộ, bấm 1 đơn để sửa. Không tự chụp (người dùng 2026-10-23: tự chụp mỗi lần bấm sinh nhiều ảnh thừa).</summary>
    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (DebtOf(sender) is { UserId: not null } debt)
        {
            Frame.Navigate(typeof(OrderDetailPage), new OrderDetailArgs(ViewModel.DetailFilter(debt), DebtsViewModel.CaptureTitle(debt)));
        }
    }

    private async void PayAll_Click(object sender, RoutedEventArgs e)
    {
        if (DebtOf(sender) is not { } debt)
        {
            return;
        }
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Thanh toán hết",
                $"{debt.Name}: chuyển {VnFormat.Number(debt.OrderCount)} đơn chưa thanh toán (tổng {VnFormat.Money(debt.Amount)}) sang \"Đã thanh toán\"?",
                "Thanh toán hết"))
        {
            return;
        }
        if (ViewModel.PayAll(debt) is { } error)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Không thanh toán được", error);
        }
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (DebtOf(sender) is { UserId: not null } debt)
        {
            CopyText(CustomerDebts.ReminderMessage(debt));
            ViewModel.Message = $"Đã chép tin nhắn nhắc thanh toán cho {debt.Name} - dán vào Zalo bằng Ctrl+V (Chi tiết → Chụp ảnh để gửi kèm ảnh).";
        }
    }

    private void CopyPhone_Click(object sender, RoutedEventArgs e)
    {
        if (DebtOf(sender) is { Phone: { } phone } debt)
        {
            CopyText(phone);
            ViewModel.Message = $"Đã chép số điện thoại của {debt.Name}: {phone}";
        }
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
