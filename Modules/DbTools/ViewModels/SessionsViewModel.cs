using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;

namespace DbTools.ViewModels;

/// <summary>1 dòng phiên kết nối (hiển thị).</summary>
public sealed class SessionRow(PgSession session, BlockState block, string blockText)
{
    public PgSession Session { get; } = session;
    public int Pid => Session.Pid;
    public string PidText => Session.IsSelf ? $"{Session.Pid} (DbTools)" : Session.Pid.ToString();
    public string Database => Session.Database;
    public string Who => $"{Session.User}{(Session.Application.Length > 0 ? " · " + Session.Application : string.Empty)}";
    public string WhoTip => $"{Who}{Environment.NewLine}Máy: {Session.Client}{(Session.BackendStart is { } t ? $"{Environment.NewLine}Kết nối lúc {t:yyyy-MM-dd HH:mm:ss}" : string.Empty)}";
    public string Client => Session.Client;
    public string State => StateText(Session.State);
    public string Since => Session.BackendStart is { } t ? t.ToString("MM-dd HH:mm:ss") : string.Empty;
    public string Query => Session.Query.ReplaceLineEndings(" ").Trim();
    public string QueryTip => Session.QueryStart is { } t ? $"Câu gần nhất (bắt đầu {t:yyyy-MM-dd HH:mm:ss}):{Environment.NewLine}{Session.Query}" : Session.Query;

    public BlockState Block { get; } = block;

    /// <summary>Cột Chờ / chặn, vd "⛔ chờ khoá, bị PID 1234 chặn".</summary>
    public string BlockText { get; } = blockText;

    /// <summary>Câu đang chạy bao lâu (chỉ khi đang chạy).</summary>
    public string QueryTime => SessionBlocking.Duration(Session.QuerySeconds);

    /// <summary>Transaction đang mở bao lâu, vd "tx 3 phút 05 giây".</summary>
    public string XactTime => Session.XactSeconds is { } x ? "tx " + SessionBlocking.Duration(x) : string.Empty;

    public bool IsBlocked => Block == BlockState.Blocked;
    public bool IsBlocking => Block == BlockState.Blocking;

    public static string StateText(string state) => state switch
    {
        "active" => "đang chạy",
        "idle" => "rảnh",
        "idle in transaction" => "rảnh trong transaction",
        "idle in transaction (aborted)" => "transaction lỗi, chưa rollback",
        "" => "-",
        var s => s,
    };
}

/// <summary>
/// Trang "Phiên kết nối": ai đang kết nối vào database nào (pg_stat_activity), ai chặn ai (pg_blocking_pids) + khoá đang chờ,
/// câu chạy bao lâu, lọc chỉ phiên đang chạy / chặn / bị chặn, tự làm mới 5 giây; Huỷ câu đang chạy (pg_cancel_backend) /
/// Ngắt phiên (pg_terminate_backend) đã chọn / ngắt hết; từ chi tiết: huỷ câu / ngắt phiên đang chặn.
/// </summary>
public sealed partial class SessionsViewModel : ObservableObject
{
    private readonly ILogger _log = AppLog.For("Sessions");
    private List<PgSession> _all = [];
    private CancellationTokenSource? _autoCts;
    private int _detailVersion;

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    public ObservableCollection<SessionRow> Rows { get; } = [];

    public ObservableCollection<string> Databases { get; } = [];

    [ObservableProperty]
    private ConnectionProfile? _profile;

