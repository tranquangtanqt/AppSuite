using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;

namespace DbTools.ViewModels;

/// <summary>1 dòng trong danh sách Lịch sử câu.</summary>
public sealed class QueryHistoryRow(QueryRecord record)
{
    public QueryRecord Record { get; } = record;

    /// <summary>Câu gọn 1 dòng.</summary>
    public string Sql => string.Join(' ', Record.Sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public string Info => $"{Record.Time:MM-dd HH:mm} · {(Record.Profile.Length > 0 ? Record.Profile : Record.Target)}"
        + (Record.Write ? " · GHI" : string.Empty)
        + (Record.Success ? (Record.Rows is { } n ? $" · {n:N0} dòng" : string.Empty) : " · lỗi")
        + $" · {Record.DurationMs:N0} ms";

    public bool Success => Record.Success;
}

/// <summary>
/// Trang "Chạy SQL": ô soạn câu → Chạy (đoạn đang chọn, không chọn thì cả ô) trong 1 giao dịch chỉ đọc (mặc định) hoặc cho
/// phép ghi (hỏi lại, Production phải gõ tên DB) → bảng kết quả của câu trả về dòng cuối, thẻ Thông báo từng câu. Copy / Xuất
/// CSV (mở CsvEditor), lịch sử 200 câu, nhớ ô soạn.
/// </summary>
public sealed partial class QueryViewModel : ObservableObject
{
    private readonly ILogger _log = AppLog.For("Query");
    private readonly QueryHistoryStore _history = new();
    private List<QueryRecord> _records;
    private CancellationTokenSource? _cts;

