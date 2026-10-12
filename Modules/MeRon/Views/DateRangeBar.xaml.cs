using MeRon.Core;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MeRon.Views;

/// <summary>Từ ngày – Đến ngày + nút chọn nhanh khoảng ngày, gắn với 1 <see cref="DateRangeFilter"/>.</summary>
public sealed partial class DateRangeBar : UserControl
{
    public static readonly DependencyProperty FilterProperty = DependencyProperty.Register(
        nameof(Filter), typeof(DateRangeFilter), typeof(DateRangeBar), new PropertyMetadata(null, (d, _) => ((DateRangeBar)d).Bindings.Update()));

    public DateRangeBar()
    {
        InitializeComponent();
        foreach (var preset in Enum.GetValues<DateRangePreset>())
        {
            var item = new MenuFlyoutItem { Text = DateRanges.Label(preset) };
            item.Click += (_, _) => Filter?.Apply(preset);
            RangeMenu.Items.Add(item);
        }
    }

    public DateRangeFilter Filter
    {
        get => (DateRangeFilter)GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }
}
