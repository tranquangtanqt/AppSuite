using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>
/// 1 dòng món trên form. Dòng thường: chọn món + số lượng (− / ô số / +); món tự do: tên + đơn giá + số lượng. Dòng bị
/// khoá (giá món đã đổi so với lúc đặt) thì không sửa / xoá được.
/// </summary>
public sealed partial class OrderLineViewModel : ObservableObject
{
    private readonly OrderFormViewModel _owner;

    public OrderLineViewModel(OrderFormViewModel owner, OrderLineDraft line)
    {
        _owner = owner;
        IsFree = line.IsFree;
        OriginalProductId = line.OriginalProductId;
        SnapshotPrice = line.SnapshotPrice;
        _selectedProduct = owner.Products.FirstOrDefault(p => p.Id == line.ProductId);
        _quantity = line.Quantity;
        _freeName = line.FreeName ?? string.Empty;
        _freePrice = line.FreePrice is { } price ? price : double.NaN;
        IsLocked = OrderCalculator.IsLocked(ToDraft(), owner.ProductMap);
    }

    public IReadOnlyList<ProductOption> Products => _owner.Products;

    public bool IsFree { get; }

    public bool IsProduct => !IsFree;

    public long? OriginalProductId { get; }

    public long? SnapshotPrice { get; }

    /// <summary>Cố định từ lúc mở form: dòng khoá thì món không đổi được nên trạng thái khoá không đổi.</summary>
    public bool IsLocked { get; }

    public bool IsEditable => !IsLocked;

    public double RowOpacity => IsLocked ? 0.55 : 1.0;

    /// <summary>null (không phải chuỗi rỗng) khi dòng không khoá: chuỗi rỗng vẫn hiện 1 tooltip trống khi rê chuột.</summary>
    public string? LockTip => IsLocked && SnapshotPrice is { } snap
        ? $"Giá món đã đổi ({VnFormat.Money(snap)} lúc đặt → {VnFormat.Money(SelectedProduct?.Price)} hiện tại): dòng bị khoá để giữ đơn giá cũ. Muốn thay đổi phải xoá đơn và tạo lại."
        : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitPriceText), nameof(LineTotalText))]
    private ProductOption? _selectedProduct;

    /// <summary>NumberBox dùng double; luôn ≥ 1 (nhập &lt; 1 hoặc bỏ trống → về 1, như hộp thoại "Nhập số lượng" của điện thoại).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotalText))]
    private double _quantity;

    [ObservableProperty]
    private string _freeName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitPriceText), nameof(LineTotalText))]
    private double _freePrice;

    partial void OnSelectedProductChanged(ProductOption? value) => _owner.Recalculate();

    /// <summary>Ô món còn chữ gõ không phải 1 món trong danh sách (bắt buộc chọn trong danh sách) → hiện đỏ dưới ô, không lưu được.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProductError))]
    private string _productError = string.Empty;

    public bool HasProductError => ProductError.Length > 0;

    /// <summary>Chọn 1 món trong danh sách (hoặc bỏ chọn khi ô trống) - xoá lỗi chữ gõ sai.</summary>
    public void ChooseProduct(ProductOption? product)
    {
        ProductError = string.Empty;
        SelectedProduct = product;
    }

    /// <summary>Chữ trong ô không khớp món nào: bỏ món đang chọn để không lưu nhầm món cũ.</summary>
    public void RejectProductText(string text)
    {
        SelectedProduct = null;
        ProductError = $"Không có món \"{text.Trim()}\" trong danh sách - chọn 1 món trong danh sách.";
    }

    // Số đang gõ trong ô (NumberBox chỉ ghi Value khi rời ô / Enter) - để Thành tiền / Tổng đổi ngay lúc gõ. null = dùng giá trị đã ghi.
    private double? _typingQuantity;
    private double? _typingFreePrice;

    /// <summary>Ô Số lượng đang gõ: số hợp lệ (≥ 1) thì tính tạm theo số đó; null / không hợp lệ thì về giá trị đã ghi.</summary>
    public void PreviewQuantity(double? value)
    {
        _typingQuantity = value is { } q && q >= 1 ? Math.Round(q) : null;
        OnPropertyChanged(nameof(LineTotalText));
        _owner.Recalculate();
    }

    /// <summary>Ô Đơn giá món tự do đang gõ (như <see cref="PreviewQuantity"/>).</summary>
    public void PreviewFreePrice(double? value)
    {
        _typingFreePrice = value is { } p && p >= 0 ? Math.Round(p) : null;
        OnPropertyChanged(nameof(UnitPriceText));
        OnPropertyChanged(nameof(LineTotalText));
        _owner.Recalculate();
    }

    partial void OnQuantityChanged(double value)
    {
        _typingQuantity = null;
        if (double.IsNaN(value) || value < 1)
        {
            Quantity = 1;
            return;
        }
        if (value != Math.Round(value))
        {
            Quantity = Math.Round(value);
            return;
        }
        _owner.Recalculate();
    }

    partial void OnFreePriceChanged(double value)
    {
        _typingFreePrice = null;
        _owner.Recalculate();
    }

    partial void OnFreeNameChanged(string value) => _owner.Error = string.Empty;

    public string UnitPriceText => VnFormat.Money(OrderCalculator.UnitPrice(ToDraft(), _owner.ProductMap));

    public string LineTotalText => VnFormat.Money(OrderCalculator.LineTotal(ToDraft(), _owner.ProductMap));

    [RelayCommand]
    private void Increase() => Quantity += 1;

    [RelayCommand]
    private void Decrease()
    {
        if (Quantity > 1)
        {
            Quantity -= 1;
        }
    }

    [RelayCommand]
    private void Remove() => _owner.RemoveLine(this);

    public OrderLineDraft ToDraft() => new(
        IsFree ? null : SelectedProduct?.Id,
        _typingQuantity is { } typed ? (long)typed : double.IsNaN(Quantity) ? 1 : (long)Quantity,
        IsFree,
        IsFree ? FreeName : null,
        IsFree ? _typingFreePrice is { } typedPrice ? (long)typedPrice : double.IsNaN(FreePrice) ? null : (long)Math.Round(FreePrice) : null,
        OriginalProductId,
        SnapshotPrice);
}

