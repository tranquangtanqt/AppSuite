using MeRon.Core.Models;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class CustomersPage : Page
{
    public CustomersViewModel ViewModel { get; } = new();

    public CustomersPage()
    {
        InitializeComponent();
        Shortcuts.Search(this, SearchBox);
        Shortcuts.Ctrl(this, Windows.System.VirtualKey.N, () => Add_Click(this, new RoutedEventArgs()));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Load();

    private async void Add_Click(object sender, RoutedEventArgs e) => await EditAsync(null);

    private async void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CustomerRow row)
        {
            await EditAsync(row.Item);
        }
    }

    /// <summary>Form Tên / Số điện thoại / Địa chỉ / Ghi chú như điện thoại; xoá chỉ có trong form sửa.</summary>
    private async Task EditAsync(Customer? customer)
    {
        var name = Dialogs.Text("Tên", customer?.Name);
        var phone = Dialogs.Text("Số điện thoại", customer?.Phone);
        // Ô gõ tự do (như điện thoại) + danh sách chọn nhanh các địa chỉ trong danh mục.
        var address = new ComboBox
        {
            Header = "Địa chỉ",
            IsEditable = true,
            ItemsSource = ViewModel.AddressSuggestions(),
            Text = customer?.Address ?? string.Empty,
            PlaceholderText = "Gõ hoặc chọn",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(address, "Địa chỉ");
        var note = Dialogs.Text("Ghi chú", customer?.Note, multiline: true);
        name.Loaded += (_, _) => name.Focus(FocusState.Programmatic);

        var id = customer?.Id ?? 0;
        var changed = await Dialogs.ShowFormAsync(XamlRoot, customer is null ? "Thêm khách hàng" : "Sửa khách hàng",
            [name, phone, address, note],
            () => ViewModel.Save(new Customer(id, name.Text, address.Text, phone.Text, note.Text)),
            customer is null ? null : () => ViewModel.Delete(id),
            $"Xoá khách hàng \"{customer?.Name}\"? Không hoàn tác được.");
        if (changed)
        {
            ViewModel.Load();
        }
    }
}