    /// <summary>Các mức giới hạn dòng trong ô chọn (0 = tất cả).</summary>
    public static readonly int[] RowLimits = [100, 1000, 10000, 0];

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    public ObservableCollection<QueryHistoryRow> History { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WriteWarning))]
    private ConnectionProfile? _profile;

    [ObservableProperty]
    private string _sqlText = AppState.Settings.QueryDraft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WriteWarning))]
    private bool _allowWrite;

    [ObservableProperty]
    private int _limitIndex = Math.Max(0, Array.IndexOf(RowLimits, AppState.Settings.QueryMaxRows));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(ExportCsvCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Gõ câu SQL rồi bấm Chạy (Ctrl+Enter / F5). Chọn 1 đoạn thì chỉ chạy đoạn đó.";

    [ObservableProperty]
    private bool _statusIsError;

    /// <summary>Thẻ Thông báo: từng câu đã chạy + NOTICE của server + lệnh bị bỏ qua.</summary>
    [ObservableProperty]
    private string _messages = string.Empty;

    /// <summary>0 = Kết quả, 1 = Thông báo.</summary>
    [ObservableProperty]
    private int _tab;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
    [NotifyPropertyChangedFor(nameof(HasTable))]
    private QueryTable? _table;

    [ObservableProperty]
    private string _historyFilter = string.Empty;

    public bool IsIdle => !IsBusy;

    public bool HasTable => Table is not null;

    /// <summary>Dòng đỏ cạnh công tắc khi đang cho phép ghi.</summary>
    public string WriteWarning => !AllowWrite ? string.Empty
        : Profile?.IsProduction == true ? "🔴 Ghi lên PRODUCTION - COMMIT khi mọi câu chạy được"
        : "Câu ghi sẽ được COMMIT khi mọi câu chạy được";

    /// <summary>Trang gắn vào: hỏi lại trước khi chạy ở chế độ cho phép ghi.</summary>
    public Func<RunConfirmation, Task<bool>>? Confirm { get; set; }

    public QueryViewModel()
    {
        Profile = AppState.LastProfile;
        _records = _history.Load();
        RefreshHistory();
    }

    public void EnsureProfile()
    {
        if (Profile is null || !Profiles.Contains(Profile))
        {
            Profile = AppState.LastProfile;
        }
    }

    /// <summary>Đổi server thì luôn về chỉ đọc - không mang chế độ ghi sang server khác.</summary>
    partial void OnProfileChanged(ConnectionProfile? value) => AllowWrite = false;

    partial void OnLimitIndexChanged(int value)
    {
        if (value >= 0 && value < RowLimits.Length)
        {
            AppState.Settings.QueryMaxRows = RowLimits[value];
            AppState.SaveSettings();
        }
    }

    partial void OnHistoryFilterChanged(string value) => RefreshHistory();

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var r in QueryHistoryStore.Filter(_records, HistoryFilter))
        {
            History.Add(new QueryHistoryRow(r));
        }
    }

    public void SaveDraft()
    {
        if (AppState.Settings.QueryDraft != SqlText)
        {
            AppState.Settings.QueryDraft = SqlText;
            AppState.SaveSettings();
        }
    }

    /// <summary>Chạy: <paramref name="selected"/> = đoạn đang chọn trong ô soạn (trống = cả ô).</summary>
    public async Task RunAsync(string? selected, int selectionStart)
    {
        if (IsBusy)
        {
            return;
        }
        if (Profile is not { } profile)
        {
            SetStatus("Chưa có kết nối - thêm ở trang Kết nối.", true);
            return;
        }
        var (text, lineOffset) = QuickQuery.Pick(SqlText, selected, selectionStart);
        var plan = QuickQuery.Plan(text, lineOffset);
        if (plan.Rejected.Count > 0)
        {
            Messages = string.Join(Environment.NewLine, plan.Rejected);
            Tab = 1;
            SetStatus(plan.Rejected[0], true);
            return;
        }
        if (plan.Statements.Count == 0)
        {
            SetStatus("Chưa có câu nào để chạy.", true);
            return;
        }
        var write = AllowWrite;
        if (write && Confirm is not null && !await Confirm(WriteConfirmation(profile, plan)))
        {
            return;
        }

        SaveDraft();
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var notices = new ConcurrentQueue<string>();
        var maxRows = RowLimits[Math.Clamp(LimitIndex, 0, RowLimits.Length - 1)];
        var started = DateTime.Now;
        SetStatus($"Đang chạy {plan.Statements.Count} câu trên {profile.Target}{(write ? " (cho phép ghi)" : " (chỉ đọc)")}...", false);
        QueryOutcome? outcome = null;
        string? openError = null;
        try
        {
            outcome = await Task.Run(() => PgQuery.RunAsync(profile, plan.Statements, write, maxRows, AppState.Settings.QueryTimeoutSeconds,
                _cts.Token, notices.Enqueue));
        }
        catch (OperationCanceledException)
        {
            openError = "Đã huỷ (chưa kết nối xong).";
        }
        catch (Exception ex)
        {
            openError = "Không kết nối được: " + Pg.Describe(ex);
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }

        var lines = new List<string>(plan.Skipped);
        if (outcome is not null)
        {
            lines.AddRange(outcome.Statements.Select(s => s.Describe()));
        }
        lines.AddRange(notices);
        if (openError is not null)
        {
            lines.Add(openError);
        }
        Messages = string.Join(Environment.NewLine, lines);

        if (outcome is null)
        {
            SetStatus(openError!, true);
            AddHistory(profile, text, write, started, false, null, 0, openError!);
            return;
        }
        Table = outcome.Table;
        var tableText = outcome.Table is { } t
            ? $" · bảng {t.Rows.Count:N0} dòng{(t.Truncated ? $" (còn nữa - giới hạn {maxRows:N0}; Xuất CSV lấy đủ)" : string.Empty)}"
            : string.Empty;
        if (outcome.Success)
        {
            Tab = outcome.Table is null ? 1 : 0;
            SetStatus($"Xong {outcome.Statements.Count} câu{tableText} · {outcome.DurationMs:N0} ms · "
                + (write ? "đã COMMIT" : "chỉ đọc (đã ROLLBACK, DB không đổi)"), false);
        }
        else
        {
            Tab = 1;
            SetStatus((outcome.Error == "Đã huỷ" ? $"Đã huỷ (đang chạy câu ở dòng {outcome.ErrorLine})" : $"Lỗi ở dòng {outcome.ErrorLine}: {outcome.Error}")
                + " - đã ROLLBACK, DB không đổi."
                + (!write && ReadOnlyViolation(outcome.Error) ? " Câu này ghi dữ liệu: bật \"Cho phép ghi\" nếu đúng là muốn ghi." : string.Empty), true);
        }
        _log.LogInformation("Chạy SQL {Count} câu trên {Target} ({Mode}): {Result}", plan.Statements.Count, profile.Target,
            write ? "ghi" : "chỉ đọc", outcome.Success ? "xong" : outcome.Error);
        AddHistory(profile, text, write, started, outcome.Success, outcome.Table?.Rows.Count, outcome.DurationMs, outcome.Error ?? string.Empty);
    }

    /// <summary>25006 = read_only_sql_transaction.</summary>
    private static bool ReadOnlyViolation(string? error) => error?.StartsWith("25006", StringComparison.Ordinal) == true;

    private static RunConfirmation WriteConfirmation(ConnectionProfile profile, QueryPlan plan)
    {
        var lines = new List<string>
        {
            $"Server: {profile.Target} (user {profile.Username}) · {profile.SchemaText}",
            $"{plan.Statements.Count} câu, chạy trong 1 giao dịch: mọi câu chạy được thì COMMIT; câu nào lỗi hoặc bấm Huỷ thì ROLLBACK hết.",
        };
        lines.AddRange(plan.Statements.Take(8).Select(s => $"  Dòng {s.Line}: {Shorten(s.Text)}"));
        if (plan.Statements.Count > 8)
        {
            lines.Add($"  ... và {plan.Statements.Count - 8} câu khác");
        }
        return new RunConfirmation("Chạy và COMMIT?", lines, "Chạy và COMMIT",
            ProductionGuard.ForQuery(profile.IsProduction, true, profile.Database, profile.Name));
    }

    private static string Shorten(string sql)
    {
        var one = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length > 120 ? one[..120] + "…" : one;
    }

    private void SetStatus(string text, bool error)
    {
        Status = text;
        StatusIsError = error;
    }

    private void AddHistory(ConnectionProfile profile, string sql, bool write, DateTime time, bool success, long? rows, long ms, string error)
    {
        try
        {
            _records = _history.Add(new QueryRecord
            {
                Time = time, Sql = sql.Trim(), Profile = profile.FullName, Target = profile.Target, Write = write,
                Success = success, Rows = rows, DurationMs = ms, Error = error,
            });
            RefreshHistory();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Không ghi được lịch sử câu");
        }
    }

    public void LoadFromHistory(QueryHistoryRow row) => SqlText = row.Record.Sql;

    [RelayCommand]
    private void ClearHistory()
    {
        _history.Clear();
        _records = [];
        RefreshHistory();
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    private bool CanExport => !IsBusy && Table is not null;

    /// <summary>Ghi đủ mọi dòng ra &lt;thư mục dump&gt;\ChaySql\&lt;hồ sơ&gt;_&lt;lúc&gt;.csv rồi mở (CsvEditor nếu có).</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportCsvAsync()
    {
        if (Table is not { } table || Profile is not { } profile)
        {
            return;
        }
        var path = Path.Combine(AppState.Settings.DumpFolder, "ChaySql", QuickQuery.ExportFileName(profile.Name, DateTime.Now));
        IsBusy = true;
        _cts = new CancellationTokenSource();
        SetStatus(table.Truncated ? "Đang đọc lại đủ mọi dòng (giao dịch chỉ đọc) và ghi CSV..." : "Đang ghi CSV...", false);
        try
        {
            var count = await Task.Run(() => PgQuery.ExportCsvAsync(profile, table, path, AppState.Settings.QueryTimeoutSeconds, _cts.Token));
            SetStatus($"Đã xuất {count:N0} dòng → {path}", false);
            Shell.OpenData(path);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Đã huỷ xuất CSV.", true);
        }
        catch (Exception ex)
        {
            SetStatus("Không xuất được CSV: " + Pg.Describe(ex)
                + (ReadOnlyViolation(Pg.Describe(ex)) ? " (câu ghi dữ liệu không chạy lại được - giảm phạm vi hoặc chọn giới hạn Tất cả rồi Chạy lại)" : string.Empty), true);
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>Báo ở dòng trạng thái sau khi chép (trang làm việc với clipboard).</summary>
    public void Copied(int rows) => SetStatus($"Đã chép {rows:N0} dòng (dán vào Excel / CsvEditor được).", false);
}
