using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MeRon.Views;

/// <summary>
/// "Chọn khách đúng" cho đơn đang ghi vào khách chung "Chưa rõ khách" (2026-11-16): ô tìm điền sẵn chữ đã ghi (vd "Nhung") →
/// danh sách khách khớp (không dấu, mọi từ); bấm 1 khách rồi Chọn. "＋ Thêm khách mới" mở form khách với tên đó. Trả id khách
/// đã chọn, null = huỷ.
/// </summary>
public static class PickCustomerDialog
{
    public static async Task<long?> ShowAsync(XamlRoot root, string hint, string orderText)
    {
        var customers = AppServices.MasterData.GetCustomers()
            .Where(c => !OrderConfirm.IsUnknownCustomer(c.Name))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var search = new TextBox
        {
            Header = "Tìm khách (tên / SĐT / địa chỉ, không cần dấu)", Text = hint, PlaceholderText = "vd Nhung",
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, "Tìm khách đúng");
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 280, MinHeight = 120 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Khách có thể đúng");
        var count = new TextBlock { Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        var addButton = new HyperlinkButton { Content = "＋ Thêm khách mới…", Padding = new Thickness(0, 4, 0, 4) };

        void Fill()
        {
            var shown = TextSearch.Filter(customers, c => $"{c.Name} {c.Phone} {c.Address}", search.Text, 200);
            list.Items.Clear();
            foreach (var c in shown)
            {
                var item = new ListViewItem { Content = Label(c), Tag = c.Id };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, Label(c));
                list.Items.Add(item);
            }
            if (shown.Count == 1)
            {
                list.SelectedIndex = 0;
            }
            count.Text = shown.Count == 0 ? "Không có khách nào khớp - sửa chữ tìm, hoặc Thêm khách mới."
                : $"{VnFormat.Number(shown.Count)} khách khớp - bấm chọn 1 khách.";
        }

        search.TextChanged += (_, _) => Fill();
        Fill();

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Chọn khách đúng",
            PrimaryButtonText = "Chọn",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 8, Width = 460,
                    Children =
                    {
                        new TextBlock { Text = orderText, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 },
                        search, count, list, addButton,
                    },
                },
            },
        };
        dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;

        // Thêm khách mới: đóng hộp này trước (mỗi lúc chỉ mở được 1 ContentDialog) rồi mới mở form khách.
        var wantAdd = false;
        addButton.Click += (_, _) =>
        {
            wantAdd = true;
            dialog.Hide();
        };

        var result = await dialog.ShowAsync();
        if (wantAdd)
        {
            return await CustomerForm.ShowAsync(root, null, search.Text.Trim().Length > 0 ? search.Text.Trim() : hint);
        }
        return result == ContentDialogResult.Primary && list.SelectedItem is ListViewItem { Tag: long id } ? id : null;
    }

    private static string Label(Customer c) =>
        string.Join("  ·  ", new[] { c.Name, c.Address, c.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
