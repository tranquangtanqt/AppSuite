using MeRon.Core.Models;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class ProductsPage : Page
{
    public ProductsViewModel ViewModel { get; } = new();

    public ProductsPage()
    {
        InitializeComponent();
        Shortcuts.Search(this, SearchBox);
        Shortcuts.Ctrl(this, Windows.System.VirtualKey.N, () => Add_Click(this, new RoutedEventArgs()));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Load();

    private async void Add_Click(object sender, RoutedEventArgs e) => await EditAsync(null);

    private async void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ProductRow row)
        {
            await EditAsync(row.Item);
        }
    }

    /// <summary>Form Danh mục / Tên / Giá bán / Mô tả như điện thoại. Không có Xoá (điện thoại cũng ẩn nút xoá món ăn).</summary>
    private async Task EditAsync(Product? product)
    {
        var options = ViewModel.CategoryOptions();
        var category = new ComboBox
        {
            Header = "Danh mục",
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(o => o.Id == product?.CategoryId) ?? options[0],
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(category, "Danh mục");
        var name = Dialogs.Text("Tên món ăn", product?.Name);
        var price = Dialogs.Money("Giá bán", product?.Price);
        var description = Dialogs.Text("Mô tả", product?.Description, multiline: true);
        name.Loaded += (_, _) => name.Focus(FocusState.Programmatic);

        var changed = await Dialogs.ShowFormAsync(XamlRoot, product is null ? "Thêm món ăn" : "Sửa món ăn",
            [category, name, price, description],
            () => ViewModel.Save(new Product(product?.Id ?? 0, (category.SelectedItem as CategoryOption)?.Id, name.Text,
                product?.ImageUrl, Dialogs.ValueOf(price), description.Text)));
        if (changed)
        {
            ViewModel.Load();
        }
    }
}
