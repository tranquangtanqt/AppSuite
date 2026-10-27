using MeRon.Core.Models;
using MeRon.Services;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MeRon.Views;

public sealed partial class OrderCard : UserControl
{
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card), typeof(OrderCardViewModel), typeof(OrderCard), new PropertyMetadata(null, (d, _) => ((OrderCard)d).Bindings.Update()));

    /// <summary>False: không hiện tên khách trên thẻ (trang Chi tiết đã có tên ở đầu nhóm) - ngày giao lên đầu thẻ.</summary>
    public static readonly DependencyProperty ShowCustomerProperty = DependencyProperty.Register(
        nameof(ShowCustomer), typeof(bool), typeof(OrderCard), new PropertyMetadata(true, (d, _) => ((OrderCard)d).ApplyShowCustomer()));

    public OrderCard()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachMenu();
    }

    public OrderCardViewModel Card
    {
        get => (OrderCardViewModel)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    public bool ShowCustomer
    {
        get => (bool)GetValue(ShowCustomerProperty);
        set => SetValue(ShowCustomerProperty, value);
    }

    private void ApplyShowCustomer()
    {
        CustomerText.Visibility = ShowCustomer ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(DeliveryPanel, ShowCustomer ? 1 : 0);
    }

    /// <summary>
    /// Menu chuột phải gắn vào nút bọc thẻ (Đặt hàng, Chi tiết, Theo món ăn) chứ không vào thẻ: nút là chỗ có focus nên
    /// Shift+F10 / phím menu cũng mở được. Thẻ được tái dùng khi cuộn nên mỗi lần mở dựng menu mới theo <see cref="Card"/>
    /// (ContextFlyout dựng mục trong Opening thì không mở được vì lúc đó menu còn rỗng): Sao chép đơn; Đã thanh toán (đơn Đã
    /// lên đơn); Giao hôm nay / ngày mai, Huỷ đơn (đơn chưa Huỷ) - không phải mở form.
    /// </summary>
    private void AttachMenu()
    {
        DependencyObject? node = this;
        while (node is not null and not Button and not Page)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        if (node is Button button)
        {
            button.ContextRequested -= Button_ContextRequested;
            button.ContextRequested += Button_ContextRequested;
        }
    }

    private void Button_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (BuildMenu() is not { } menu)
        {
            return;
        }
        args.Handled = true;
        if (args.TryGetPosition(sender, out var point))
        {
            menu.ShowAt(sender, point);
        }
        else
        {
            menu.ShowAt((FrameworkElement)sender);
        }
    }

    private MenuFlyout? BuildMenu()
    {
        if (Card is not { } card)
        {
            return null;
        }
        var menu = new MenuFlyout();
        Add(menu, "Sao chép đơn", Symbol.Copy, () =>
        {
            if (FindPage() is { Frame: { } frame })
            {
                frame.Navigate(typeof(OrderFormPage), new OrderFormArgs(null, CopyFromId: card.Id));
            }
        });
        if (card.Status == OrderStatus.Cancelled)
        {
            return menu;
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        if (card.Status == OrderStatus.Shipping)
        {
            Add(menu, "Đã thanh toán", Symbol.Accept, () => Run("Thanh toán", () => AppServices.Orders.PayShipping([card.Id])));
        }
        var today = DateOnly.FromDateTime(DateTime.Today);
        var current = card.Order.Delivery is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        foreach (var (label, day) in new[] { ("Giao hôm nay", today), ("Giao ngày mai", today.AddDays(1)) })
        {
            if (day != current)
            {
                Add(menu, $"{label} ({day:dd/MM})", Symbol.Calendar,
                    () => Run("Đổi ngày giao", () => AppServices.Orders.SetDeliveryDay(card.Id, day)));
            }
        }
        Add(menu, "Huỷ đơn", Symbol.Cancel, async () =>
        {
            if (await Dialogs.ConfirmAsync(XamlRoot, "Huỷ đơn?",
                    $"Huỷ đơn của {card.CustomerName} ({card.DeliveryText}, {card.TotalText})? Đơn vẫn được giữ lại để tra cứu, không tính vào báo cáo.",
                    "Huỷ đơn"))
            {
                Run("Huỷ đơn", () => AppServices.Orders.SetStatus(card.Id, OrderStatus.Cancelled));
            }
        });
        return menu;
    }

    private static void Add(MenuFlyout menu, string text, Symbol icon, Action click)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new SymbolIcon(icon) };
        item.Click += (_, _) => click();
        menu.Items.Add(item);
    }

    /// <summary>Ghi rồi bảo trang đang chứa thẻ tải lại (<see cref="IOrderListPage"/>); lỗi thì báo bằng hộp thoại.</summary>
    private async void Run(string what, Action action)
    {
        var page = FindPage();
        var root = XamlRoot;
        if (AppServices.Try(what, action) is { } error)
        {
            await Dialogs.ShowMessageAsync(root, $"{what} không được", error);
            return;
        }
        (page as IOrderListPage)?.ReloadOrders();
    }

    private Page? FindPage()
    {
        DependencyObject? node = this;
        while (node is not null and not Page)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        return node as Page;
    }
}

/// <summary>Trang có danh sách thẻ đơn: tải lại sau khi menu chuột phải của thẻ đổi đơn.</summary>
public interface IOrderListPage
{
    void ReloadOrders();
}
