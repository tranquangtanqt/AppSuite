using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;

namespace DbTools.ViewModels;

/// <summary>1 dòng phiên kết nối (hiển thị).</summary>
public sealed class SessionRow(PgSession session)
{
    public PgSession Session { get; } = session;
    public int Pid => Session.Pid;
    public string PidText => Session.IsSelf ? $"{Session.Pid} (DbTools)" : Session.Pid.ToString();
    public string Database => Session.Database;
    public string Who => $"{Session.User}{(Session.Application.Length > 0 ? " · " + Session.Application : string.Empty)}";
    public string Client => Session.Client;
    public string State => Session.State switch
    {
        "active" => "đang chạy",
        "idle" => "rảnh",
        "idle in transaction" => "rảnh trong transaction",
        "idle in transaction (aborted)" => "transaction lỗi, chưa rollback",
        "" => "-",
        var s => s,
    };
    public string Since => Session.BackendStart is { } t ? t.ToString("MM-dd HH:mm:ss") : string.Empty;
    public string Query => Session.Query.ReplaceLineEndings(" ").Trim();
    public string QueryTip => Session.QueryStart is { } t ? $"Câu gần nhất (bắt đầu {t:yyyy-MM-dd HH:mm:ss}):{Environment.NewLine}{Session.Query}" : Session.Query;
}

/// <summary>
/// Trang "Phiên kết nối": ai đang kết nối vào database nào (pg_stat_activity), ngắt phiên đã chọn / ngắt hết
/// (pg_terminate_backend) - vd trước khi copy DB bằng TEMPLATE, restore, xoá DB, hoặc gỡ phiên treo giữ khoá.
/// </summary>
public sealed partial class SessionsViewModel : ObservableObject
{
    private readonly ILogger _log = AppLog.For("Sessions");

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    public ObservableCollection<SessionRow> Rows { get; } = [];

    public ObservableCollection<string> Databases { get; } = [];

    [ObservableProperty]
    private ConnectionProfile? _profile;

    /// <summary>Lọc theo database; trống = mọi database trên server.</summary>
    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(TerminateSelectedCommand), nameof(TerminateAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Chọn server (hồ sơ kết nối) rồi bấm Làm mới.";

    /// <summary>Dòng đang chọn (ListView chọn nhiều - trang cập nhật).</summary>
    public List<SessionRow> Selected { get; } = [];

    /// <summary>Trang gắn vào: hỏi lại trước khi ngắt.</summary>
    public Func<RunConfirmation, Task<bool>>? Confirm { get; set; }

    public bool IsEmpty => Rows.Count == 0;

    public SessionsViewModel()
    {
        Profile = Profiles.FirstOrDefault(p => p.Name == AppState.Settings.LastProfile) ?? Profiles.FirstOrDefault();
    }

    partial void OnProfileChanged(ConnectionProfile? value)
    {
        Database = value?.Database ?? string.Empty;
        Databases.Clear();
        Rows.Clear();
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void EnsureProfile()
    {
        if (Profile is null || !Profiles.Contains(Profile))
        {
            Profile = Profiles.FirstOrDefault(p => p.Name == AppState.Settings.LastProfile) ?? Profiles.FirstOrDefault();
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

    private async Task LoadAsync(ConnectionProfile profile)
    {
        using var cts = new CancellationTokenSource(Pg.Within(20));
        var filter = Database.Trim().Length > 0 ? Database.Trim() : null;
        var sessions = await PgAdmin.ListSessionsAsync(profile, filter, cts.Token);
        Rows.Clear();
        Selected.Clear();
        foreach (var s in sessions)
        {
            Rows.Add(new SessionRow(s));
        }
        OnPropertyChanged(nameof(IsEmpty));
        var others = sessions.Count(s => !s.IsSelf);
        Status = $"{(filter is null ? "Mọi database" : "Database " + filter)} trên {profile.Host}:{profile.Port}: {others} phiên"
            + (sessions.Any(s => s.IsSelf) ? " (+ 1 phiên của DbTools dùng để đọc danh sách)" : string.Empty)
            + $" · lúc {DateTime.Now:HH:mm:ss}";
    }

    private bool CanTerminate => !IsBusy && Rows.Count > 0;

    [RelayCommand(CanExecute = nameof(CanTerminate))]
    private Task TerminateSelectedAsync() => TerminateAsync(Selected.Where(r => !r.Session.IsSelf).ToList(), "đã chọn");

    [RelayCommand(CanExecute = nameof(CanTerminate))]
    private Task TerminateAllAsync() => TerminateAsync(Rows.Where(r => !r.Session.IsSelf).ToList(), "trong danh sách");

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
        lines.AddRange(rows.Take(12).Select(r => $"  {r.Pid} · {r.Database} · {r.Who} · {r.Client} · {r.State}"));
        if (rows.Count > 12)
        {
            lines.Add($"  ... và {rows.Count - 12} phiên khác");
        }
        if (Confirm is not null && !await Confirm(new RunConfirmation($"Ngắt {rows.Count} phiên?", lines, "Ngắt phiên",
                Core.ProductionGuard.ForSessions(profile.IsProduction, rows.Select(r => r.Database), profile.Name, profile.Database))))
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

    public void SelectionChanged() => TerminateSelectedCommand.NotifyCanExecuteChanged();
}
