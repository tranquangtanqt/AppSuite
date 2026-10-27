using MeRon.Core.Models;
using MeRon.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MeRon.Views;

/// <summary>
/// Hộp thoại Thêm / Sửa khách hàng (Tên / Số điện thoại / Địa chỉ / Ghi chú như điện thoại; Xoá chỉ có khi sửa) - dùng ở trang
/// Khách hàng và ở ô Khách hàng của form đơn ("＋ Thêm khách mới", điền sẵn tên đang gõ, con trỏ ở ô Số điện thoại).
/// </summary>
internal static class CustomerForm
{
    /// <summary>Trả về mã khách vừa lưu / xoá; null = bấm Huỷ.</summary>
    public static async Task<long?> ShowAsync(XamlRoot root, Customer? customer, string? initialName = null)
    {
        var name = Dialogs.Text("Tên", customer?.Name ?? initialName);
        var phone = Dialogs.Text("Số điện thoại", customer?.Phone);
        // Ô gõ tự do (như điện thoại) + danh sách chọn nhanh các địa chỉ trong danh mục.
        var address = new ComboBox
        {
            Header = "Địa chỉ",
            IsEditable = true,
            ItemsSource = AppServices.MasterData.GetAddresses().Select(a => a.Name ?? a.Code).Distinct().ToList(),
            Text = customer?.Address ?? string.Empty,
            PlaceholderText = "Gõ hoặc chọn",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(address, "Địa chỉ");
        var note = Dialogs.Text("Ghi chú", customer?.Note, multiline: true);
        // ContentDialog tự đặt focus vào ô đầu khi mở (sau Loaded) → đặt lại sau 1 nhịp.
        var first = customer is null && !string.IsNullOrWhiteSpace(initialName) ? phone : name;
        first.Loaded += (_, _) =>
        {
            first.Focus(FocusState.Programmatic);
            first.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => first.Focus(FocusState.Programmatic));
        };

        var id = customer?.Id ?? 0;
        var changed = await Dialogs.ShowFormAsync(root, customer is null ? "Thêm khách hàng" : "Sửa khách hàng",
            [name, phone, address, note],
            () => AppServices.Try("Lưu khách hàng",
                () => id = AppServices.MasterData.SaveCustomer(new Customer(id, name.Text, address.Text, phone.Text, note.Text))),
            customer is null ? null : () => AppServices.Try("Xoá khách hàng", () => AppServices.MasterData.DeleteCustomer(id)),
            $"Xoá khách hàng \"{customer?.Name}\"? Không hoàn tác được.");
        return changed ? id : null;
    }
}
