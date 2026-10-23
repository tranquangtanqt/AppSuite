using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Reports;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>1 thẻ khách ở trang Công nợ.</summary>
public sealed record DebtRow(CustomerDebt Debt)
{
    public string Name => Debt.Name;

    public string Contact => string.Join("  ·  ", new[] { Debt.Phone, Debt.Address }.Where(s => s is not null));

    public bool HasContact => Contact.Length > 0;

    public bool HasPhone => Debt.Phone is not null;

    /// <summary>Đơn không gắn khách: không lọc riêng được ở màn Chi tiết (không có ảnh / tin nhắn), vẫn Thanh toán hết được.</summary>
    public bool HasCustomer => Debt.UserId is not null;

    /// <summary>Ô to bên trái: đơn cũ nhất bao nhiêu ngày.</summary>
    public string DaysText => VnFormat.Number(Debt.OldestDays);

    public bool IsOverdue => Debt.IsOverdue;

    public string AmountText => VnFormat.Money(Debt.Amount);

    public string OrdersText => Debt.OldestDay is not { } oldest
        ? $"{VnFormat.Number(Debt.OrderCount)} đơn chưa thanh toán"
        : Debt.OrderCount == 1 || Debt.NewestDay == oldest
            ? $"{VnFormat.Number(Debt.OrderCount)} đơn chưa thanh toán  ·  giao {oldest:dd/MM/yyyy}"
            : $"{VnFormat.Number(Debt.OrderCount)} đơn chưa thanh toán  ·  {oldest:dd/MM/yyyy} – {Debt.NewestDay:dd/MM/yyyy}";

    public string AccessibleName => $"{Name} - nợ {AmountText} - {OrdersText}";
}

/// <summary>
/// Công nợ: đơn Đã lên đơn (chưa thanh toán) gom theo khách - tổng nợ, đơn cũ nhất bao nhiêu ngày (xem
/// <see cref="CustomerDebts"/>). Bộ lọc giữ suốt phiên (trang được cache); mỗi lần vào trang thì đọc lại dữ liệu.
/// </summary>
public sealed partial class DebtsViewModel : ObservableObject
{
    private IReadOnlyList<CustomerDebt> _all = [];

    public ObservableCollection<DebtRow> Rows { get; } = [];

    [ObservableProperty]
    private DebtSort _sort = DebtSort.Amount;

    /// <summary>Tính cả đơn giao sau hôm nay (đặt trước) - mặc định không: chưa giao thì chưa phải nợ.</summary>
    [ObservableProperty]
    private bool _includeFuture;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public static string SortLabel(DebtSort sort) => sort == DebtSort.Oldest ? "Nợ lâu nhất trước" : "Nợ nhiều nhất trước";

    partial void OnSortChanged(DebtSort value) => Apply();

    partial void OnSearchTextChanged(string value) => Apply();

    partial void OnIncludeFutureChanged(bool value) => Load();

    public void Load()
    {
        try
        {
            _all = AppServices.Reports.CustomerDebts(Today, IncludeFuture);
            Error = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.For("Reports").LogError(ex, "Tải công nợ lỗi");
            Error = "Không đọc được dữ liệu: " + ex.Message;
            _all = [];
        }
        Apply();
    }

    /// <summary>Bộ lọc màn Chi tiết đúng các đơn đang tính nợ của khách (cùng điều kiện ngày với <see cref="Load"/>).</summary>
    public OrderFilter DetailFilter(CustomerDebt debt) =>
        new(OrderStatus.Shipping, null, IncludeFuture ? null : Today, debt.UserId);

    /// <summary>Tiêu đề trên ảnh hoá đơn.</summary>
    public static string CaptureTitle(CustomerDebt debt) =>
        $"Đơn chưa thanh toán - {debt.Name}  ·  tính đến {DateTime.Today:dd/MM/yyyy}  ·  Tổng {VnFormat.Money(debt.Amount)}";

    /// <summary>Thanh toán hết các đơn đang nợ của 1 khách; trả lỗi hoặc null.</summary>
    public string? PayAll(CustomerDebt debt)
    {
        var count = 0;
        var error = AppServices.Try("Thanh toán hết", () => count = AppServices.Orders.PayShipping(debt.OrderIds));
        if (error is null)
        {
            AppLog.For("Orders").LogInformation("Công nợ: thanh toán hết {Count} đơn của khách {User}", count, debt.UserId);
            Load();
            Message = $"Đã chuyển {VnFormat.Number(count)} đơn của {debt.Name} sang Đã thanh toán ({VnFormat.Money(debt.Amount)}).";
        }
        return error;
    }

    private void Apply()
    {
        var shown = CustomerDebts.Sort(
            _all.Where(d => TextSearch.Matches(d.Name, SearchText) || TextSearch.Matches(d.Phone, SearchText)), Sort);
        Rows.Clear();
        foreach (var d in shown)
        {
            Rows.Add(new DebtRow(d));
        }
        var total = shown.Sum(d => d.Amount);
        var orders = shown.Sum(d => d.OrderCount);
        var overdue = shown.Count(d => d.IsOverdue);
        Summary = _all.Count == 0
            ? "Không có đơn nào chưa thanh toán."
            : (shown.Count == _all.Count ? $"{VnFormat.Number(_all.Count)} khách" : $"{VnFormat.Number(shown.Count)} / {VnFormat.Number(_all.Count)} khách") +
              $"  ·  {VnFormat.Number(orders)} đơn  ·  Tổng nợ {VnFormat.Money(total)}" +
              (overdue > 0 ? $"  ·  {VnFormat.Number(overdue)} khách nợ từ {CustomerDebts.OverdueDays} ngày" : "");
        HasData = shown.Count > 0;
    }
}
