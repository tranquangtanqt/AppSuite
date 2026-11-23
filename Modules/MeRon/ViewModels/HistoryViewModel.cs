using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.History;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>1 dòng ở trang Lịch sử chỉnh sửa.</summary>
public sealed class HistoryRow(HistoryEntry entry, int batchCount)
{
    public HistoryEntry Entry { get; } = entry;

    public HistoryKind Kind => Entry.Kind;

    public string TimeText => Entry.At.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public string Action => Entry.Action;

    public string CustomerText => string.IsNullOrWhiteSpace(Entry.CustomerName) ? "(không có khách)" : Entry.CustomerName.Trim();

    public string OrderText => $"Đơn {Entry.OrderId}" + (Entry.Delivery is { } d ? $" · giao {DbDate.Show(d)}" : string.Empty);

    /// <summary>Tính 1 lần (thẻ bị dựng lại nhiều lần khi cuộn).</summary>
    public string ChangesText { get; } = string.Join("\n", HistoryText.Changes(entry.Before, entry.After));

    /// <summary>Thêm đơn mới thì không có "trước" để khôi phục.</summary>
    public bool CanRestore => Entry.Before is not null;

    public int BatchCount { get; } = batchCount;

    public bool HasBatch => BatchCount > 1 && CanRestore;

    public string BatchText => $"Khôi phục cả lô ({BatchCount} đơn)";

    public string AccessibleName => $"{TimeText} {Action} {CustomerText} {OrderText}";
}

/// <summary>
/// Trang Lịch sử chỉnh sửa: các lần thay đổi đơn trên máy tính (<see cref="OrderHistory"/>), lọc theo ngày sửa, loại
/// (xoá / sửa / thêm), tên khách / mã đơn; khôi phục từng đơn hoặc cả lô (trang làm hộp thoại, gọi <see cref="Restore"/>).
/// Hiện <see cref="PageSize"/> dòng mỗi trang (lịch sử vài nghìn dòng dựng hết thẻ thì cuộn lag); lọc / tìm / chuyển trang
/// dùng lại dữ liệu đã đọc, chỉ <see cref="Load"/> (vào trang, đổi khoảng ngày, sau khôi phục) mới đọc file lịch sử.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    public static readonly string[] KindLabels = ["Tất cả", "Đơn bị xoá", "Đơn bị sửa", "Đơn thêm mới"];

    public const int PageSize = 100;

    private IReadOnlyList<HistoryEntry> _all = [];
    private Dictionary<string, int> _batches = [];
    private List<HistoryEntry> _shown = [];
    private int _page;

    public HistoryViewModel()
    {
        Filter.Apply(DateRangePreset.All);
        Filter.Changed += (_, _) =>
        {
            _page = 0;
            Load();
        };
    }

    public DateRangeFilter Filter { get; } = new();

    public ObservableCollection<HistoryRow> Rows { get; } = [];

    [ObservableProperty]
    private int _kindIndex;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>"Trang 2/50 · dòng 101–200"; rỗng khi chỉ 1 trang (ẩn thanh chuyển trang).</summary>
    [ObservableProperty]
    private string _pageText = string.Empty;

    [ObservableProperty]
    private bool _canPrevious;

    [ObservableProperty]
    private bool _canNext;

    partial void OnKindIndexChanged(int value)
    {
        _page = 0;
        Apply();
    }

    partial void OnSearchTextChanged(string value)
    {
        _page = 0;
        Apply();
    }

    /// <summary>Đọc lại file lịch sử; giữ trang đang xem (vd quay lại sau khi Mở đơn).</summary>
    public void Load()
    {
        try
        {
            _all = AppServices.History.Query(Filter.From, Filter.To);
            _batches = _all.Where(e => e.Batch is not null).GroupBy(e => e.Batch!).ToDictionary(g => g.Key, g => g.Count());
            Error = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.For("History").LogError(ex, "Đọc lịch sử chỉnh sửa lỗi");
            Error = "Không đọc được lịch sử chỉnh sửa: " + ex.Message;
            _all = [];
            _batches = [];
        }
        Apply();
    }

    /// <summary>Lọc theo loại / ô tìm trên dữ liệu đã đọc rồi hiện trang hiện tại.</summary>
    private void Apply()
    {
        _shown = _all.Where(Matches).ToList();
        var keep = App.MainWindow?.MenuSettings.HistoryKeepDays ?? MenuSettings.DefaultHistoryKeepDays;
        Summary = $"{VnFormat.Number(_shown.Count)} lần thay đổi" + (keep > 0 ? $" · giữ {keep} ngày" : " · giữ hết");
        IsEmpty = _shown.Count == 0;
        ShowPage();
    }

    private void ShowPage()
    {
        var paging = Paging.Of(_shown.Count, _page, PageSize);
        _page = paging.Page;
        Rows.Clear();
        foreach (var entry in _shown.Skip(paging.Skip).Take(paging.Take))
        {
            Rows.Add(new HistoryRow(entry, entry.Batch is { } b ? _batches[b] : 1));
        }
        PageText = paging.Text;
        CanPrevious = paging.HasPrevious;
        CanNext = paging.HasNext;
    }

    /// <summary>Sang trang trước (-1) / sau (+1).</summary>
    public void MovePage(int delta)
    {
        _page += delta;
        ShowPage();
    }

    private bool Matches(HistoryEntry entry)
    {
        var kindOk = KindIndex switch
        {
            1 => entry.Kind == HistoryKind.Delete,
            2 => entry.Kind == HistoryKind.Edit,
            3 => entry.Kind == HistoryKind.Create,
            _ => true,
        };
        var query = SearchText.Trim();
        return kindOk && (query.Length == 0 || TextSearch.MatchesAllWords(entry.CustomerName, query)
                          || entry.OrderId.ToString(CultureInfo.InvariantCulture) == query.TrimStart('#'));
    }

    /// <summary>Kế hoạch khôi phục của 1 dòng / cả lô (đọc đơn hiện tại).</summary>
    public static IReadOnlyList<RestorePlan> Plan(IEnumerable<HistoryEntry> entries) =>
        entries.Select(e => RestorePlan.For(e, AppServices.Orders.Get(e.OrderId), AppServices.Orders.CustomerExists)).ToList();

    public static IReadOnlyList<RestorePlan> PlanBatch(HistoryEntry entry) =>
        Plan(entry.Batch is { } batch ? AppServices.History.Batch(batch) : [entry]);

    /// <summary>Ghi khôi phục; trả lỗi hoặc null.</summary>
    public string? Restore(IReadOnlyList<RestorePlan> plans, string what)
    {
        var count = 0;
        var error = AppServices.Try("Khôi phục", () => count = AppServices.Orders.Restore(plans));
        if (error is not null)
        {
            return error;
        }
        AppLog.For("History").LogInformation("Khôi phục {What}: {Count} đơn", what, count);
        Message = $"Đã khôi phục {VnFormat.Number(count)} đơn ({what}). Lần khôi phục cũng được ghi vào lịch sử - khôi phục nhầm thì khôi phục lại dòng \"Khôi phục\" mới nhất.";
        Load();
        return null;
    }
}
