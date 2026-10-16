using MeRon.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>Biểu đồ (Thống kê): chỉ dữ liệu đơn hàng - xem ChartsViewModel.</summary>
public sealed partial class ChartsPage : Page
{
    public ChartsViewModel ViewModel { get; } = new();

    public ChartsPage()
    {
        InitializeComponent();
        Shortcuts.Ctrl(this, Windows.System.VirtualKey.D, RangeBar.ShowPresets);
        Shortcuts.Add(this, Windows.System.VirtualKey.F5, Windows.System.VirtualKeyModifiers.None, ViewModel.Load);

        SalesChart.Format = ChartsViewModel.CompactMoney;
        SalesChart.Series = [ChartColors.Sales];
        WeekdayChart.Series = [new ChartSeries("Số đơn", ChartColors.Slot1.Light, ChartColors.Slot1.Dark)];
        // Cột chồng: quay lại (dưới, xanh) + mới (trên, cam) - thứ tự slot cố định của palette phân loại.
        MixChart.Series =
        [
            new ChartSeries("Khách quay lại", ChartColors.Slot1.Light, ChartColors.Slot1.Dark),
            new ChartSeries("Khách mới", ChartColors.Slot2.Light, ChartColors.Slot2.Dark),
        ];
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChartsViewModel.Report))
            {
                Show();
            }
        };
    }

    private bool _filterInitialized;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        // Lần đầu mở: 30 ngày qua; vào lại từ menu thì giữ khoảng ngày đã chọn.
        if (!_filterInitialized)
        {
            ViewModel.SetDefaultRange();
            _filterInitialized = true;
        }
        ViewModel.Load();
    }

    private void Show()
    {
        KpiList.ItemsSource = ViewModel.Kpis;
        SalesChart.Columns = ViewModel.SalesColumns;
        WeekdayChart.Columns = ViewModel.WeekdayColumns;
        MixChart.Columns = ViewModel.MixColumns;
        TopList.Bars = ViewModel.TopBars;
        SalesTable.Show(["Kỳ", "Số đơn", "Doanh thu"], ViewModel.SalesTable);
        WeekdayTable.Show(["Thứ", "Số đơn", "Doanh thu"], ViewModel.WeekdayTable);
        MixTable.Show(["Kỳ", "Khách quay lại", "Khách mới"], ViewModel.MixTable);
        TopTable.Show(["Món", "Số phần", "Doanh thu"], ViewModel.TopTable);
    }
}
