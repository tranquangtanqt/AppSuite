using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;

namespace DbTools.ViewModels;

/// <summary>1 dòng ở danh sách thông số (hiển thị).</summary>
public sealed class SettingRow(PgSetting setting, SettingChange? pending, bool overridden)
{
    public PgSetting Setting { get; } = setting;
    public SettingChange? Pending { get; } = pending;
    public string Name => Setting.Name;
    public string Desc => Setting.ShortDesc;

    /// <summary>Giá trị, có thay đổi chờ áp dụng thì "cũ → mới".</summary>
    public string ValueText => Pending is null ? Setting.Value
        : Pending.Reset ? $"{Setting.Value} → mặc định" : $"{Setting.Value} → {Pending.NewValue}";

    /// <summary>Dấu nhỏ cạnh tên: ✎ chờ áp dụng, ● đặt bằng ALTER SYSTEM, ↻ cần khởi động lại (◆ đã ghi, chờ khởi động lại), ⚑ đặt riêng DB / user.</summary>
    public string Marks => string.Concat(
        Pending is not null ? "✎ " : string.Empty,
        Setting.ByAlterSystem ? "● " : string.Empty,
        Setting.PendingRestart ? "◆ " : Setting.NeedsRestart ? "↻ " : string.Empty,
        overridden ? "⚑ " : string.Empty).TrimEnd();

    public bool HasPending => Pending is not null;
}

/// <summary>1 dòng ở ô chi tiết: nhãn + nội dung.</summary>
public sealed record DetailLine(string Label, string Text);

