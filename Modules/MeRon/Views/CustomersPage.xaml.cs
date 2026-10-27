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

    private async Task EditAsync(Customer? customer)
    {
        if (await CustomerForm.ShowAsync(XamlRoot, customer) is not null)
        {
            ViewModel.Load();
        }
    }
}
