using MeRon.Core.Models;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class AddressesPage : Page
{
    public AddressesViewModel ViewModel { get; } = new();

    public AddressesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Load();

    private async void Add_Click(object sender, RoutedEventArgs e) => await EditAsync(null);

    private async void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AddressRow row)
        {
            await EditAsync(row.Item);
        }
    }

    /// <summary>Form Mã / Tên. Mã là khoá nên khi sửa chỉ đổi được Tên; Xoá nằm trong form sửa (như điện thoại).</summary>
    private async Task EditAsync(Address? address)
    {
        var code = Dialogs.Text("Mã", address?.Code, readOnly: address is not null);
        var name = Dialogs.Text("Tên", address?.Name);
        (address is null ? code : name).Loaded += (sender, _) => ((Control)sender).Focus(FocusState.Programmatic);

        var changed = await Dialogs.ShowFormAsync(XamlRoot, address is null ? "Thêm địa chỉ" : "Sửa địa chỉ",
            [code, name],
            () => address is null ? ViewModel.Add(new Address(code.Text, name.Text)) : ViewModel.Update(address with { Name = name.Text }),
            address is null ? null : () => ViewModel.Delete(address.Code),
            $"Xoá địa chỉ \"{address?.Code} - {address?.Name}\"? Khách hàng đã ghi địa chỉ này vẫn giữ nguyên chữ đã ghi.");
        if (changed)
        {
            ViewModel.Load();
        }
    }
}