/// <summary>1 mục ở ô chọn nhóm (Key null = tất cả).</summary>
public sealed record SettingGroup(string? Key, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Trang "Cấu hình server": mọi thông số của server (pg_settings) theo nhóm, tìm / lọc, chi tiết từng thông số (mô tả, mặc định,
/// nguồn, áp dụng khi nào, khoảng cho phép, đặt riêng theo database / user). Sửa nhiều dòng → chờ áp dụng → Áp dụng: hỏi lại
/// (Production gõ tên DB) → ALTER SYSTEM SET / RESET + pg_reload_conf → đọc lại, báo thông số chờ khởi động lại.
/// </summary>
public sealed partial class ServerConfigViewModel : ObservableObject
{
    private readonly ILogger _log = AppLog.For("ServerConfig");
    private readonly Dictionary<string, SettingChange> _pending = new(StringComparer.Ordinal);
    private ServerConfig? _config;

    /// <summary>Lối tắt từ trang khác (vd nút ở Nhật ký server): hồ sơ + nhóm mở sẵn.</summary>
    public static (ConnectionProfile Profile, string GroupKey)? Request { get; set; }

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    public ObservableCollection<SettingRow> Rows { get; } = [];

    public ObservableCollection<SettingGroup> Groups { get; } = [new(null, "Mọi nhóm")];

    public ObservableCollection<string> EditOptions { get; } = [];

    [ObservableProperty]
    private ConnectionProfile? _profile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ApplyCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private string _status = "Chọn server rồi bấm Làm mới để đọc cấu hình.";

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    private SettingGroup? _group;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _changedOnly;

    [ObservableProperty]
    private bool _alterSystemOnly;

    [ObservableProperty]
    private bool _restartOnly;

    [ObservableProperty]
    private bool _overriddenOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(Detail), nameof(IsChoice), nameof(IsText), nameof(CanEdit), nameof(CanReset), nameof(CanUnstage))]
    private SettingRow? _selected;

    /// <summary>Ô sửa giá trị của thông số đang chọn.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditChoice))]
    private string _editText = string.Empty;

    /// <summary>Ô chọn (on / off, enum) - cùng giá trị với <see cref="EditText"/>.</summary>
    public object? EditChoice
    {
        get => EditOptions.Contains(EditText) ? EditText : null;
        set
        {
            if (value is string s)
            {
                EditText = s;
            }
        }
    }

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(ClearPendingCommand))]
    private string _pendingText = string.Empty;

    public bool HasPending => _pending.Count > 0;

    public bool NoPending => _pending.Count == 0;

    public bool HasSelection => Selected is not null;

    /// <summary>on / off hoặc enum: chọn trong danh sách.</summary>
    public bool IsChoice => Selected?.Setting.VarType is "bool" or "enum";

    public bool IsText => Selected is not null && !IsChoice;

    public bool CanEdit => Selected is { Setting.ReadOnly: false };

    public bool CanReset => Selected is { Setting.ByAlterSystem: true };

    public bool CanUnstage => Selected?.HasPending == true;

    /// <summary>Phần chi tiết của thông số đang chọn (các dòng "nhãn: giá trị").</summary>
    public IReadOnlyList<DetailLine> Detail
    {
        get
        {
            if (Selected?.Setting is not { } s)
            {
                return [];
            }
            var list = new List<DetailLine>
            {
                new DetailLine("Mô tả", s.ShortDesc + (s.ExtraDesc.Length > 0 ? " " + s.ExtraDesc : string.Empty)),
                new("Nhóm", $"{s.Group} ({s.Category})"),
                new("Đang áp", s.Value + (s.PendingRestart ? "  (đã ghi giá trị mới, chờ khởi động lại)" : string.Empty)),
                new("Mặc định", s.DefaultText),
                new("Nguồn", s.SourceText),
                new("Áp dụng khi", s.ContextText),
                new("Cho phép", s.RangeText),
            };
            var overrides = _config?.Overrides.Where(o => o.Name == s.Name).ToList() ?? [];
            if (overrides.Count > 0)
            {
                list.Add(new("Đặt riêng", string.Join("; ", overrides.Select(o => $"{o.Where} = {o.Value}"))
                    + " - \"Đang áp\" là giá trị trong phiên của DbTools (database bảo trì postgres / template1, user của hồ sơ), có thể đã bị giá trị đặt riêng đè."));
            }
            if (Selected.Pending is { } p)
            {
                list.Add(new("Chờ áp dụng", p.Reset ? "về mặc định" : p.NewValue!));
            }
            return list;
        }
    }

    /// <summary>Trang gắn vào: hỏi lại trước khi ghi lên server.</summary>
    public Func<RunConfirmation, Task<bool>>? Confirm { get; set; }

    public ServerConfigViewModel()
    {
        Profile = AppState.LastProfile;
        Group = Groups[0];
    }

    public void EnsureProfile()
    {
        if (Profile is null || !Profiles.Contains(Profile))
        {
            Profile = AppState.LastProfile;
        }
    }

    /// <summary>Nhận lối tắt từ trang khác: đổi hồ sơ, đọc cấu hình, mở sẵn nhóm.</summary>
    public async Task TakeRequestAsync()
    {
        if (Request is not { } r)
        {
            return;
        }
        Request = null;
        // Mở để xem cả nhóm: bỏ chữ tìm / ô lọc còn lại từ lần trước.
        SearchText = string.Empty;
        ChangedOnly = AlterSystemOnly = RestartOnly = OverriddenOnly = false;
        Profile = r.Profile;
        await RefreshAsync();
        Group = Groups.FirstOrDefault(g => g.Key == r.GroupKey) ?? Groups[0];
    }

    partial void OnProfileChanged(ConnectionProfile? value)
    {
        _config = null;
        _pending.Clear();
        UpdatePending();
        Rows.Clear();
        Selected = null;
        CountText = string.Empty;
        SetStatus("Bấm Làm mới để đọc cấu hình của server này.", false);
    }

    partial void OnGroupChanged(SettingGroup? value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnChangedOnlyChanged(bool value) => ApplyFilter();

    partial void OnAlterSystemOnlyChanged(bool value) => ApplyFilter();

    partial void OnRestartOnlyChanged(bool value) => ApplyFilter();

    partial void OnOverriddenOnlyChanged(bool value) => ApplyFilter();

    partial void OnSelectedChanged(SettingRow? value)
    {
        EditOptions.Clear();
        if (value?.Setting is not { } s)
        {
            EditText = string.Empty;
            return;
        }
        foreach (var o in s.VarType == "bool" ? ["on", "off"] : s.EnumValues)
        {
            EditOptions.Add(o);
        }
        EditText = value.Pending is { Reset: false } p ? p.NewValue! : s.Value;
        OnPropertyChanged(nameof(EditChoice)); // danh sách vừa đổi: ô chọn phải chọn lại dù chữ không đổi
    }

    private void SetStatus(string text, bool error)
    {
        Status = text;
        StatusIsError = error;
    }

    private void ApplyFilter()
    {
        if (_config is null)
        {
            return;
        }
        var keep = Selected?.Name;
        var overridden = _config.Overrides.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        var list = ServerSettings.Filter(_config.Settings, SearchText, Group?.Key, ChangedOnly, AlterSystemOnly, RestartOnly, overridden, OverriddenOnly).ToList();
        Rows.Clear();
        foreach (var s in list)
        {
            Rows.Add(new SettingRow(s, _pending.GetValueOrDefault(s.Name), overridden.Contains(s.Name)));
        }
        CountText = $"{Rows.Count:N0} / {_config.Settings.Count:N0} thông số";
        Selected = Rows.FirstOrDefault(r => r.Name == keep);
    }

    private bool CanRefresh => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (Profile is not { } profile)
        {
            SetStatus("Chưa có kết nối - thêm ở trang Kết nối.", true);
            return;
        }
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(30));
            var config = await PgSettings.LoadAsync(profile, cts.Token);
            Load(config);
            _pending.Clear();
            UpdatePending();
            ApplyFilter();
            SetStatus($"PostgreSQL {config.Version} · {profile.Host}:{profile.Port} · {config.Settings.Count(s => s.ByAlterSystem)} thông số đặt bằng ALTER SYSTEM, "
                + $"{config.Settings.Count(s => s.PendingRestart)} chờ khởi động lại"
                + (config.Superuser ? string.Empty : " · user không phải superuser: chỉ xem được (đổi sẽ bị từ chối trừ khi được GRANT ALTER SYSTEM)"), false);
        }
        catch (Exception ex)
        {
            SetStatus("Không đọc được cấu hình: " + Pg.Describe(ex), true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Load(ServerConfig config)
    {
        _config = config;
        var current = Group?.Key;
        var groups = config.Settings.Select(s => s.GroupKey).Distinct().OrderBy(ServerSettings.GroupOrder).ThenBy(k => k, StringComparer.Ordinal).ToList();
        if (!groups.SequenceEqual(Groups.Skip(1).Select(g => g.Key!)))
        {
            while (Groups.Count > 1)
            {
                Groups.RemoveAt(1);
            }
            foreach (var key in groups)
            {
                Groups.Add(new SettingGroup(key, $"{ServerSettings.GroupName(key)} ({config.Settings.Count(s => s.GroupKey == key)})"));
            }
            Group = Groups.FirstOrDefault(g => g.Key == current) ?? Groups[0];
        }
    }

    /// <summary>Đưa giá trị trong ô sửa vào danh sách chờ áp dụng (bằng giá trị đang áp thì bỏ khỏi danh sách).</summary>
    [RelayCommand]
    private void Stage()
    {
        if (Selected?.Setting is not { } s)
        {
            return;
        }
        var (value, error) = ServerSettings.Normalize(s, EditText);
        if (error is not null)
        {
            SetStatus(error, true);
            return;
        }
        if (string.Equals(value, s.Value, StringComparison.Ordinal))
        {
            _pending.Remove(s.Name);
            SetStatus($"{s.Name}: bằng giá trị đang áp - không đổi.", false);
        }
        else
        {
            _pending[s.Name] = new SettingChange(s, value);
            SetStatus($"{s.Name} → {value}: chờ áp dụng (bấm Áp dụng ở dưới).", false);
        }
        UpdatePending();
        ApplyFilter();
    }

    /// <summary>Về mặc định: bỏ dòng ALTER SYSTEM của thông số đang chọn (chờ áp dụng).</summary>
    [RelayCommand]
    private void StageReset()
    {
        if (Selected?.Setting is not { ByAlterSystem: true } s)
        {
            return;
        }
        _pending[s.Name] = new SettingChange(s, null);
        SetStatus($"{s.Name}: về mặc định - chờ áp dụng.", false);
        UpdatePending();
        ApplyFilter();
    }

    /// <summary>Về mặc định mọi thông số đang đặt bằng ALTER SYSTEM (chờ áp dụng).</summary>
    [RelayCommand]
    private void StageResetAll()
    {
        if (_config is null)
        {
            return;
        }
        var list = _config.Settings.Where(s => s.ByAlterSystem).ToList();
        if (list.Count == 0)
        {
            SetStatus("Không có thông số nào đặt bằng ALTER SYSTEM (hoặc user không xem được nguồn).", false);
            return;
        }
        foreach (var s in list)
        {
            _pending[s.Name] = new SettingChange(s, null);
        }
        SetStatus($"{list.Count} thông số về mặc định - chờ áp dụng.", false);
        UpdatePending();
        ApplyFilter();
    }

    [RelayCommand]
    private void Unstage()
    {
        if (Selected is { } row && _pending.Remove(row.Name))
        {
            UpdatePending();
            ApplyFilter();
        }
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void ClearPending()
    {
        _pending.Clear();
        UpdatePending();
        ApplyFilter();
        SetStatus("Đã bỏ mọi thay đổi chờ áp dụng.", false);
    }

    private void UpdatePending()
    {
        var restart = _pending.Values.Count(c => c.Setting.NeedsRestart && !c.Reset);
        PendingText = _pending.Count == 0 ? string.Empty
            : $"Chờ áp dụng {_pending.Count} thay đổi{(restart > 0 ? $" ({restart} cần khởi động lại server)" : string.Empty)}: "
              + string.Join(", ", _pending.Values.Select(c => c.Reset ? c.Setting.Name + " → mặc định" : $"{c.Setting.Name} → {c.NewValue}"));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(NoPending));
    }

    private bool CanApply => !IsBusy && HasPending;

    /// <summary>Hỏi lại → ALTER SYSTEM SET / RESET + pg_reload_conf → đọc lại, báo từng thông số đã áp / chờ khởi động lại.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (Profile is not { } profile || _config is null || _pending.Count == 0)
        {
            return;
        }
        var changes = _pending.Values.OrderBy(c => c.Setting.Name, StringComparer.Ordinal).ToList();
        var statements = changes.Select(ServerSettings.Statement).ToList();
        var lines = new List<string>
        {
            $"Server: {profile.Host}:{profile.Port} (user {profile.Username}) - áp cho CẢ SERVER (mọi database), ghi vào postgresql.auto.conf trên máy server rồi nạp lại cấu hình.",
        };
        lines.AddRange(changes.Select(c => "  " + c.Describe() + (c.Setting.NeedsRestart && !c.Reset ? "  (cần khởi động lại)" : string.Empty)));
        lines.AddRange(ServerSettings.Warnings(changes, _config.Overrides));
        lines.Add("Muốn trả lại như cũ: chọn thông số → Về mặc định (hoặc Về mặc định tất cả) → Áp dụng.");
        lines.Add("Câu sẽ chạy: " + string.Join("; ", statements) + "; SELECT pg_reload_conf()");
        if (Confirm is not null && !await Confirm(new RunConfirmation($"Đổi {changes.Count} thông số của server?", lines, "Áp dụng",
                ProductionGuard.ForServerConfig(profile.IsProduction, profile.Database, profile.Name))))
        {
            return;
        }

        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(30));
            var (after, done, error) = await PgSettings.ApplyAsync(profile, statements, cts.Token);
            _log.LogInformation("Cấu hình server {Host}:{Port}: {Done}/{Count} câu {Statements} {Error}", profile.Host, profile.Port, done, statements.Count,
                string.Join("; ", statements.Take(done)), error?.Message);
            var byName = after.Settings.ToDictionary(s => s.Name);
            var report = changes.Take(done).Select(c => byName.TryGetValue(c.Setting.Name, out var s)
                ? $"{s.Name} = {s.Value}{(s.PendingRestart ? " (chờ khởi động lại)" : string.Empty)}"
                : c.Setting.Name).ToList();
            Load(after);
            foreach (var c in changes.Take(done))
            {
                _pending.Remove(c.Setting.Name);
            }
            if (error is not null)
            {
                // Câu lỗi + các câu sau giữ lại trong danh sách chờ (cập nhật theo giá trị mới đọc).
                foreach (var c in changes.Skip(done))
                {
                    if (byName.TryGetValue(c.Setting.Name, out var s))
                    {
                        _pending[c.Setting.Name] = c with { Setting = s };
                    }
                }
            }
            UpdatePending();
            ApplyFilter();
            if (error is null)
            {
                SetStatus($"Đã áp dụng {done} thay đổi: {string.Join(", ", report)}.", false);
            }
            else
            {
                var failed = changes[done].Setting.Name;
                SetStatus((done > 0 ? $"Đã áp dụng {done} thay đổi ({string.Join(", ", report)}); " : string.Empty)
                    + $"{failed} lỗi: {Pg.Describe(error)}"
                    + (error is Npgsql.PostgresException { SqlState: "42501" } ? " - cần superuser (PostgreSQL 15+: hoặc GRANT ALTER SYSTEM ON PARAMETER ...)" : string.Empty)
                    + ". Các thay đổi chưa chạy vẫn ở danh sách chờ.", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Không áp dụng được: " + Pg.Describe(ex), true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