    /// <summary>Lọc theo database; trống = mọi database trên server.</summary>
    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(TerminateSelectedCommand), nameof(TerminateAllCommand), nameof(CancelSelectedCommand),
        nameof(CancelBlockersCommand), nameof(TerminateBlockersCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Chọn server (hồ sơ kết nối) rồi bấm Làm mới.";

    /// <summary>Chỉ hiện phiên đang chạy câu / đang chặn / bị chặn.</summary>
    [ObservableProperty]
    private bool _busyOnly;

    /// <summary>Tự làm mới mỗi <see cref="AutoSeconds"/> giây (tắt sẵn; đổi server / rời trang thì tắt).</summary>
    [ObservableProperty]
    private bool _autoRefresh;

    public const int AutoSeconds = 5;

    /// <summary>Ô chi tiết của phiên đang chọn (chọn đúng 1 dòng).</summary>
    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelBlockersCommand), nameof(TerminateBlockersCommand))]
    private bool _detailBlocked;

    /// <summary>Dòng đang chọn (ListView chọn nhiều - trang cập nhật).</summary>
    public List<SessionRow> Selected { get; } = [];

    /// <summary>Trang gắn vào: hỏi lại trước khi huỷ / ngắt.</summary>
    public Func<RunConfirmation, Task<bool>>? Confirm { get; set; }

    /// <summary>Đã nạp lại danh sách: trang chọn lại các PID đang chọn trước đó.</summary>
    public event EventHandler<IReadOnlyList<int>>? Reloaded;

    public bool IsEmpty => Rows.Count == 0;

    public bool HasDetail => Detail.Length > 0;

    public SessionsViewModel()
    {
        Profile = AppState.LastProfile;
    }

    partial void OnProfileChanged(ConnectionProfile? value)
    {
        AutoRefresh = false;
        EditableBox.Reset(Databases, t => Database = t, value?.Database ?? string.Empty);
        _all = [];
        Rows.Clear();
        Detail = string.Empty;
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnDetailChanged(string value) => OnPropertyChanged(nameof(HasDetail));

    partial void OnBusyOnlyChanged(bool value) => ShowRows();

    partial void OnAutoRefreshChanged(bool value)
    {
        _autoCts?.Cancel();
        _autoCts = null;
        if (value)
        {
            _autoCts = new CancellationTokenSource();
            _ = AutoLoopAsync(_autoCts.Token);
        }
    }

    /// <summary>Mỗi 5 giây đọc lại (bỏ lượt nếu đang bận / đang mở hộp hỏi lại). Lỗi thì báo và tắt.</summary>
    private async Task AutoLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(AutoSeconds), token);
                if (!IsBusy && !_confirming && Profile is { } profile)
                {
                    IsBusy = true;
                    try
                    {
                        await LoadAsync(profile);
                    }
                    catch (Exception ex)
                    {
                        Status = "Tự làm mới dừng: " + Pg.Describe(ex);
                        AutoRefresh = false;
                    }
                    finally
                    {
                        IsBusy = false;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Tắt tự làm mới.
        }
    }

    private bool _confirming;

    private async Task<bool> AskAsync(RunConfirmation confirmation)
    {
        if (Confirm is null)
        {
            return true;
        }
        _confirming = true;
        try
        {
            return await Confirm(confirmation);
        }
        finally
        {
            _confirming = false;
        }
    }

    public void EnsureProfile()
    {
        if (Profile is null || !Profiles.Contains(Profile))
        {
            Profile = AppState.LastProfile;
        }
    }

    public async Task LoadDatabasesAsync()
    {
        if (Profile is not { } profile)
        {
            return;
        }
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(10));
            foreach (var db in (await PgAdmin.ListDatabasesAsync(profile, cts.Token)).Where(db => !Databases.Contains(db)))
            {
                Databases.Add(db);
            }
        }
        catch (Exception ex)
        {
            _log.LogInformation("Không đọc được danh sách database: {Error}", Pg.Describe(ex));
        }
    }

    private bool CanRefresh => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (Profile is not { } profile)
        {
            Status = "Chưa có kết nối - thêm ở trang Kết nối.";
            return;
        }
        IsBusy = true;
        try
        {
            await LoadAsync(profile);
        }
        catch (Exception ex)
        {
            Status = "Không đọc được danh sách phiên: " + Pg.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static SessionLink Link(PgSession s) => new(s.Pid, s.State, s.BlockedBy, s.IsSelf);

    private async Task LoadAsync(ConnectionProfile profile)
    {
        using var cts = new CancellationTokenSource(Pg.Within(20));
        var filter = Database.Trim().Length > 0 ? Database.Trim() : null;
        _all = await PgAdmin.ListSessionsAsync(profile, filter, cts.Token);
        ShowRows();
        var links = _all.Select(Link).ToList();
        var blocks = SessionBlocking.Blocks(links);
        var blocked = links.Count(l => l.BlockedBy.Count > 0);
        var others = _all.Count(s => !s.IsSelf);
        Status = $"{(filter is null ? "Mọi database" : "Database " + filter)} trên {profile.Host}:{profile.Port}: {others} phiên"
            + (_all.Any(s => s.IsSelf) ? " (+ 1 phiên của DbTools dùng để đọc danh sách)" : string.Empty)
            + $", {_all.Count(s => s.State == "active" && !s.IsSelf)} đang chạy"
            + (blocked > 0 ? $", ⛔ {blocked} phiên bị chặn bởi {blocks.Count} phiên" : string.Empty)
            + $" · lúc {DateTime.Now:HH:mm:ss}"
            + (AutoRefresh ? $" (tự làm mới {AutoSeconds} giây)" : string.Empty);
    }

    /// <summary>Dựng lại bảng theo ô lọc, giữ các dòng đang chọn (trang chọn lại theo PID).</summary>
    private void ShowRows()
    {
        var keep = Selected.Select(r => r.Pid).ToList();
        var links = _all.Select(Link).ToList();
        var blocks = SessionBlocking.Blocks(links);
        Rows.Clear();
        Selected.Clear();
        foreach (var s in _all)
        {
            var link = Link(s);
            if (BusyOnly && !SessionBlocking.IsBusy(link, blocks))
            {
                continue;
            }
            Rows.Add(new SessionRow(s, SessionBlocking.StateOf(link, blocks), SessionBlocking.BlockText(link, blocks, s.WaitEventType, s.WaitEvent)));
        }
        OnPropertyChanged(nameof(IsEmpty));
        Reloaded?.Invoke(this, keep);
        SelectionChanged();
    }

    private bool CanAct => !IsBusy && Rows.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task TerminateSelectedAsync() => TerminateAsync(Selected.Where(r => !r.Session.IsSelf).ToList(), "đã chọn");

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task TerminateAllAsync() => TerminateAsync(Rows.Where(r => !r.Session.IsSelf).ToList(), "trong danh sách");

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task CancelSelectedAsync() => CancelAsync(Selected.Where(r => !r.Session.IsSelf).ToList(), "đã chọn");

    private bool CanActOnBlockers => !IsBusy && DetailBlocked;

    /// <summary>Huỷ câu của các phiên đầu chuỗi đang chặn phiên đang chọn.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnBlockers))]
    private Task CancelBlockersAsync() => CancelAsync(BlockerRows(), "đang chặn PID " + Selected.FirstOrDefault()?.Pid);

    [RelayCommand(CanExecute = nameof(CanActOnBlockers))]
    private Task TerminateBlockersAsync() => TerminateAsync(BlockerRows(), "đang chặn PID " + Selected.FirstOrDefault()?.Pid);

    private List<SessionRow> BlockerRows()
    {
        if (Selected is not [var row])
        {
            return [];
        }
        var byPid = _all.ToDictionary(s => s.Pid, Link);
        var roots = SessionBlocking.RootBlockers(row.Pid, byPid);
        var links = _all.Select(Link).ToList();
        var blocks = SessionBlocking.Blocks(links);
        return _all.Where(s => roots.Contains(s.Pid) && !s.IsSelf)
            .Select(s => new SessionRow(s, SessionBlocking.StateOf(Link(s), blocks), SessionBlocking.BlockText(Link(s), blocks, s.WaitEventType, s.WaitEvent)))
            .ToList();
    }

    private static IEnumerable<string> Describe(IEnumerable<SessionRow> rows) =>
        rows.Take(12).Select(r => $"  {r.Pid} · {r.Database} · {r.Who} · {r.Client} · {r.State}{(r.QueryTime.Length > 0 ? " " + r.QueryTime : string.Empty)}"
            + (r.Query.Length > 0 ? $" · {(r.Query.Length > 80 ? r.Query[..80] + "…" : r.Query)}" : string.Empty));

    private async Task CancelAsync(List<SessionRow> rows, string which)
    {
        if (Profile is not { } profile)
        {
            return;
        }
        if (rows.Count == 0)
        {
            Status = "Chưa chọn phiên nào (phiên của chính DbTools không huỷ).";
            return;
        }
        var lines = new List<string>
        {
            $"Server: {profile.Host}:{profile.Port} (user {profile.Username})",
            $"Huỷ câu đang chạy của {rows.Count} phiên {which}: câu đó dừng với lỗi \"canceling statement due to user request\", transaction đang mở bị rollback; phiên vẫn kết nối.",
        };
        lines.AddRange(Describe(rows));
        if (rows.Count > 12)
        {
            lines.Add($"  ... và {rows.Count - 12} phiên khác");
        }
        var idle = rows.Count(r => r.Session.State != "active");
        if (idle > 0)
        {
            lines.Add($"⚠ {idle} phiên không có câu đang chạy (rảnh / rảnh trong transaction): huỷ câu không có tác dụng - phiên rảnh trong transaction vẫn giữ khoá, phải Ngắt phiên mới gỡ được.");
        }
        if (!await AskAsync(new RunConfirmation($"Huỷ câu của {rows.Count} phiên?", lines, "Huỷ câu",
                ProductionGuard.ForSessions(profile.IsProduction, rows.Select(r => r.Database), profile.Name, profile.Database))))
        {
            return;
        }
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(30));
            var done = await PgAdmin.CancelAsync(profile, rows.Select(r => r.Pid).ToList(), cts.Token);
            _log.LogInformation("Huỷ câu {Done}/{Count} phiên trên {Host}:{Port}", done, rows.Count, profile.Host, profile.Port);
            await Task.Delay(500);
            await LoadAsync(profile);
            Status = $"Đã gửi lệnh huỷ câu tới {done}/{rows.Count} phiên"
                + (done < rows.Count ? " (phiên còn lại đã thoát hoặc user không đủ quyền)" : string.Empty) + ". " + Status;
        }
        catch (Exception ex)
        {
            Status = "Không huỷ được: " + Pg.Describe(ex)
                + " - cần superuser, thành viên pg_signal_backend, hoặc cùng user với phiên đó.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TerminateAsync(List<SessionRow> rows, string which)
    {
        if (Profile is not { } profile)
        {
            return;
        }
        if (rows.Count == 0)
        {
            Status = "Chưa chọn phiên nào (phiên của chính DbTools không ngắt).";
            return;
        }
        var lines = new List<string>
        {
            $"Server: {profile.Host}:{profile.Port} (user {profile.Username})",
            $"Ngắt {rows.Count} phiên {which}: câu đang chạy bị huỷ, transaction chưa COMMIT bị rollback, ứng dụng đó mất kết nối.",
        };
        lines.AddRange(Describe(rows));
        if (rows.Count > 12)
        {
            lines.Add($"  ... và {rows.Count - 12} phiên khác");
        }
        if (!await AskAsync(new RunConfirmation($"Ngắt {rows.Count} phiên?", lines, "Ngắt phiên",
                ProductionGuard.ForSessions(profile.IsProduction, rows.Select(r => r.Database), profile.Name, profile.Database))))
        {
            return;
        }
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(30));
            var killed = await PgAdmin.TerminateAsync(profile, rows.Select(r => r.Pid).ToList(), cts.Token);
            _log.LogInformation("Ngắt {Killed}/{Count} phiên trên {Host}:{Port}", killed, rows.Count, profile.Host, profile.Port);
            await Task.Delay(300);
            await LoadAsync(profile);
            Status = $"Đã ngắt {killed}/{rows.Count} phiên"
                + (killed < rows.Count ? " (phiên còn lại đã tự thoát hoặc user không đủ quyền ngắt)" : string.Empty) + ". " + Status;
        }
        catch (Exception ex)
        {
            Status = "Không ngắt được: " + Pg.Describe(ex)
                + " - cần superuser, thành viên pg_signal_backend, hoặc cùng user với phiên đó.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Đổi dòng đang chọn: cập nhật nút + ô chi tiết (chọn đúng 1 dòng). Phiên bị chặn thì đọc thêm khoá đang chờ.</summary>
    public void SelectionChanged()
    {
        TerminateSelectedCommand.NotifyCanExecuteChanged();
        CancelSelectedCommand.NotifyCanExecuteChanged();
        _ = UpdateDetailAsync();
    }

    private async Task UpdateDetailAsync()
    {
        var version = ++_detailVersion;
        if (Selected is not [var row])
        {
            Detail = string.Empty;
            DetailBlocked = false;
            return;
        }
        var s = row.Session;
        var lines = new List<string>
        {
            $"PID {row.PidText} · {s.Database} · {row.Who} · máy {s.Client} · {row.State}"
                + (row.QueryTime.Length > 0 ? $" · câu chạy {row.QueryTime}" : string.Empty)
                + (row.XactTime.Length > 0 ? $" · {row.XactTime}" : string.Empty)
                + (s.WaitEventType.Length > 0 ? $" · chờ {s.WaitEventType} / {s.WaitEvent}" : string.Empty),
            s.Query.Length > 0 ? (s.State == "active" ? "Câu đang chạy: " : "Câu gần nhất: ") + s.Query.Trim() : "(chưa chạy câu nào)",
        };
        var byPid = _all.ToDictionary(x => x.Pid, Link);
        var blocks = SessionBlocking.Blocks(byPid.Values);
        if (blocks.TryGetValue(s.Pid, out var victims))
        {
            lines.Add($"🔒 Đang chặn {victims.Count} phiên: PID {string.Join(", ", victims)}"
                + (s.State.StartsWith("idle in transaction", StringComparison.Ordinal) ? " - phiên này rảnh trong transaction (quên COMMIT / ROLLBACK?): huỷ câu không gỡ được, phải ngắt phiên." : string.Empty));
        }
        DetailBlocked = s.BlockedBy.Count > 0;
        if (s.BlockedBy.Count > 0)
        {
            var roots = SessionBlocking.RootBlockers(s.Pid, byPid);
            lines.Add($"⛔ Bị chặn bởi PID {string.Join(", ", s.BlockedBy.Distinct())}"
                + (roots.Count > 0 && !roots.SequenceEqual(s.BlockedBy.Distinct()) ? $" (đầu chuỗi: PID {string.Join(", ", roots)})" : string.Empty) + ":");
            foreach (var b in _all.Where(x => roots.Contains(x.Pid)))
            {
                lines.Add($"  PID {b.Pid} · {b.Database} · {b.User}{(b.Application.Length > 0 ? " · " + b.Application : string.Empty)} · máy {b.Client} · {SessionRow.StateText(b.State)}"
                    + (b.XactSeconds is { } x ? $" · tx {SessionBlocking.Duration(x)}" : string.Empty)
                    + $" · câu: {b.Query.ReplaceLineEndings(" ").Trim()}");
            }
        }
        Detail = string.Join(Environment.NewLine, lines);
        if (s.BlockedBy.Count > 0 && Profile is { } profile)
        {
            try
            {
                using var cts = new CancellationTokenSource(Pg.Within(10));
                var waits = await PgAdmin.LockWaitsAsync(profile, s.Database, s.Pid, cts.Token);
                if (version == _detailVersion && waits.Count > 0)
                {
                    Detail += Environment.NewLine + "Đang chờ khoá: " + string.Join("; ", waits.Select(w =>
                        $"{w.Mode} trên {(w.Relation.Length > 0 ? "bảng " + w.Relation : w.LockType)}{(w.Detail.Length > 0 ? " (" + w.Detail + ")" : string.Empty)}"));
                }
            }
            catch (Exception ex)
            {
                _log.LogInformation("Không đọc được khoá đang chờ: {Error}", Pg.Describe(ex));
            }
        }
    }

    /// <summary>Rời trang: thôi tự làm mới.</summary>
    public void Leave() => AutoRefresh = false;
}