/// <summary>
/// Form thêm / sửa đơn - giống order_form_screen của điện thoại: khách hàng, các dòng món (thường / tự do), Giảm giá,
/// Tổng tự tính, tổng số lượng, Ngày giao hàng, "Đã thanh toán trước" + Ngày thanh toán, Ghi chú; Lưu (cảnh báo trùng đơn
/// do View hỏi), Huỷ đơn, Xoá đơn.
/// </summary>
public sealed partial class OrderFormViewModel : ObservableObject
{
    private readonly OrderRepository _orders = AppServices.Orders;
    private DateTime _originalDelivery;
    private DateTime? _originalPayment;

    public IReadOnlyList<CustomerOption> Customers { get; private set; } = [];

    public IReadOnlyList<ProductOption> Products { get; private set; } = [];

    public IReadOnlyDictionary<long, Product> ProductMap { get; private set; } = new Dictionary<long, Product>();

    public ObservableCollection<OrderLineViewModel> Lines { get; } = [];

    public long Id { get; private set; }

    public bool IsEdit => Id != 0;

    /// <summary>Form thêm đơn: hiện nút Lưu và thêm mới / Lưu và chép cho khách khác.</summary>
    public bool IsNew => !IsEdit;

    public OrderStatus OriginalStatus { get; private set; } = OrderStatus.Shipping;

    /// <summary>Đơn mới đang chép từ đơn này (Sao chép đơn); null = không phải bản sao.</summary>
    public long? CopiedFromId { get; private set; }

    public string Title => IsEdit ? $"Chỉnh sửa đặt hàng #{Id}"
        : CopiedFromId is { } from ? $"Thêm đặt hàng (sao chép từ đơn #{from})" : "Thêm đặt hàng";

    /// <summary>Báo khi sao chép: giá món đã đổi, món không còn trong danh sách.</summary>
    public string CopyNotice { get; private set; } = string.Empty;

    public bool HasCopyNotice => CopyNotice.Length > 0;

    public string StatusText => IsEdit ? OrderStatusText.Label(OriginalStatus) : string.Empty;

    public bool IsCancelled => IsEdit && OriginalStatus == OrderStatus.Cancelled;

    /// <summary>Huỷ đơn chỉ hiện khi đơn chưa ở trạng thái Huỷ (như điện thoại).</summary>
    public bool CanCancel => IsEdit && OriginalStatus != OrderStatus.Cancelled;

    [ObservableProperty]
    private CustomerOption? _customer;

    partial void OnCustomerChanged(CustomerOption? value) => Error = string.Empty;

    [ObservableProperty]
    private double _discount;

