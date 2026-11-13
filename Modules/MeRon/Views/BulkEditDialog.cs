using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MeRon.Views;

/// <summary>
/// Hộp thoại sửa hàng loạt (trang Đặt hàng, Chọn nhiều): ô nhập (ngày, món...) + xem trước ngay bên dưới - đơn sẽ đổi (đơn đã
/// thanh toán tô cam) và đơn bỏ qua kèm lý do; nút Áp dụng ghi số đơn. Đổi ô nhập là xem trước tính lại.
/// </summary>
internal static class BulkEditDialog
{
    private const int MaxLines = 200;

    /// <param name="build">Tính kế hoạch theo ô nhập hiện tại; Plan null = ô nhập chưa đủ, Hint = lời nhắc.</param>
    /// <param name="watch">Đăng ký "ô nhập vừa đổi" → xem trước tính lại.</param>
    public static async Task<BulkPlan?> ShowAsync(XamlRoot root, string title, UIElement? input, Func<(BulkPlan? Plan, string? Hint)> build,
        Action<Action>? watch = null, bool delete = false)
    {
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(summary, "BulkPreviewSummary");
        var lines = new StackPanel { Spacing = 2 };
        var panel = new StackPanel { Spacing = 10, MinWidth = 460 };
        if (input is not null)
        {
            panel.Children.Add(input);
        }
        if (delete)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Xoá hẳn khỏi dữ liệu (khác Huỷ: đơn huỷ vẫn giữ để tra cứu). Lấy lại được bằng nút Hoàn tác ngay sau khi xoá.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            });
        }
        panel.Children.Add(summary);
        panel.Children.Add(new ScrollViewer { Content = lines, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) });

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, // ContentDialog không tự cuộn
            CloseButtonText = "Huỷ bỏ",
            DefaultButton = ContentDialogButton.None, // Close mặc định cũng tô màu nhấn → 2 nút cam cạnh nhau dễ bấm nhầm
            PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;

        BulkPlan? current = null;
        void Refresh()
        {
            var (plan, hint) = build();
            current = plan;
            lines.Children.Clear();
            if (plan is null)
            {
                summary.Text = hint ?? string.Empty;
                dialog.PrimaryButtonText = delete ? "Xoá" : "Áp dụng";
                dialog.IsPrimaryButtonEnabled = false;
                return;
            }
            var n = plan.Changes.Count;
            summary.Text = (plan.Rename is { } r ? $"Danh mục: \"{r.OldName}\" → \"{r.NewName}\"  ·  " : string.Empty)
                           + (n == 0 ? "Không có đơn nào cần đổi." : $"{n} đơn sẽ {(delete ? "xoá" : "đổi")}")
                           + (plan.WarnCount > 0 ? $"  ·  {plan.WarnCount} đơn đã thanh toán (tô cam)" : string.Empty)
                           + (plan.Skipped.Count > 0 ? $"  ·  bỏ qua {plan.Skipped.Count} đơn" : string.Empty);
            var caution = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            foreach (var change in plan.Changes.Take(MaxLines))
            {
                lines.Children.Add(new TextBlock
                {
                    Text = $"• {Who(change.Before)}: {change.Description}" + (change.Warn ? "  (đã thanh toán)" : string.Empty),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = change.Warn ? caution : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                });
            }
            if (plan.Changes.Count > MaxLines)
            {
                lines.Children.Add(new TextBlock { Text = $"... và {plan.Changes.Count - MaxLines} đơn nữa", Opacity = 0.7 });
            }
            if (plan.Skipped.Count > 0)
            {
                lines.Children.Add(new TextBlock { Text = "Bỏ qua (không đổi):", Margin = new Thickness(0, 8, 0, 0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.8 });
                foreach (var skip in plan.Skipped.Take(MaxLines))
                {
                    lines.Children.Add(new TextBlock { Text = $"• {Who(skip.Order)}: {skip.Reason}", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
                }
            }
            dialog.PrimaryButtonText = delete ? $"Xoá {n} đơn" : n == 0 && plan.Rename is not null ? "Đổi tên trong Danh mục" : $"Áp dụng cho {n} đơn";
            dialog.IsPrimaryButtonEnabled = n > 0 || plan.Rename is not null;
        }
        watch?.Invoke(Refresh);
        Refresh();
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? current : null;
    }

    private static string Who(Order order) =>
        $"{(string.IsNullOrWhiteSpace(order.UserName) ? "(không có khách)" : order.UserName)} · {DbDate.Show(order.Delivery)} · {VnFormat.Money(order.Total)}";

    /// <summary>Ô chọn ngày (DateBox như các trang khác) + báo khi đổi.</summary>
    public static (DateBox Box, Action<Action> Watch) DateInput(string header, DateOnly initial)
    {
        var box = new DateBox
        {
            Header = header,
            Date = new DateTimeOffset(initial.ToDateTime(TimeOnly.MinValue)),
            AllowEmpty = false,
            NearToday = true,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, header);
        return (box, refresh => box.RegisterPropertyChangedCallback(DateBox.DateProperty, (_, _) => refresh()));
    }

    public static DateOnly? DayOf(DateBox box) => box.Date is { } d ? DateOnly.FromDateTime(d.Date) : null;
}
