using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MeRon.Views;

public sealed partial class OrderCard : UserControl
{
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card), typeof(OrderCardViewModel), typeof(OrderCard), new PropertyMetadata(null, (d, _) => ((OrderCard)d).Bindings.Update()));

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

    /// <summary>
    /// Menu chuột phải "Sao chép đơn" đặt lên nút bọc thẻ (Đặt hàng, Chi tiết, Theo món ăn) chứ không lên thẻ: nút là chỗ
    /// có focus nên Shift+F10 / phím menu cũng mở được. Thẻ được tái dùng khi cuộn nên đọc <see cref="Card"/> lúc bấm.
    /// </summary>
    private void AttachMenu()
    {
        DependencyObject? node = this;
        while (node is not null and not Button and not Page)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        if (node is not Button { ContextFlyout: null } button)
        {
            return;
        }
        var copy = new MenuFlyoutItem { Text = "Sao chép đơn", Icon = new SymbolIcon(Symbol.Copy) };
        copy.Click += (_, _) =>
        {
            if (Card is { } card && FindPage() is { Frame: { } frame })
            {
                frame.Navigate(typeof(OrderFormPage), new OrderFormArgs(null, CopyFromId: card.Id));
            }
        };
        var menu = new MenuFlyout();
        menu.Items.Add(copy);
        button.ContextFlyout = menu;
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
