using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Core.Models;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

public sealed record PurchaseCardLine(string Name, string Price);

/// <summary>Thẻ phiếu nhập (purchase_card của điện thoại): ngày, các khoản (tên, giá), Tổng.</summary>
public sealed class PurchaseCardViewModel(Purchase purchase)
{
    public long Id => purchase.Id;

    public string DateText => purchase.Day is { } d ? DbDate.Show(d) : purchase.Date ?? string.Empty;

    public IReadOnlyList<PurchaseCardLine> Lines { get; } =
        purchase.Items.Select(i => new PurchaseCardLine(i.Name ?? string.Empty, VnFormat.Money(i.Price))).ToList();

    public string TotalText => VnFormat.Money(purchase.Total);

    public string AccessibleName => $"Phiếu {purchase.Id} - {DateText} - {Lines.Count} khoản - {TotalText}";
}

/// <summary>Trang Mua hàng: phiếu nhập trong khoảng ngày (mặc định hôm nay), mới nhất trước.</summary>
public sealed partial class PurchasesViewModel : ObservableObject
{
    public PurchasesViewModel()
    {
        Range.Changed += (_, _) => Load();
    }

    public DateRangeFilter Range { get; } = new();

    public ObservableCollection<PurchaseCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    public bool IsEmpty => Cards.Count == 0;

    public void Load()
    {
        try
        {
            var purchases = AppServices.Purchases.Query(Range.From, Range.To);
            Cards.Clear();
            foreach (var p in purchases)
            {
                Cards.Add(new PurchaseCardViewModel(p));
            }
            Summary = purchases.Count == 0
                ? "Không có phiếu nhập nào trong khoảng ngày này."
                : $"{VnFormat.Number(purchases.Count)} phiếu  ·  Tổng {VnFormat.Money(purchases.Sum(p => p.Total))}";
            Error = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.For("Purchases").LogError(ex, "Tải phiếu nhập lỗi");
            Error = "Không đọc được phiếu nhập: " + ex.Message;
        }
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>1 khoản trên form phiếu nhập: Tên hàng + Giá tiền.</summary>
public sealed partial class PurchaseLineViewModel(PurchaseFormViewModel owner, string? name, long? price) : ObservableObject
{
    [ObservableProperty]
    private string _name = name ?? string.Empty;

    [ObservableProperty]
    private double _price = price is { } p ? p : double.NaN;

    partial void OnPriceChanged(double value) => owner.Recalculate();

    partial void OnNameChanged(string value) => owner.Error = string.Empty;

    [RelayCommand]
    private void Remove() => owner.RemoveLine(this);

    public PurchaseLineDraft ToDraft() => new(Name, double.IsNaN(Price) ? null : (long)Math.Round(Price));
}

/// <summary>Form phiếu nhập (purchase_form_screen của điện thoại): Ngày nhập, các khoản, Tổng tự cộng, Lưu, Xoá phiếu.</summary>
public sealed partial class PurchaseFormViewModel : ObservableObject
{
    private readonly PurchaseRepository _repository = AppServices.Purchases;
    private DateTime _originalDate;

    public long Id { get; private set; }

    public bool IsEdit => Id != 0;

    public string Title => IsEdit ? $"Sửa phiếu nhập #{Id}" : "Thêm phiếu nhập";

    public ObservableCollection<PurchaseLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    private DateTimeOffset? _date;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    /// <summary>Phiếu mới: ngày = <paramref name="initialDay"/> (khi trang Mua hàng đang lọc đúng 1 ngày) hoặc hôm nay, sẵn 1 khoản trống.</summary>
    public void Load(long? id, DateOnly? initialDay)
    {
        Lines.Clear();
        if (id is { } pid && _repository.Get(pid) is { } purchase)
        {
            Id = purchase.Id;
            _originalDate = purchase.Day ?? DateTime.Now;
            foreach (var item in purchase.Items)
            {
                Lines.Add(new PurchaseLineViewModel(this, item.Name, item.Price));
            }
        }
        else
        {
            Id = 0;
            _originalDate = initialDay is { } day ? day.ToDateTime(TimeOnly.MinValue) : DateTime.Now;
            Lines.Add(new PurchaseLineViewModel(this, null, null));
        }
        Date = new DateTimeOffset(_originalDate.Date);
        OnPropertyChanged(string.Empty);
        Recalculate();
    }

    [RelayCommand]
    private void AddLine()
    {
        Lines.Add(new PurchaseLineViewModel(this, null, null));
        Recalculate();
    }

    public void RemoveLine(PurchaseLineViewModel line)
    {
        Lines.Remove(line);
        Recalculate();
    }

    /// <summary>Tính lại Tổng; người dùng vừa sửa form nên lỗi cũ (nếu có) không còn đúng - xoá đi.</summary>
    public void Recalculate()
    {
        TotalText = "Tổng: " + VnFormat.Money(PurchaseRepository.Total(Lines.Select(l => l.ToDraft())));
        Error = string.Empty;
    }

    private PurchaseDraft BuildDraft()
    {
        var date = Date is { } d && d.Date != _originalDate.Date ? d.Date : _originalDate;
        return new PurchaseDraft(Id, date, Lines.Select(l => l.ToDraft()).ToList());
    }

    public PurchaseValidationException? Validate()
    {
        try
        {
            PurchaseRepository.Validate(BuildDraft());
            Error = string.Empty;
            return null;
        }
        catch (PurchaseValidationException ex)
        {
            Error = ex.Message;
            return ex;
        }
    }

    public bool Save()
    {
        var error = AppServices.Try("Lưu phiếu nhập", () =>
        {
            var id = _repository.Save(BuildDraft());
            AppLog.For("Purchases").LogInformation("{Action} phiếu nhập {Id}", IsEdit ? "Sửa" : "Thêm", id);
        });
        Error = error ?? string.Empty;
        return error is null;
    }

    public bool Delete()
    {
        var error = AppServices.Try("Xoá phiếu nhập", () => _repository.Delete(Id));
        Error = error ?? string.Empty;
        return error is null;
    }
}
