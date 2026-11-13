using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Reports;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>1 ô nhóm tuổi nợ ở đầu trang Công nợ (bấm = lọc khách có nợ trong nhóm).</summary>
public sealed record AgingTile(AgingBucket Bucket, bool IsSelected)
{
    public string Label => Bucket.Label;

    public string AmountText => VnFormat.Money(Bucket.Amount);

    public string CountText => $"{VnFormat.Number(Bucket.OrderCount)} đơn · {VnFormat.Number(Bucket.CustomerCount)} khách";

    public bool IsOldest => Bucket.IsOldest && Bucket.OrderCount > 0;

    public string AccessibleName => $"Nợ {Label}: {AmountText}, {CountText}" + (IsSelected ? " - đang lọc" : string.Empty);
}

/// <summary>1 thẻ khách ở trang Công nợ. <paramref name="IsOverdue"/>: đơn cũ nhất quá mốc tuổi nợ lớn nhất (ô ngày tô cam).</summary>
public sealed record DebtRow(CustomerDebt Debt, bool IsOverdue)
{
    public string Name => Debt.Name;

    public string Contact => string.Join("  ·  ", new[] { Debt.Phone, Debt.Address }.Where(s => s is not null));

    public bool HasContact => Contact.Length > 0;

    public bool HasPhone => Debt.Phone is not null;

    /// <summary>Đơn không gắn khách: không lọc riêng được ở màn Chi tiết (không có ảnh / tin nhắn), vẫn Thanh toán hết được.</summary>
    public bool HasCustomer => Debt.UserId is not null;

    /// <summary>Ô to bên trái: đơn cũ nhất bao nhiêu ngày.</summary>
    public string DaysText => VnFormat.Number(Debt.OldestDays);

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

    /// <summary>Mốc tuổi nợ đang dùng (trang đọc / lưu settings.json).</summary>
    public IReadOnlyList<int> Marks { get; private set; } = DebtAging.DefaultMarks;

    [ObservableProperty]
    private string _marksText = DebtAging.Format(DebtAging.DefaultMarks);

    [ObservableProperty]
    private string _marksError = string.Empty;

    public ObservableCollection<AgingTile> Tiles { get; } = [];

    [ObservableProperty]
    private bool _hasTiles;

    /// <summary>Ô nhóm đang lọc (null = không lọc).</summary>
    private int? _bucketFilter;

    [ObservableProperty]
    private string _note = string.Empty;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public static string SortLabel(DebtSort sort) => sort == DebtSort.Oldest ? "Nợ lâu nhất trước" : "Nợ nhiều nhất trước";

    partial void OnSortChanged(DebtSort value) => Apply();

    partial void OnSearchTextChanged(string value) => Apply();

    partial void OnIncludeFutureChanged(bool value) => Load();

    /// <summary>Đặt mốc (đọc từ settings.json lúc mở trang) - không lưu.</summary>
    public void UseMarks(IReadOnlyList<int> marks)
    {
        Marks = marks;
        MarksText = DebtAging.Format(marks);
        MarksError = string.Empty;
        _bucketFilter = null;
    }

    /// <summary>Ô Mốc ngày: đọc chữ người dùng gõ. Đúng → dùng mốc mới, trả mốc để trang lưu; sai → báo đỏ, giữ mốc cũ, trả null.</summary>
    public IReadOnlyList<int>? CommitMarks()
    {
        var (marks, error) = DebtAging.Parse(MarksText);
        if (marks is null)
        {
            MarksError = error ?? string.Empty;
            return null;
        }
        var changed = !marks.SequenceEqual(Marks);
        UseMarks(marks);
        if (changed)
        {
            Apply();
        }
        return changed ? marks : null;
    }

    /// <summary>Bấm 1 ô nhóm: lọc khách có nợ trong nhóm đó; bấm lại ô đang lọc thì bỏ lọc.</summary>
    public void ToggleBucket(AgingBucket bucket)
    {
        _bucketFilter = _bucketFilter == bucket.Index ? null : bucket.Index;
        Apply();
    }

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

    /// <summary>Tiêu đề trên ảnh hoá đơn (mở từ Công nợ nên không cần ghi "Đơn chưa thanh toán" / ngày).</summary>
    public static string CaptureTitle(CustomerDebt debt) =>
        $"{debt.Name}  ·  Tổng {VnFormat.Money(debt.Amount)}";

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
        var searched = _all.Where(d => TextSearch.Matches(d.Name, SearchText) || TextSearch.Matches(d.Phone, SearchText)).ToList();
        // Ô nhóm tuổi nợ tính theo danh sách đã tìm (chưa lọc nhóm); nhóm đang lọc không còn (vd bỏ tích đơn đặt trước) → bỏ lọc.
        var buckets = DebtAging.Summarize(searched, Marks, Today);
        var bucket = buckets.FirstOrDefault(b => b.Index == _bucketFilter);
        _bucketFilter = bucket?.Index;
        Tiles.Clear();
        foreach (var b in buckets)
        {
            Tiles.Add(new AgingTile(b, b.Index == _bucketFilter));
        }
        HasTiles = searched.Count > 0;
        Note = $"Đơn \"Đã lên đơn\" (chưa thanh toán) đã tới ngày giao, gộp theo khách. Ô bên trái = đơn cũ nhất đã giao bao nhiêu ngày " +
               $"(cam khi trên {Marks[^1]} ngày). Bấm 1 ô nhóm tuổi nợ ở trên để chỉ xem khách có nợ trong nhóm đó. Chi tiết: các đơn đang nợ " +
               "của khách - bấm Chụp ảnh ở đó để gửi Zalo (Ctrl+V).";

        var shown = CustomerDebts.Sort(bucket is null ? searched : searched.Where(d => DebtAging.HasOrderIn(d, bucket, Today)), Sort);
        Rows.Clear();
        foreach (var d in shown)
        {
            Rows.Add(new DebtRow(d, DebtAging.IsOverdue(d, Marks)));
        }
        var total = shown.Sum(d => d.Amount);
        var orders = shown.Sum(d => d.OrderCount);
        var overdue = shown.Count(d => DebtAging.IsOverdue(d, Marks));
        Summary = _all.Count == 0
            ? "Không có đơn nào chưa thanh toán."
            : (shown.Count == _all.Count ? $"{VnFormat.Number(_all.Count)} khách" : $"{VnFormat.Number(shown.Count)} / {VnFormat.Number(_all.Count)} khách") +
              $"  ·  {VnFormat.Number(orders)} đơn  ·  Tổng nợ {VnFormat.Money(total)}" +
              (overdue > 0 ? $"  ·  {VnFormat.Number(overdue)} khách nợ trên {Marks[^1]} ngày" : "") +
              (bucket is null ? "" : $"  ·  đang lọc: có nợ {bucket.Label}");
        HasData = shown.Count > 0;
    }
}
