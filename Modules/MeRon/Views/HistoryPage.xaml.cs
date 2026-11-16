using MeRon.Core.History;
using MeRon.Services;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>Lịch sử chỉnh sửa - xem <see cref="HistoryViewModel"/>. Trang cache: giữ bộ lọc khi chuyển trang.</summary>
public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = new();

    public HistoryPage()
    {
        InitializeComponent();
        Shortcuts.Search(this, SearchBox);
        foreach (var label in HistoryViewModel.KindLabels)
        {
            KindBox.Items.Add(label);
        }
    }

    /// <summary>Mỗi lần vào trang đọc lại (vừa sửa đơn ở trang khác); bộ lọc giữ nguyên.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.Message = string.Empty;
        ViewModel.Load();
    }

    private static HistoryRow? RowOf(object sender) => (sender as FrameworkElement)?.Tag as HistoryRow;

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
        {
            return;
        }
        var plan = HistoryViewModel.Plan([row.Entry])[0];
        if (plan.Blocked is { } reason)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Không khôi phục", reason);
            return;
        }
        var text = $"Đưa đơn {row.Entry.OrderId} ({row.CustomerText}) về như trước lần \"{row.Action}\" lúc {row.TimeText}:\n"
                   + string.Join("\n", plan.Changes.Select(c => "• " + c))
                   + (plan.Warnings.Count > 0 ? "\n\n" + string.Join("\n", plan.Warnings.Select(w => "⚠ " + w)) : string.Empty);
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Khôi phục đơn?", text, "Khôi phục"))
        {
            return;
        }
        if (ViewModel.Restore([plan], $"đơn {row.Entry.OrderId} - {row.CustomerText}") is { } error)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Không khôi phục được", error);
        }
    }

    private async void RestoreBatch_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
        {
            return;
        }
        var plans = HistoryViewModel.PlanBatch(row.Entry);
        var usable = plans.Where(p => p.Blocked is null).ToList();
        var skipped = plans.Where(p => p.Blocked is not null).Select(p => $"• Đơn {p.Entry.OrderId} ({CustomerOf(p)}): {p.Blocked}").ToList();
        if (usable.Count == 0)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Không khôi phục", "Không có đơn nào cần khôi phục:\n" + string.Join("\n", skipped));
            return;
        }
        const int MaxShown = 15;
        var lines = usable.Take(MaxShown).Select(p => $"• Đơn {p.Entry.OrderId} ({CustomerOf(p)}): {string.Join("; ", p.Changes)}").ToList();
        if (usable.Count > MaxShown)
        {
            lines.Add($"… và {usable.Count - MaxShown} đơn nữa");
        }
        var warnings = usable.SelectMany(p => p.Warnings.Select(w => $"⚠ Đơn {p.Entry.OrderId}: {w}")).ToList();
        var text = $"Khôi phục {usable.Count} đơn về như trước lần \"{row.Action}\" lúc {row.TimeText}:\n" + string.Join("\n", lines)
                   + (skipped.Count > 0 ? $"\n\nBỏ qua {skipped.Count} đơn:\n" + string.Join("\n", skipped) : string.Empty)
                   + (warnings.Count > 0 ? "\n\n" + string.Join("\n", warnings) : string.Empty);
        if (!await Dialogs.ConfirmAsync(XamlRoot, $"Khôi phục {usable.Count} đơn?", text, "Khôi phục"))
        {
            return;
        }
        if (ViewModel.Restore(usable, $"lô \"{row.Action}\" lúc {row.TimeText}") is { } error)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Không khôi phục được", error);
        }
    }

    private static string CustomerOf(RestorePlan plan) =>
        string.IsNullOrWhiteSpace(plan.Entry.CustomerName) ? "không có khách" : plan.Entry.CustomerName.Trim();

    /// <summary>Mở đơn hiện tại trong form sửa (đơn đã xoá thì báo).</summary>
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
        {
            return;
        }
        if (AppServices.Orders.Get(row.Entry.OrderId) is null)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Đơn không còn",
                $"Đơn {row.Entry.OrderId} không còn trong dữ liệu (đã xoá, hoặc đã nhập lại backup). Muốn lấy lại: bấm Khôi phục ở dòng \"Xoá\" của đơn đó.");
            return;
        }
        Frame.Navigate(typeof(OrderFormPage), new OrderFormArgs(row.Entry.OrderId));
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
