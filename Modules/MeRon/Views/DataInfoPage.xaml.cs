using MeRon.Core;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>Thông tin dữ liệu: tổng quan, theo năm → tháng, dọn dữ liệu cũ (xem DataInfoViewModel).</summary>
public sealed partial class DataInfoPage : Page
{
    public DataInfoViewModel ViewModel { get; } = new();

    public DataInfoPage()
    {
        InitializeComponent();
        Shortcuts.Add(this, Windows.System.VirtualKey.F5, Windows.System.VirtualKeyModifiers.None, ViewModel.Refresh);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Refresh();

    private void Refresh_Click(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    private void Period_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PeriodRow row })
        {
            ViewModel.Toggle(row);
        }
    }

    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox is { SelectedIndex: >= 0 } box)
        {
            ViewModel.Preset = (CleanupPreset)box.SelectedIndex;
        }
    }

    /// <summary>Xác nhận (nút chọn sẵn = Huỷ) với đúng số dòng sẽ xoá, rồi xuất backup + xoá.</summary>
    private async void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshPlan();
        if (ViewModel.CurrentPlan is not { IsEmpty: false })
        {
            return;
        }
        var message = ViewModel.PlanText +
                      (ViewModel.HasKeptUnpaid ? "\n\n" + ViewModel.KeptUnpaidText : string.Empty) +
                      "\n\nTrước khi xoá, app tự xuất bộ backup TSV vào:\n" + ViewModel.BackupParent +
                      "\n(khôi phục lại được ở khung Khôi phục phần đã dọn)\n\nĐiện thoại không bị đụng tới - muốn dọn cả điện thoại thì xuất rồi nhập vào điện thoại.";
        if (await Dialogs.ConfirmAsync(XamlRoot, "Xoá dữ liệu cũ?", message, "Xoá dữ liệu cũ"))
        {
            await ViewModel.CleanupAsync();
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CleanupRow row })
        {
            return;
        }
        var message = $"{row.Title}\n{row.Detail}\n\nChỉ thêm lại các dòng đang thiếu; dữ liệu hiện có giữ nguyên. Database hiện tại được chép bản sao trước.";
        if (await Dialogs.ConfirmAsync(XamlRoot, "Khôi phục phần đã dọn?", message, "Khôi phục"))
        {
            await ViewModel.RestoreAsync(row);
        }
    }

    private void OpenPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            DataInfoViewModel.Open(path);
        }
    }

    /// <summary>Trang Chi tiết lọc "Đã lên đơn" giao trước mốc - thu tiền (Thanh toán toàn bộ) hoặc huỷ từng đơn.</summary>
    private void ShowKeptUnpaid_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(OrderDetailPage), new ViewModels.OrderDetailArgs(ViewModel.KeptUnpaidFilter));

    private void OpenExport_Click(object sender, RoutedEventArgs e) => App.MainWindow?.SelectMenu("ExportPage");

    // ---------- x:Bind helpers ----------

    public static bool CanRun(bool canCleanup, bool isBusy) => canCleanup && !isBusy;

    public static Brush RowBackground(bool isYear) =>
        isYear ? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public static Thickness RowIndent(bool isYear) => isYear ? new Thickness(0) : new Thickness(22, 0, 0, 0);

    public static Windows.UI.Text.FontWeight RowWeight(bool isYear) => isYear ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    /// <summary>Tháng không có gì: mờ đi.</summary>
    public static double RowOpacity(bool isEmpty) => isEmpty ? 0.45 : 1;
}
