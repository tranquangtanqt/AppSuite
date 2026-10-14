using MeRon.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class ReportSummaryPage : Page
{
    public ReportSummaryViewModel ViewModel { get; } = new();

    public ReportSummaryPage()
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
}