    [ObservableProperty]
    private DateTimeOffset? _deliveryDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaymentVisible))]
    private bool _isPrepaid;

    [ObservableProperty]
    private DateTimeOffset? _paymentDate;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string _subTotalText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _quantityText = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    public bool PaymentVisible => IsPrepaid;

    private double? _typingDiscount;

    /// <summary>Ô Giảm giá đang gõ (NumberBox chỉ ghi Value khi rời ô / Enter) → Tổng đổi ngay; null = dùng giá trị đã ghi.</summary>
    public void PreviewDiscount(double? value)
    {
        _typingDiscount = value is { } d && d >= 0 ? Math.Round(d) : null;
        Recalculate();
    }

    private long DiscountValue => _typingDiscount is { } typed ? (long)typed : double.IsNaN(Discount) ? 0 : (long)Math.Round(Discount);

    partial void OnDiscountChanged(double value)
    {
        _typingDiscount = null;
        if (double.IsNaN(value) || value < 0)
        {
            Discount = 0;
            return;
        }
        Recalculate();
    }

    /// <summary>Bật "Đã thanh toán trước" → Ngày thanh toán = Ngày giao (như điện thoại).</summary>
    partial void OnIsPrepaidChanged(bool value)
    {
        if (value && PaymentDate is null)
        {
            PaymentDate = DeliveryDate;
        }
    }

    public void Load(OrderFormArgs args)
    {
        _typingDiscount = null;
        Customers = CustomerOption.From(AppServices.MasterData.GetCustomers());
        var products = AppServices.MasterData.GetProducts();
        ProductMap = products.ToDictionary(p => p.Id);
        Products = products.Select(p => new ProductOption(p.Id, p.Name ?? $"#{p.Id}", p.Price ?? 0)).ToList();
        Lines.Clear();
        CopiedFromId = null;
        CopyNotice = string.Empty;

        if (args.CopyFromId is { } copyId && _orders.Get(copyId) is { } source)
        {
            var copy = OrderCopier.CopyAsNew(source, DateOnly.FromDateTime(DateTime.Today), ProductMap, args.InitialDelivery);
            var draft = copy.Draft;
            Id = 0;
            CopiedFromId = copyId;
            OriginalStatus = OrderStatus.Shipping;
            // Lưu và chép cho khách khác: bỏ trống khách để nhập khách mới, giữ cả Đã thanh toán trước của đơn vừa lưu.
            Customer = args.ForOtherCustomer ? null : Customers.FirstOrDefault(c => c.Id == draft.UserId);
            _originalDelivery = draft.Delivery;
            DeliveryDate = new DateTimeOffset(_originalDelivery.Date);
            IsPrepaid = args.ForOtherCustomer && source.Status == OrderStatus.Completed;
            PaymentDate = IsPrepaid ? DeliveryDate : null;
            _originalPayment = null;
            Discount = draft.Discount;
            Note = draft.Note ?? string.Empty;
            foreach (var line in draft.Lines)
            {
                Lines.Add(new OrderLineViewModel(this, line));
            }
            CopyNotice = copy.Notices.Count == 0 ? string.Empty : "Khác đơn gốc: " + string.Join("; ", copy.Notices) + ".";
        }
        else if (args.OrderId is not { } id || _orders.Get(id) is not { } order)
        {
            Id = 0;
            OriginalStatus = OrderStatus.Shipping;
            Customer = Customers.FirstOrDefault(c => c.Id == args.InitialUserId);
            _originalDelivery = args.InitialDelivery is { } day ? day.ToDateTime(TimeOnly.MinValue) : DateTime.Now;
            DeliveryDate = new DateTimeOffset(_originalDelivery.Date);
            IsPrepaid = false;
            PaymentDate = null;
            _originalPayment = null;
            Discount = 0;
            Note = string.Empty;
        }
        else
        {
            Id = order.Id;
            OriginalStatus = order.Status;
            Customer = Customers.FirstOrDefault(c => c.Id == order.UserId);
            _originalDelivery = order.Delivery ?? DateTime.Now;
            DeliveryDate = new DateTimeOffset(_originalDelivery.Date);
            IsPrepaid = order.Status == OrderStatus.Completed;
            _originalPayment = order.Payment;
            PaymentDate = order.Payment is { } p ? new DateTimeOffset(p.Date) : IsPrepaid ? DeliveryDate : null;
            Discount = order.DiscountValue;
            Note = order.Note ?? string.Empty;
            // Món không còn trong danh mục (bị xoá) → hiện như món tự do, giống điện thoại.
            foreach (var item in order.Items)
            {
                var free = item.ProductId is null || !ProductMap.ContainsKey(item.ProductId.Value);
                Lines.Add(new OrderLineViewModel(this, free
                    ? new OrderLineDraft(null, item.Quantity, true, item.SnapshotName, item.SnapshotPrice)
                    : new OrderLineDraft(item.ProductId, item.Quantity, OriginalProductId: item.ProductId, SnapshotPrice: item.SnapshotPrice)));
            }
        }
        OnPropertyChanged(string.Empty);
        Recalculate();
        if ((args.OrderId ?? args.CopyFromId) is not null && Id == 0 && CopiedFromId is null)
        {
            Error = "Không tìm thấy đơn hàng (có thể vừa nhập lại backup) - đang mở form đơn mới.";
        }
    }

    /// <summary>Thêm món: dòng trống, View đưa con trỏ vào ô món để gõ lọc (điện thoại mặc định món đầu tiên - với ô
    /// autocomplete thì chọn sẵn 1 món dễ lưu nhầm).</summary>
    [RelayCommand]
    private void AddLine()
    {
        Lines.Add(new OrderLineViewModel(this, new OrderLineDraft(null, 1)));
        Recalculate();
    }

    [RelayCommand]
    private void AddFreeLine()
    {
        Lines.Add(new OrderLineViewModel(this, new OrderLineDraft(null, 1, true)));
        Recalculate();
    }

    public void RemoveLine(OrderLineViewModel line)
    {
        if (!line.IsLocked)
        {
            Lines.Remove(line);
            Recalculate();
        }
    }

    public void Recalculate()
    {
        var drafts = Lines.Select(l => l.ToDraft()).ToList();
        var subTotal = OrderCalculator.SubTotal(drafts, ProductMap);
        var discount = DiscountValue;
        SubTotalText = "Tạm tính: " + VnFormat.Money(subTotal);
        TotalText = "Tổng: " + VnFormat.Money(OrderCalculator.Total(subTotal, discount));
        QuantityText = $"Tổng số lượng: {VnFormat.Number(OrderCalculator.TotalQuantity(drafts))} món";
        Error = string.Empty; // người dùng vừa sửa form - lỗi cũ không còn đúng
    }

    /// <summary>Ngày chọn trên lịch; giữ nguyên giờ gốc nếu vẫn là ngày cũ (sửa đơn không làm đổi dữ liệu không liên quan).</summary>
    private static DateTime Pick(DateTimeOffset? picked, DateTime original) =>
        picked is { } p && p.Date != original.Date ? p.Date : original;

    public OrderDraft BuildDraft()
    {
        var delivery = Pick(DeliveryDate, _originalDelivery);
        DateTime? payment = PaymentDate is { } pd
            ? _originalPayment is { } op && op.Date == pd.Date ? op : pd.Date
            : null;
        return new OrderDraft(Id, Customer?.Id, OriginalStatus, delivery, IsPrepaid, payment,
            DiscountValue, Note, Lines.Select(l => l.ToDraft()).ToList());
    }

    /// <summary>Kiểm tra form; trả về lỗi (kèm dòng / ô cần đưa con trỏ tới) hoặc null.</summary>
    public OrderValidationException? Validate()
    {
        try
        {
            // Ô món còn chữ gõ không khớp món nào: báo đúng lỗi đó (core chỉ thấy "chưa chọn món").
            for (var i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].HasProductError)
                {
                    throw new OrderValidationException(Lines[i].ProductError, i, OrderLineField.Product);
                }
            }
            OrderCalculator.Validate(BuildDraft());
            Error = string.Empty;
            return null;
        }
        catch (OrderValidationException ex)
        {
            Error = ex.Message;
            return ex;
        }
    }

    /// <summary>Khách đã có đơn khác cùng ngày giao (không tính đơn huỷ / chính đơn này)?</summary>
    public bool HasDuplicate()
    {
        var draft = BuildDraft();
        return draft.UserId is { } userId && _orders.HasOtherOrderSameDay(userId, DateOnly.FromDateTime(draft.Delivery), Id);
    }

    /// <summary>Id của đơn vừa lưu (Lưu và chép cho khách khác chép từ đơn này).</summary>
    public long SavedId { get; private set; }

    public bool Save()
    {
        var error = AppServices.Try("Lưu đơn hàng", () =>
        {
            var id = _orders.Save(BuildDraft(), ProductMap);
            SavedId = id;
            AppLog.For("Orders").LogInformation("{Action} đơn {Id}", IsEdit ? "Sửa" : "Thêm", id);
        });
        Error = error ?? string.Empty;
        return error is null;
    }

    public bool CancelOrder()
    {
        var error = AppServices.Try("Huỷ đơn", () => _orders.SetStatus(Id, OrderStatus.Cancelled));
        Error = error ?? string.Empty;
        return error is null;
    }

    public bool DeleteOrder()
    {
        var error = AppServices.Try("Xoá đơn", () => _orders.Delete(Id));
        Error = error ?? string.Empty;
        return error is null;
    }
}
