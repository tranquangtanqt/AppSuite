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

    public string ChangesText => string.Join("\n", HistoryText.Changes(Entry.Before, Entry.After));

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
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    public static readonly string[] KindLabels = ["Tất cả", "Đơn bị xoá", "Đơn bị sửa", "Đơn thêm mới"];

    public HistoryViewModel()
    {
        Filter.Apply(DateRangePreset.All);
        Filter.Changed += (_, _) => Load();
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

    partial void OnKindIndexChanged(int value) => Load();

    partial void OnSearchTextChanged(string value) => Load();

    public void Load()
    {
        Rows.Clear();
        try
        {
            var entries = AppServices.History.Query(Filter.From, Filter.To);
            var batches = entries.Where(e => e.Batch is not null).GroupBy(e => e.Batch!).ToDictionary(g => g.Key, g => g.Count());
            var shown = entries.Where(Matches).ToList();
            foreach (var entry in shown)
            {
                Rows.Add(new HistoryRow(entry, entry.Batch is { } b ? batches[b] : 1));
            }
            var keep = App.MainWindow?.MenuSettings.HistoryKeepDays ?? MenuSettings.DefaultHistoryKeepDays;
            Summary = $"{VnFormat.Number(shown.Count)} lần thay đổi" + (keep > 0 ? $" · giữ {keep} ngày" : " · giữ hết");
            IsEmpty = shown.Count == 0;
            Error = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.For("History").LogError(ex, "Đọc lịch sử chỉnh sửa lỗi");
            Error = "Không đọc được lịch sử chỉnh sửa: " + ex.Message;
        }
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
