using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Core.Models;
using MeRon.Services;

namespace MeRon.ViewModels;

/// <summary>
/// Khung chung của 4 trang danh mục: tải toàn bộ 1 lần (vài trăm dòng), lọc ngay khi gõ (không phân biệt hoa thường /
/// dấu), đánh số thứ tự theo danh sách đang hiện.
/// </summary>
public abstract partial class ListViewModel<TItem, TRow> : ObservableObject
{
    private IReadOnlyList<TItem> _all = [];

    protected MasterDataRepository Repository { get; } = AppServices.MasterData;

    public ObservableCollection<TRow> Items { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private string _loadError = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public void Load()
    {
        try
        {
            _all = Fetch();
            LoadError = string.Empty;
        }
        catch (Exception ex)
        {
            _all = [];
            LoadError = "Không đọc được dữ liệu: " + ex.Message;
        }
        ApplyFilter();
    }

    protected abstract IReadOnlyList<TItem> Fetch();

    protected abstract bool Matches(TItem item, string query);

    protected abstract TRow MakeRow(int index, TItem item);

    private void ApplyFilter()
    {
        Items.Clear();
        var index = 0;
        foreach (var item in _all)
        {
            if (string.IsNullOrWhiteSpace(SearchText) || Matches(item, SearchText))
            {
                Items.Add(MakeRow(++index, item));
            }
        }
        CountText = Items.Count == _all.Count
            ? $"{VnFormat.Number(_all.Count)} mục"
            : $"{VnFormat.Number(Items.Count)} / {VnFormat.Number(_all.Count)} mục";
    }
}

// ---- Khách hàng ----

public sealed record CustomerRow(int Index, Customer Item);

public sealed class CustomersViewModel : ListViewModel<Customer, CustomerRow>
{
    protected override IReadOnlyList<Customer> Fetch() => Repository.GetCustomers();

    /// <summary>Tìm theo tên (như điện thoại) và cả số điện thoại.</summary>
    protected override bool Matches(Customer item, string query) =>
        TextSearch.Matches(item.Name, query) || TextSearch.Matches(item.Phone, query);

    protected override CustomerRow MakeRow(int index, Customer item) => new(index, item);

    /// <summary>Gợi ý cho ô Địa chỉ: tên các địa chỉ trong danh mục (ô vẫn cho gõ tự do như điện thoại).</summary>
    public IReadOnlyList<string> AddressSuggestions() =>
        Repository.GetAddresses().Select(a => a.Name ?? a.Code).Distinct().ToList();

    public string? Save(Customer customer) => AppServices.Try("Lưu khách hàng", () => Repository.SaveCustomer(customer));

    public string? Delete(long id) => AppServices.Try("Xoá khách hàng", () => Repository.DeleteCustomer(id));
}

// ---- Món ăn ----

public sealed record ProductRow(int Index, Product Item, string CategoryName);

/// <summary>Lựa chọn trong ô Danh mục (Id null = không chọn).</summary>
public sealed record CategoryOption(long? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class ProductsViewModel : ListViewModel<Product, ProductRow>
{
    private Dictionary<long, string> _categoryNames = [];

    protected override IReadOnlyList<Product> Fetch()
    {
        _categoryNames = Repository.GetCategories().ToDictionary(c => c.Id, c => c.Name ?? string.Empty);
        return Repository.GetProducts();
    }

    protected override bool Matches(Product item, string query) => TextSearch.Matches(item.Name, query);

    protected override ProductRow MakeRow(int index, Product item) =>
        new(index, item, item.CategoryId is { } id && _categoryNames.TryGetValue(id, out var name) ? name : string.Empty);

    public IReadOnlyList<CategoryOption> CategoryOptions() =>
        [new(null, "(không chọn)"), .. Repository.GetCategories().Select(c => new CategoryOption(c.Id, c.Name ?? $"#{c.Id}"))];

    public string? Save(Product product) => AppServices.Try("Lưu món ăn", () => Repository.SaveProduct(product));
}

// ---- Danh mục món ăn ----

public sealed record CategoryRow(int Index, Category Item);

public sealed class CategoriesViewModel : ListViewModel<Category, CategoryRow>
{
    protected override IReadOnlyList<Category> Fetch() => Repository.GetCategories();

    protected override bool Matches(Category item, string query) =>
        TextSearch.Matches(item.Name, query) || TextSearch.Matches(item.Description, query);

    protected override CategoryRow MakeRow(int index, Category item) => new(index, item);

    public string? Save(Category category) => AppServices.Try("Lưu danh mục", () => Repository.SaveCategory(category));

    public string? Delete(long id) => AppServices.Try("Xoá danh mục", () => Repository.DeleteCategory(id));
}

// ---- Địa chỉ ----

public sealed record AddressRow(int Index, Address Item);

public sealed class AddressesViewModel : ListViewModel<Address, AddressRow>
{
    protected override IReadOnlyList<Address> Fetch() => Repository.GetAddresses();

    protected override bool Matches(Address item, string query) =>
        TextSearch.Matches(item.Code, query) || TextSearch.Matches(item.Name, query);

    protected override AddressRow MakeRow(int index, Address item) => new(index, item);

    public string? Add(Address address) => AppServices.Try("Thêm địa chỉ", () => Repository.AddAddress(address));

    public string? Update(Address address) => AppServices.Try("Lưu địa chỉ", () => Repository.UpdateAddress(address));

    public string? Delete(string code) => AppServices.Try("Xoá địa chỉ", () => Repository.DeleteAddress(code));
}
