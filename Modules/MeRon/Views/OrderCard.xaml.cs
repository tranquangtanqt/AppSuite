using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MeRon.Views;

public sealed partial class OrderCard : UserControl
{
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card), typeof(OrderCardViewModel), typeof(OrderCard), new PropertyMetadata(null, (d, _) => ((OrderCard)d).Bindings.Update()));

    public OrderCard()
    {
        InitializeComponent();
    }

    public OrderCardViewModel Card
    {
        get => (OrderCardViewModel)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }
}
