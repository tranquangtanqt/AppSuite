using MeRon.Core.Models;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class CategoriesPage : Page
{
    public CategoriesViewModel ViewModel { get; } = new();

    public CategoriesPage()
    {
        InitializeComponent();
        Shortcuts.Search(this, SearchBox);
        Shortcuts.Ctrl(this, Windows.System.VirtualKey.N, () => Add_Click(this, new RoutedEventArgs()));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Load();

    private async void Add_Click(object sender, RoutedEventArgs e) => await EditAsync(null);

    private async void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CategoryRow row)
        {
            await EditAsync(row.Item);
        }
    }

    private async void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CategoryRow row })
        {
            await EditAsync(row.Item);
        }
    }

    private async void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CategoryRow row }
            || !await Dialogs.ConfirmAsync(XamlRoot, "Xoá danh mục?", $"Xoá danh mục \"{row.Item.Name}\"? Không hoàn tác được.", "Xoá"))
        {
            return;
        }
        if (ViewModel.Delete(row.Item.Id) is { } error)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Không xoá được", error);
            return;
        }
        ViewModel.Load();
    }

    private async Task EditAsync(Category? category)
    {
        var name = Dialogs.Text("Tên", category?.Name);
        var description = Dialogs.Text("Mô tả", category?.Description, multiline: true);
        name.Loaded += (_, _) => name.Focus(FocusState.Programmatic);

        var changed = await Dialogs.ShowFormAsync(XamlRoot, category is null ? "Thêm danh mục" : "Sửa danh mục",
            [name, description],
            () => ViewModel.Save(new Category(category?.Id ?? 0, name.Text, description.Text)));
        if (changed)
        {
            ViewModel.Load();
        }
    }
}
